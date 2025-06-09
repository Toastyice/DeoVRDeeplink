namespace DeoVRDeeplink.Api;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;

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
            var baseUrl = "https://jellyfin-dev.schella.io";
            var vrLibrary = GetVrLibrary();

            var videos = GetVideosFromVrLibrary(vrLibrary);
        
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
                        name = vrLibrary.Name,
                        list = videoList
                    }
                }
            };

            _logger.LogInformation("Generated DeoVR response with {Count} videos from VR library", videoList.Length);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating DeoVR scenes JSON");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error generating DeoVR scenes");
        }
    }
    
    private Folder GetVrLibrary()
    {
        return _libraryManager.RootFolder
            .Children
            .OfType<Folder>().FirstOrDefault(folder => folder.Name.Equals("VR", StringComparison.InvariantCultureIgnoreCase)) 
               ?? new Folder();
    }
    
    private IEnumerable<Video> GetVideosFromVrLibrary(Folder vrLibrary)
    {
        var query = new InternalItemsQuery
        {
            ParentId = vrLibrary.Id,
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
}
