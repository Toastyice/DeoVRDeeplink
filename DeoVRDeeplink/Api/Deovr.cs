using System.Net.Mime;
using DeoVRDeeplink.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using DeoVRDeeplink.Model;
using DeoVRDeeplink.Utilities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Entities;

namespace DeoVRDeeplink.Api;

[ApiController]
[Route("deovr")]
public class DeoVrController : ControllerBase
{
    private readonly ILogger<DeoVrController> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServerConfigurationManager _config;

    public DeoVrController(
        ILogger<DeoVrController> logger,
        ILibraryManager libraryManager,
        IHttpContextAccessor httpContextAccessor,
        IServerConfigurationManager config)
    {
        _logger = logger;
        _libraryManager = libraryManager;
        _httpContextAccessor = httpContextAccessor;
        _config = config;
    }

    /// <summary>
    /// Returns a JSON structure compatible with DeoVR deeplinks
    /// </summary>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    [IpWhitelist]
    public IActionResult GetScenes()
    {
        try
        {
            var baseUrl = GetServerUrl();
            var configLibraries = DeoVrDeeplinkPlugin.Instance!.Configuration.Libraries;
            var libraries = GetAllEnabledLibraries(configLibraries).ToArray();
            
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
                    VideoUrl = $"{baseUrl}/deovr/json/{video.Id}/response.json",
                    ThumbnailUrl = GetImageUrlWithFallback(video, ImageType.Backdrop, baseUrl) ?? string.Empty
                }).ToList();

                var scene = new DeoVrScene
                {
                    Name = library.Name,
                    List = videoList
                };

                response.Scenes.Add(scene);

                _logger.LogInformation("Added {Count} videos from library: {LibraryName}", 
                    videoList.Count, library.Name);
                
                // Check if this library has the random flag enabled
                if (configLibraries.FirstOrDefault(config => config.Id == library.Id) is { Enabled: true, Random: true })
                {
                    // Create a randomized duplicate of the video list
                    var random = new Random();
                    var randomizedVideoList = videoList.OrderBy(x => random.Next()).ToList();

                    var randomScene = new DeoVrScene
                    {
                        Name = $"{library.Name} - Random",
                        List = randomizedVideoList
                    };

                    response.Scenes.Add(randomScene);

                    _logger.LogInformation("Added randomized scene with {Count} videos for library: {LibraryName}", 
                        randomizedVideoList.Count, library.Name);
                }
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

    private IEnumerable<Folder> GetAllEnabledLibraries(IList<LibraryConfiguration> configLibraries)
    {
        var allLibraries = _libraryManager.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>();

        var enabledLibraryIds = configLibraries
            .Where(lib => lib.Enabled)
            .Select(lib => lib.Id);

        return allLibraries.Where(lib => enabledLibraryIds.Contains(lib.Id));
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
    
    private static string? GetImageUrlWithFallback(BaseItem item, ImageType preferredType, string baseUrl)
    {
        return TryGetImageUrl(item, preferredType, baseUrl) ?? TryGetImageUrl(item, ImageType.Primary, baseUrl);
    }

    private static string? TryGetImageUrl(BaseItem item, ImageType imageType, string baseUrl)
    {
        return Array.Find(item.ImageInfos, img => img.Type == imageType && IsValidImage(img)) != null
            ? $"{baseUrl}/Items/{item.Id}/Images/{imageType}"
            : null;
    }

    private static bool IsValidImage(ItemImageInfo img)
    {
        return !string.IsNullOrEmpty(img.Path) &&
               (!img.IsLocalFile || img is { Width: > 0, Height: > 0 });
    }

}
