using System.Net.Mime;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DeoVRDeeplink.Api;

[ApiController]
[Route("deovr")]
public class StaticContentController : ControllerBase
{
    private readonly ILogger<StaticContentController> _logger;
    private readonly Assembly _assembly;
    private readonly string _clientScriptResourcePath =
        $"{DeoVrDeeplinkPlugin.Instance?.GetType().Namespace}.Web.DeoVRClient.js";
    public StaticContentController(ILogger<StaticContentController> logger)
    {
        _logger = logger;
        _assembly = Assembly.GetExecutingAssembly();
    }
    
    /// <summary>Serves embedded client JavaScript.</summary>
    [HttpGet("ClientScript")]
    [Produces("application/javascript")]
    [AllowAnonymous]
    public IActionResult GetClientScript()
    {
        try
        {
            var stream = _assembly.GetManifestResourceStream(_clientScriptResourcePath);
            if (stream != null) return File(stream, "application/javascript");
            _logger.LogError("Resource not found: {Path}", _clientScriptResourcePath);
            return NotFound();

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving client script resource.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving script resource.");
        }
    }

    /// <summary>Serves the icon image.</summary>
    [HttpGet("Icon")]
    [Produces(MediaTypeNames.Image.Png)]
    [AllowAnonymous]
    public IActionResult GetIcon()
    {
        const string resourceName = "DeoVRDeeplink.Web.Icon.png";
        try
        {
            var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return NotFound();

            return File(stream, MediaTypeNames.Image.Png);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving icon resource.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving icon resource.");
        }
    }
}
