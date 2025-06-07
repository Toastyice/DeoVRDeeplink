namespace DeoVRDeeplink;

using System.Text.RegularExpressions;
using Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

public partial class DeoVrDeeplinkPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<DeoVrDeeplinkPlugin> _logger;
    private readonly IServerConfigurationManager _configurationManager;

    public static DeoVrDeeplinkPlugin? Instance { get; private set; }
    public override string Name => "DeoVRDeeplink";
    public override Guid Id => Guid.Parse("e7bea589-e339-490c-8738-596e42b9042e");
    public override string Description => "Adds deeplink support for DeoVR player";

    public DeoVrDeeplinkPlugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<DeoVrDeeplinkPlugin> logger,
        IServerConfigurationManager configurationManager)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurationManager = configurationManager ?? throw new ArgumentNullException(nameof(configurationManager));
        Instance = this;

        // Consider calling PatchIndexHtml() in some OnEnable method instead
        TryPatchIndexHtml(applicationPaths);
    }

    private void TryPatchIndexHtml(IApplicationPaths applicationPaths)
    {
        if (string.IsNullOrWhiteSpace(applicationPaths.WebPath))
        {
            _logger.LogDebug("WebPath is null or whitespace, skipping DeoVR script injection.");
            return;
        }

        var indexFile = Path.Combine(applicationPaths.WebPath, "index.html");
        if (!File.Exists(indexFile))
        {
            _logger.LogWarning("Index file not found at {IndexFile}, skipping DeoVR script injection.", indexFile);
            return;
        }

        var indexContents = File.ReadAllText(indexFile);
        var basePath = GetBasePath() ?? string.Empty;
        var scriptElement = $"<script plugin=\"DeoVRDeeplink\" src=\"{basePath}/DeoVRDeeplink/ClientScript\"></script>";

        // If already present, skip
        if (indexContents.Contains(scriptElement))
        {
            _logger.LogInformation("DeoVR client script is already present in {IndexFile}.", indexFile);
            return;
        }

        _logger.LogInformation("Injecting DeoVR client script in {IndexFile}...", indexFile);

        // Remove any previous instances of the script (in case file changed)
        indexContents = GetDeovrDeeplinkScriptRegex().Replace(indexContents, string.Empty);

        var bodyClosing = indexContents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyClosing == -1)
        {
            _logger.LogWarning("Could not find closing </body> tag in {IndexFile}. DeoVR script not injected.", indexFile);
            return;
        }

        indexContents = indexContents.Insert(bodyClosing, scriptElement);

        try
        {
            File.WriteAllText(indexFile, indexContents);
            _logger.LogInformation("DeoVR client script injected successfully into {IndexFile}.", indexFile);
        }
        catch (IOException ioe)
        {
            _logger.LogError(ioe, "I/O exception occurred while trying to update {IndexFile}.", indexFile);
        }
        catch (UnauthorizedAccessException uae)
        {
            _logger.LogError(uae, "UnauthorizedAccessException writing to {IndexFile}.", indexFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception while writing to {IndexFile}.", indexFile);
        }
    }

    private string? GetBasePath()
    {
        try
        {
            var networkConfig = _configurationManager.GetConfiguration("network");
            var configType = networkConfig.GetType();
            var basePathProp = configType.GetProperty("BaseUrl");
            var confBasePath = basePathProp?.GetValue(networkConfig)?.ToString()?.Trim('/');
            if (!string.IsNullOrEmpty(confBasePath))
            {
                return $"/{confBasePath}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to get base path from config, using root ('/').");
        }
        return string.Empty;
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "DeoVRDeeplink",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        };
    }

    [GeneratedRegex("<script plugin=\"DeoVRDeeplink\".*?></script>", RegexOptions.IgnoreCase)]
    private static partial Regex GetDeovrDeeplinkScriptRegex();
}
