using System.Net.Mime;
using DeoVRDeeplink.Configuration;
using DeoVRDeeplink.Utilities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
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
    IItemRepository itemRepository,
    IChapterRepository chapterRepository) : ControllerBase
{
    private readonly IChapterRepository _chapterRepository = chapterRepository;
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
        var baseUrl = UrlHelper.GetServerUrl(_httpContextAccessor.HttpContext);

        switch (item)
        {
            case Video video:
                try
                {
                    var libConfig = GetLibraryConfigForItem(video);
                    var response =
                        DeoVrResponseBuilder.BuildVideoResponse(video, baseUrl, libConfig, _chapterRepository, _logger);
                    return Ok(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating DeoVR response for movie ID: {Id}", Id);
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error generating DeoVR response.");
                }
            case Person person:
                try
                {
                    var response = DeoVrResponseBuilder.BuildActorResponse(person, baseUrl, _libraryManager, _logger);
                    return Ok(response);
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

    private LibraryConfiguration? GetLibraryConfigForItem(BaseItem item)
    {
        var config = DeoVrDeeplinkPlugin.Instance!.Configuration;
        var libraries = config.Libraries;
        
        var collectionFolder = _libraryManager.GetCollectionFolders(item).FirstOrDefault();
        if (collectionFolder == null)
        {
            _logger.LogWarning("No collection folder found for item {ItemName} (Id: {ItemId})", item.Name, item.Id);
            return null;
        }
        
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
    ///     Securely proxies video streams with signed, expiring tokens.
    /// </summary>
    [HttpGet("proxy/{token}/stream.mp4")]
    [AllowAnonymous]
    public async Task ProxyStream(string token)
    {
        // Validate and extract token
        if (!SignatureValidator.ValidateAndExtractToken(token, DeoVrDeeplinkPlugin.ProxySecret,
                out var movieId, out var mediaSourceId, out _))
        {
            _logger.LogWarning("Proxy authentication failed for token");
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.Body.FlushAsync();
            return;
        }
        
        if (!Guid.TryParse(movieId, out _))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.Body.FlushAsync();
            return;
        }
        
        var jellyfinInternalBaseUrl = UrlHelper.GetInternalBaseUrl(_config);
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