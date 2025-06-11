using DeoVRDeeplink.Utilities;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DeoVRDeeplink.Api;

[ApiController]
[Route("deovr")]
public class TimelineController : ControllerBase
{
    private readonly IApplicationPaths _appPaths;
    private readonly ILogger<TimelineController> _logger;

    public TimelineController(ILogger<TimelineController> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        _appPaths = appPaths;
    }

    [HttpGet("timeline/{movieId}/4096_timelinePreview341x195.jpg")]
    [IpWhitelist]
    public async Task<IActionResult> GetTimelineImage(string movieId)
    {
        try
        {
            // Validate movieId is a valid GUID
            if (!Guid.TryParse(movieId, out _)) return BadRequest("Invalid movie ID");

            // Build the file path
            var filePath = Path.Combine(_appPaths.CachePath, "deovr-timeline", $"{movieId}.jpg");

            // Check if file exists
            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogWarning("Timeline image not found: {FilePath}", filePath);
                return NotFound("Timeline image not found");
            }

            // Read and return the file
            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

            _logger.LogDebug("Serving timeline image: {FilePath}", filePath);

            return File(fileBytes, "image/jpeg", "4096_timelinePreview341x195.jpg");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving timeline image for movie {MovieId}, file {FileName}", movieId, $"{movieId}.jpg");
            return StatusCode(500, "Internal server error");
        }
    }
}