using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.AspNetCore.Http;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using DeoVRDeeplink.Model;

namespace DeoVRDeeplink.Api;

//Include library in /deovr optin?
//Link to all movies
//1. Get the library to work done
//2. Get additional metadata to work
//3. Trickplay images possible?

[ApiController]
[Route("deovr")]
public class DeoVrController : ControllerBase
{
    private readonly ILogger<DeoVrController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeoVrController(
        ILogger<DeoVrController> logger,
        ILibraryManager libraryManager,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager config,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Returns a JSON structure compatible with DeoVR deeplinks
    /// </summary>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    public IActionResult GetScenes()
    {
        try
        {
            var baseUrl = GetServerUrl();
            var libraries = GetAllLibraries().ToArray();
            
            if (libraries.Length == 0)
            {
                _logger.LogWarning("No libraries found");
                return Ok(new DeoVrScenesResponse());
            }

            var response = new DeoVrScenesResponse();

            foreach (var library in libraries)
            {
                var videos = GetVideosFromLibrary(library).ToArray();
                
                if (videos.Length == 0)
                {
                    _logger.LogDebug("No videos found in library: {LibraryName}", library.Name);
                    continue;
                }

                var videoList = videos.Select(video => new DeoVrVideoItem
                {
                    Title = video.Name,
                    VideoLength = GetVideoDuration(video),
                    VideoUrl = $"{baseUrl}/DeoVRDeeplink/json/{video.Id}/response.json",
                    ThumbnailUrl = $"{baseUrl}/Items/{video.Id}/Images/Backdrop"
                }).ToList();

                var scene = new DeoVrScene
                {
                    Name = library.Name,
                    List = videoList
                };

                response.Scenes.Add(scene);

                _logger.LogInformation("Added {Count} videos from library: {LibraryName}", 
                    videoList.Count, library.Name);
            }

            var totalVideos = response.Scenes.Sum(scene => scene.List.Count);
            _logger.LogInformation("Generated DeoVR response with {TotalCount} videos from {LibraryCount} libraries", 
                totalVideos, response.Scenes.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating DeoVR scenes JSON");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error generating DeoVR scenes");
        }
    }

    private IEnumerable<Folder> GetAllLibraries()
    {
        return _libraryManager.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>();
    }

    private IEnumerable<Video> GetVideosFromLibrary(Folder library)
    {
        var query = new InternalItemsQuery
        {
            ParentId = library.Id,
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            IsFolder = false
        };

        return _libraryManager.GetItemList(query).OfType<Video>();
    }
    
    private static int GetVideoDuration(BaseItem video)
    {
        if (video is Video videoItem && videoItem.RunTimeTicks.HasValue)
        {
            return (int)(videoItem.RunTimeTicks.Value / TimeSpan.TicksPerSecond);
        }
        return 0;
    }
    
    // Gets accessible server URL from current context
    private string GetServerUrl()
    {
        var req = _httpContextAccessor.HttpContext?.Request;
        return req == null ? "" : $"{req.Scheme}://{req.Host}{req.PathBase}";
    }
}
