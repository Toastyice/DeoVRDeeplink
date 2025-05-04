namespace DeoVRDeeplink.Configuration;

using MediaBrowser.Model.Plugins;
/// <summary>
/// Plugin configuration.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        ApiKey = "Some api key";
        ProxySecret = System.Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Gets or sets the API Key
    /// </summary>
    public string ApiKey { get; set; }
        
    /// <summary>
    /// Secret for signing proxy tokens
    /// </summary>
    public string ProxySecret { get; set; }
}