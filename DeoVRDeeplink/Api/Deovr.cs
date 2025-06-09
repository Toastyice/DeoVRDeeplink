namespace DeoVRDeeplink.Api;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.AspNetCore.Http;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using Model;

//Include library in /deovr optin
//Link to all movies
//1. Get the library to work
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
    [Produces("application/json")]
    public IActionResult GetScenes()
    {
        try
        {
            var baseUrl = GetServerUrl();
            var libraries = GetAllLibraries();

            if (!libraries.Any())
            {
                _logger.LogWarning("No libraries found");
                return Ok(new DeoVrScenesResponse());
            }

            var response = new DeoVrScenesResponse();

            foreach (var library in libraries)
            {
                var videos = GetVideosFromLibrary(library);
            
                if (!videos.Any())
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
    /// <summary>
    /// Returns scenes from a specific library by name
    /// </summary>
    [HttpGet("library/{libraryName}")]
    [Produces("application/json")]
    public IActionResult GetScenesFromLibrary(string libraryName)
    {
        try
        {
            var baseUrl = GetServerUrl();
            var library = GetLibraryByName(libraryName);

            if (library == null)
            {
                _logger.LogWarning("Library not found: {LibraryName}", libraryName);
                return NotFound($"Library '{libraryName}' not found");
            }

            var videos = GetVideosFromLibrary(library);
            
            var videoList = videos.Select<Video, object>(video => new
            {
                title = video.Name,
                videoLength = GetVideoDuration(video),
                video_url = $"{baseUrl}/DeoVRDeeplink/json/{video.Id}/response.json",
                thumbnailUrl = $"{baseUrl}/Items/{video.Id}/Images/Backdrop"
            }).ToArray();

            var response = new
            {
                scenes = new[]
                {
                    new
                    {
                        name = library.Name,
                        list = videoList
                    }
                }
            };

            _logger.LogInformation("Generated DeoVR response with {Count} videos from library: {LibraryName}", 
                videoList.Length, library.Name);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating DeoVR scenes JSON for library: {LibraryName}", libraryName);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                $"Error generating DeoVR scenes for library: {libraryName}");
        }
    }

    /// <summary>
    /// Returns a list of all available libraries
    /// </summary>
    [HttpGet("libraries")]
    [Produces("application/json")]
    public IActionResult GetLibraries()
    {
        try
        {
            var libraries = GetAllLibraries().Select(library => new
            {
                name = library.Name,
                id = library.Id,
                videoCount = GetVideosFromLibrary(library).Count()
            }).ToArray();

            _logger.LogInformation("Retrieved {Count} libraries", libraries.Length);
            return Ok(new { libraries });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving libraries");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving libraries");
        }
    }

    private IEnumerable<Folder> GetAllLibraries()
    {
        return _libraryManager.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>();
    }

    private Folder? GetLibraryByName(string libraryName)
    {
        return _libraryManager.RootFolder
            .Children
            .OfType<Folder>()
            .FirstOrDefault(folder => 
                folder.Name.Equals(libraryName, StringComparison.InvariantCultureIgnoreCase));
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
