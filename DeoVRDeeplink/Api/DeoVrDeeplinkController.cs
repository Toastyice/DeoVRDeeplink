namespace DeoVRDeeplink.Api;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using DeoVRDeeplink.Configuration;

[ApiController]
[Route("DeoVRDeeplink")]
public class DeoVrDeeplinkController : ControllerBase
{
    private readonly ILogger<DeoVrDeeplinkController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    private readonly IServerConfigurationManager _config;

    private readonly string _clientScriptResourcePath =
        $"{DeoVrDeeplinkPlugin.Instance?.GetType().Namespace}.Web.DeoVRClient.js";

    public DeoVrDeeplinkController(
        ILogger<DeoVrDeeplinkController> logger,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IMediaEncoder mediaEncoder,
        IHttpContextAccessor httpContextAccessor,
        IServerConfigurationManager config)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _mediaEncoder = mediaEncoder;
        _httpContextAccessor = httpContextAccessor;
        _config = config;
    }

    /// <summary>Serves embedded client JavaScript.</summary>
    [HttpGet("ClientScript")]
    [Produces("application/javascript")]
    public IActionResult GetClientScript()
    {
        try
        {
            var stream = _assembly.GetManifestResourceStream(_clientScriptResourcePath);
            if (stream == null)
            {
                _logger.LogError("Resource not found: {Path}", _clientScriptResourcePath);
                return NotFound();
            }

            return File(stream, "application/javascript");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving client script resource.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving script resource.");
        }
    }

    /// <summary>Serves the icon image.</summary>
    [HttpGet("Icon")]
    [AllowAnonymous]
    public IActionResult GetIcon()
    {
        const string resourceName = "DeoVRDeeplink.Web.Icon.png";
        try
        {
            var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return NotFound();

            return File(stream, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving icon resource.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving icon resource.");
        }
    }

    /// <summary>
    /// Returns DeoVR compatible JSON for a movie.
    /// </summary>
    [HttpGet("json/{movieId}/response.json")]
    [Produces("application/json")]
    public async Task<IActionResult> GetDeoVrResponse(string movieId)
    {
        try
        {
            var response = await BuildVideoResponse(movieId);
            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating DeoVR response for movie ID: {MovieId}", movieId);
            return StatusCode(StatusCodes.Status500InternalServerError, "Error generating DeoVR response.");
        }
    }

    /// <summary>
    /// Helper to sign a proxy url token
    /// </summary>
    private static string SignUrl(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        // Hex string!
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task<DeoVrVideoResponse?> BuildVideoResponse(string movieId)
    {
        if (!Guid.TryParse(movieId, out var itemId))
            return null;

        var item = _libraryManager.GetItemById(itemId);
        if (item is not Video video)
            return null;

        // Try to extract video stream info
        var stream = _mediaSourceManager.GetStaticMediaSources(video, false)
            .FirstOrDefault()?.MediaStreams
            .FirstOrDefault(s => s.Type == MediaStreamType.Video);

        var resolution = stream?.Height ?? 2160;
        var codec = stream?.Codec ?? "h264";

        var runtimeSeconds = (int)((video.RunTimeTicks ?? 0) / 10_000_000);

        var (stereoMode, screenType) = Get3DType(video, DeoVrDeeplinkPlugin.Instance!.Configuration);

        var baseUrl = GetServerUrl();

        // NEW: Proxy link with short-lived token
        var proxySecret = DeoVrDeeplinkPlugin.Instance?.Configuration.ProxySecret ?? "default-secret";
        var expiry = DateTimeOffset.UtcNow.AddSeconds(runtimeSeconds * 2).ToUnixTimeSeconds();
        var tokenData = $"{movieId}:{expiry}";
        var sig = SignUrl(tokenData, proxySecret);
        // /DeoVRDeeplink/proxy/{movieId}/{expiry}/{sig}/stream.mp4
        var streamUrl = $"{baseUrl}/DeoVRDeeplink/proxy/{movieId}/{expiry}/{sig}/stream.mp4";

        var response = new DeoVrVideoResponse
        {
            Id = itemId.GetHashCode(),
            Title = video.Name?.Split(' ')[0] ?? "Unknown",
            Is3D = true,
            VideoLength = runtimeSeconds,
            ScreenType = screenType,
            StereoMode = stereoMode,
            ThumbnailUrl = $"{baseUrl}/Items/{itemId}/Images/Primary",
            VideoThumbnail = $"{baseUrl}/Items/{itemId}/Images/Primary",
            Encodings =
            [
                new DeoVrEncoding
                {
                    Name = codec,
                    VideoSources =
                    [
                        new DeoVrVideoSource
                        {
                            Resolution = resolution,
                            Url = streamUrl
                        }
                    ]
                }
            ],
            Timestamps = await GetDeoVrTimestampsAsync(video),
            Corrections = new DeoVrCorrections()
        };
        return response;
    }

    /// <summary>
    /// Retrieves chapter timestamps, in seconds, for the item.
    /// </summary>
    private async Task<List<DeoVrTimestamps>> GetDeoVrTimestampsAsync(BaseItem item)
    {
        var source = item.GetMediaSources(false).FirstOrDefault();
        var info = await _mediaEncoder.GetMediaInfo(
            new MediaInfoRequest
            {
                MediaSource = source,
                MediaType = DlnaProfileType.Video,
                ExtractChapters = true,
            },
            CancellationToken.None);

        return info.Chapters?
            .Select(ch => new DeoVrTimestamps
            {
                ts = (int)(ch.StartPositionTicks / 10_000_000),
                name = ch.Name
            }).ToList() ?? [];
    }

    // Returns VR display type

    private static (string StereoMode, string ScreenType) Get3DType(Video video, PluginConfiguration config)
    {
        return video.Video3DFormat switch
        {
            Video3DFormat.FullSideBySide   => ("sbs", "sphere"),
            Video3DFormat.FullTopAndBottom => ("tb", "sphere"),
            Video3DFormat.HalfSideBySide   => ("sbs", "dome"),
            Video3DFormat.HalfTopAndBottom => ("tb", "dome"),
            _ => (
                config.FallbackStereoMode switch
                {
                    StereoMode.SideBySide => "sbs",
                    StereoMode.TopBottom  => "tb",
                    _ => "off"
                },
                config.FallbackProjection switch
                {
                    ProjectionType.Projection180 => "dome",
                    ProjectionType.Projection360 => "sphere",
                    _ => "flat"
                }
            )
        };
    }

    // Gets accessible server URL from current context
    private string GetServerUrl()
    {
        var req = _httpContextAccessor.HttpContext?.Request;
        return req == null ? "" : $"{req.Scheme}://{req.Host}{req.PathBase}";
    }
        
    /// <summary>
    /// Securely proxies video streams with signed, expiring tokens.
    /// </summary>
    [HttpGet("proxy/{movieId}/{expiry}/{signature}/stream.mp4")]
    [AllowAnonymous]
    public async Task ProxyStream(string movieId, long expiry, string signature)
    {
        // Validate movieId format
        if (!Guid.TryParse(movieId, out var itemGuid))
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
        var dataToSign = $"{movieId}:{expiry}";
        var expected = SignUrl(dataToSign, proxySecret);
        if (!string.Equals(signature, expected, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Proxy signature mismatch. Provided: {UserSig}, Expected: {ExpectedSig}, movieId: {MovieId}, expiry: {Expiry}",
                signature, expected, movieId, expiry);

            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.Body.FlushAsync();
            return;
        }

        // Prepare Jellyfin endpoint (should be local for performance)
        var jellyfinApiKey = DeoVrDeeplinkPlugin.Instance?.Configuration.ApiKey;
        var jellyfinInternalBaseUrl = GetJellyfinInternalBaseUrl();
        var jellyfinUrl =
            $"{jellyfinInternalBaseUrl}/Videos/{movieId}/stream.mp4?Static=true&mediaSourceId={movieId}&deviceId=DeoVRDeeplink_v1";

        // Use a static/shared HttpClient for best performance
        var httpClient = StaticHttpClient.Instance;

        var forwardRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, jellyfinUrl);
        forwardRequest.Headers.Add("X-Emby-Token", jellyfinApiKey);

        // Forward the Range header for seeking
        if (Request.Headers.TryGetValue("Range", out var rangeValues))
        {
            foreach (var value in rangeValues)
                forwardRequest.Headers.TryAddWithoutValidation("Range", value);
        }

        using var resp = await httpClient.SendAsync(forwardRequest, HttpCompletionOption.ResponseHeadersRead);

        Response.StatusCode = (int)resp.StatusCode;

        // Copy all headers from Jellyfin response to our response
        foreach (var header in resp.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in resp.Content.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();

        // Remove headers that should not be set by user code
        Response.Headers.Remove("transfer-encoding");

        // Proxy the content stream in large chunks for performance
        await using var stream = await resp.Content.ReadAsStreamAsync();
        var buffer = new byte[2 * 1024 * 1024]; // 2 MB chunks
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            await Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead));
            await Response.Body.FlushAsync();
        }
    }
    private string GetJellyfinInternalBaseUrl()
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
    public static readonly HttpClient Instance = new HttpClient();
}
