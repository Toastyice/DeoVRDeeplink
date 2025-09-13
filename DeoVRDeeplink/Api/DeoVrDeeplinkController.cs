using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using DeoVRDeeplink.Configuration;
using DeoVRDeeplink.Model;
using DeoVRDeeplink.Utilities;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DeoVRDeeplink.Api;

[ApiController]
[Route("deovr")]
public class DeoVrDeeplinkController(
    ILogger<DeoVrDeeplinkController> logger,
    ILibraryManager libraryManager,
    IMediaSourceManager mediaSourceManager,
    IHttpContextAccessor httpContextAccessor,
    IServerConfigurationManager config,
    IItemRepository itemRepository) : ControllerBase
{
    private readonly IServerConfigurationManager _config = config;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IItemRepository _itemRepository = itemRepository;
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly ILogger<DeoVrDeeplinkController> _logger = logger;
    private readonly IMediaSourceManager _mediaSourceManager = mediaSourceManager;

    /// <summary>
    ///     Returns DeoVR compatible JSON for a movie or Person.
    /// </summary>
    [HttpGet("json/{Id}/response.json")]
    [Produces(MediaTypeNames.Application.Json)]
    [IpWhitelist]
    public IActionResult GetDeoVrResponse(string Id)
    {
        if (!Guid.TryParse(Id, out var itemId))
            return NotFound();

        var item = _libraryManager.GetItemById(itemId);
        switch (item)
        {
            case Video video:
                try
                {
                    var response = BuildVideoResponse(video);
                    return response is null ? NotFound() : Ok(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating DeoVR response for movie ID: {Id}", Id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error generating DeoVR response.");
                }
            case Person person:
                try
                {
                    var response = BuildActorResponse(person);
                    return response is null ? NotFound() : Ok(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating DeoVR response for Actor ID: {Id}", Id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error generating DeoVR response.");
                }
            default:
                return NotFound();
        }
    }

    private DeoVrScenesResponse? BuildActorResponse(Person person)
    {
        var query = new InternalItemsQuery
        {
            PersonIds = [person.Id],
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsFolder = false
        };
        var baseUrl = GetServerUrl();
        var response = new DeoVrScenesResponse();
        var videoList = _libraryManager
            .GetItemList(query)
            .OfType<Video>()
            .Select(video => new DeoVrVideoItem
            {
                Title = video.Name,
                VideoLength = (int)((video.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond),
                VideoUrl = $"{baseUrl}/deovr/json/{video.Id}/response.json",
                ThumbnailUrl = GetImageUrlWithFallback(video, ImageType.Backdrop, baseUrl) ?? string.Empty
            }).ToList();
        var scene = new DeoVrScene
        {
            Name = person.Name,
            List = videoList
        };

        response.Scenes.Add(scene);
        _logger.LogInformation("Added {Count} videos from library: {Person}",
            videoList.Count, person.Name);
        return response;
    }

    /// <summary>
    ///     Helper to sign a proxy url token
    /// </summary>
    private static string SignUrl(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private DeoVrVideoResponse? BuildVideoResponse(Video video)
    {
        var runtimeSeconds = (int)((video.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond);

        var libConfig = GetLibraryConfigForItem(video);
        var fallbackStereo = libConfig?.FallbackStereoMode ?? StereoMode.None;
        var fallbackProjection = libConfig?.FallbackProjection ?? ProjectionType.None;

        var baseUrl = GetServerUrl();
        var proxySecret = DeoVrDeeplinkPlugin.Instance!.Configuration.ProxySecret;
        var expiry = DateTimeOffset.UtcNow.AddSeconds(runtimeSeconds * 2).ToUnixTimeSeconds();

        var thumbnailUrl = GetImageUrlWithFallback(video, ImageType.Backdrop, baseUrl);
        var (stereoMode, screenType) = Get3DType(video, fallbackStereo, fallbackProjection);

        var encodings = video.GetMediaSources(false)
            .GroupBy(ms => ms.VideoStream.Codec ?? "unknown")
            .Select(g => new DeoVrEncoding
            {
                Name = g.Key,
                VideoSources = g.Select(ms => new DeoVrVideoSource
                {
                    Resolution = ms.VideoStream?.Height ?? 2160,
                    Url = $"{baseUrl}/deovr/proxy/{video.Id}/{ms.Id}/{expiry}/{SignUrl($"{video.Id}:{ms.Id}:{expiry}", proxySecret)}/stream.mp4"
                }).ToList()
            }).ToList();

        var response = new DeoVrVideoResponse
        {
            Id = video.Id.GetHashCode(),
            Title = video.Name ?? "Unknown",
            Is3D = true,
            VideoLength = runtimeSeconds,
            ScreenType = screenType,
            StereoMode = stereoMode,
            ThumbnailUrl = thumbnailUrl!,
            TimelinePreview = $"{baseUrl}/deovr/timeline/{video.Id}/4096_timelinePreview341x195.jpg",
            Encodings = encodings,
            Timestamps = GetDeoVrTimestamps(video),
            Corrections = new DeoVrCorrections()
        };

        return response;
    }


    private LibraryConfiguration? GetLibraryConfigForItem(BaseItem item)
    {
        var config = DeoVrDeeplinkPlugin.Instance!.Configuration;
        var libraries = config.Libraries;

        // Jellyfin gives you the containing library (CollectionFolder)
        var collectionFolder = _libraryManager.GetCollectionFolders(item).FirstOrDefault();
        if (collectionFolder == null)
        {
            _logger.LogWarning("No collection folder found for item {ItemName} (Id: {ItemId})", item.Name, item.Id);
            return null;
        }

        // Match your plugin’s configured libraries by GUID
        var lib = libraries.FirstOrDefault(l => l.Id == collectionFolder.Id);
        if (lib != null)
        {
            _logger.LogDebug("Found library config for {CollectionFolderName} (Id: {CollectionFolderId})",
                collectionFolder.Name, collectionFolder.Id);
            return lib;
        }

        _logger.LogWarning("No library config found for library {CollectionFolderName} (Id: {CollectionFolderId})",
            collectionFolder.Name, collectionFolder.Id);
        return null;
    }

    /// <summary>
    ///     Retrieves chapter timestamps, in seconds, for the item.
    /// </summary>
    private List<DeoVrTimestamps> GetDeoVrTimestamps(BaseItem item)
    {
        try
        {
            var chapters = _itemRepository.GetChapters(item);

            if (chapters != null && chapters.Count != 0)
                return chapters
                    .Select(ch => new DeoVrTimestamps
                    {
                        ts = (int)(ch.StartPositionTicks / TimeSpan.TicksPerSecond),
                        name = ch.Name ?? "Untitled Chapter"
                    })
                    .ToList();
            _logger.LogDebug("No chapters found for item {ItemName}", item.Name);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chapters for item {ItemName}", item.Name);
            return [];
        }
    }
    // Returns VR display type

    private static (string StereoMode, string ScreenType) Get3DType(Video video, StereoMode fallbackStereo,
        ProjectionType fallbackProjection)
    {
        return video.Video3DFormat switch
        {
            Video3DFormat.FullSideBySide => ("sbs", "sphere"),
            Video3DFormat.FullTopAndBottom => ("tb", "sphere"),
            Video3DFormat.HalfSideBySide => ("sbs", "dome"),
            Video3DFormat.HalfTopAndBottom => ("tb", "dome"),
            _ => (
                fallbackStereo switch
                {
                    StereoMode.SideBySide => "sbs",
                    StereoMode.TopBottom => "tb",
                    _ => "off"
                },
                fallbackProjection switch
                {
                    ProjectionType.Projection180 => "dome",
                    ProjectionType.Projection360 => "sphere",
                    _ => "flat"
                }
            )
        };
    }


    private static string? GetImageUrlWithFallback(BaseItem item, ImageType preferredType, string baseUrl)
    {
        return TryGetImageUrl(item, preferredType, baseUrl) ?? TryGetImageUrl(item, ImageType.Primary, baseUrl);
    }

    private static string? TryGetImageUrl(BaseItem item, ImageType imageType, string baseUrl)
    {
        return Array.Find(item.ImageInfos, img => img.Type == imageType && IsValidImage(img)) != null
            ? $"{baseUrl}/Items/{item.Id}/Images/{imageType}?fillHeight=235&fillWidth=471&quality=96"
            : null;
    }

    private static bool IsValidImage(ItemImageInfo img)
    {
        return !string.IsNullOrEmpty(img.Path) &&
               (!img.IsLocalFile || img is { Width: > 0, Height: > 0 });
    }

    /// <summary>
    ///     Securely proxies video streams with signed, expiring tokens.
    /// </summary>
    [HttpGet("proxy/{movieId}/{mediaSourceId}/{expiry}/{signature}/stream.mp4")]
    [AllowAnonymous] //fine? has performance problems otherwise
    public async Task ProxyStream(string movieId, string mediaSourceId, long expiry, string signature)
    {
        // Validate movieId format
        if (!Guid.TryParse(movieId, out _))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.Body.FlushAsync();
            return;
        }

        // Validate expiry
        var proxySecret = DeoVrDeeplinkPlugin.Instance?.Configuration.ProxySecret ?? "default-secret";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now > expiry)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.Body.FlushAsync();
            return;
        }

        // Validate signature
        var dataToSign = $"{movieId}:{mediaSourceId}:{expiry}";
        var expected = SignUrl(dataToSign, proxySecret);
        if (!string.Equals(signature, expected, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Proxy signature mismatch. Provided: {UserSig}, Expected: {ExpectedSig}, movieId: {MovieId}, mediaSourceId: {mediaSourceId}, expiry: {Expiry}",
                signature, expected, movieId, mediaSourceId, expiry);

            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.Body.FlushAsync();
            return;
        }

        // Prepare Jellyfin endpoint (should be local for performance)
        var jellyfinInternalBaseUrl = GetInternalBaseUrl();
        var jellyfinUrl =
            $"{jellyfinInternalBaseUrl}/Videos/{movieId}/stream.mp4?Static=true&mediaSourceId={mediaSourceId}&deviceId=DeoVRDeeplink";

        var httpClient = StaticHttpClient.Instance;
        var forwardRequest = new HttpRequestMessage(HttpMethod.Get, jellyfinUrl);

        // Forward the Range header for seeking
        if (Request.Headers.TryGetValue("Range", out var rangeValues))
            foreach (var value in rangeValues)
                forwardRequest.Headers.TryAddWithoutValidation("Range", value);

        using var resp = await httpClient.SendAsync(forwardRequest, HttpCompletionOption.ResponseHeadersRead,
            HttpContext.RequestAborted);

        Response.StatusCode = (int)resp.StatusCode;

        // Copy all headers from Jellyfin response to our response
        foreach (var header in resp.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in resp.Content.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();

        // Remove headers that should not be set by user code
        Response.Headers.Remove("transfer-encoding");

        // Proxy the content stream in large chunks for performance with cancellation support
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[2 * 1024 * 1024]; // 2 MB chunks

        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, HttpContext.RequestAborted)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), HttpContext.RequestAborted);
                if (HttpContext.RequestAborted.IsCancellationRequested)
                    break;
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected during streaming for movie {MovieId}", movieId);
            // This is expected when client disconnects
        }
    }

    // Gets accessible server URL from current context
    private string GetServerUrl()
    {
        var req = _httpContextAccessor.HttpContext?.Request;
        return req == null ? "" : $"{req.Scheme}://{req.Host}{req.PathBase}";
    }

    private string GetInternalBaseUrl()
    {
        var options = _config.GetNetworkConfiguration();
        var httpPort = options.InternalHttpPort;
        var httpsPort = options.InternalHttpsPort;

        var protocol = options.RequireHttps ? "https" : "http";
        var port = options.RequireHttps ? httpsPort : httpPort;

        return $"{protocol}://localhost:{port}";
    }
}

public class StaticHttpClient
{
    private static readonly Lazy<HttpClient> _instance = new(() => new HttpClient
    {
        Timeout = Timeout.InfiniteTimeSpan, // No timeout for streaming
        DefaultRequestHeaders = { ConnectionClose = false }
    });

    public static HttpClient Instance => _instance.Value;
}