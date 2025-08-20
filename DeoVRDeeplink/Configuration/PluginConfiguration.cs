using System.Text.Json.Serialization;
using MediaBrowser.Model.Plugins;

namespace DeoVRDeeplink.Configuration;

/// <summary>
///     Projection type for VR content.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProjectionType
{
    /// <summary>
    ///     No forced projection.
    /// </summary>
    None = 0,

    /// <summary>
    ///     180-degree projection.
    /// </summary>
    Projection180 = 1,

    /// <summary>
    ///     360-degree projection.
    /// </summary>
    Projection360 = 2
}

/// <summary>
///     Stereo mode for VR content.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StereoMode
{
    /// <summary>
    ///     No forced stereo mode.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Side-by-side stereo format.
    /// </summary>
    SideBySide = 1,

    /// <summary>
    ///     Top-bottom stereo format.
    /// </summary>
    TopBottom = 2
}

public class LibraryConfiguration
{
    public Guid Id { get; set; }              // Library ID
    public string Name { get; set; }            // Optional: Library name
    public bool Enabled { get; set; }           // Enabled toggle
    public bool Random { get; set; }            // Random toggle
    public bool TimelineImages { get; set; }    // Timeline images toggle
    public ProjectionType FallbackProjection { get; set; }  // Fallback projection enum
    public StereoMode FallbackStereoMode { get; set; }
}

/// <summary>
///     Plugin configuration.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PluginConfiguration" /> class.
    /// </summary>
    public PluginConfiguration()
    {
        ProxySecret = Guid.NewGuid().ToString("N");
        FallbackProjection = ProjectionType.Projection180;
        FallbackStereoMode = StereoMode.SideBySide;
        AllowedIpRanges = [];
        EnableIpRestriction = false;
        Libraries = new List<LibraryConfiguration>();
    }

    /// <summary>
    ///     Secret for signing proxy tokens
    /// </summary>
    public string ProxySecret { get; set; }

    /// <summary>
    ///     Fallback projection type (180/360/None)
    /// </summary>
    public ProjectionType FallbackProjection { get; set; }

    /// <summary>
    ///     Fallback stereo mode (SBS/TB/None)
    /// </summary>
    public StereoMode FallbackStereoMode { get; set; }

    /// <summary>
    ///     List of allowed IP ranges in CIDR notation (e.g., "192.168.1.0/24", "10.0.0.0/8", "127.0.0.1/32")
    /// </summary>
    public List<string> AllowedIpRanges { get; set; }

    /// <summary>
    ///     Whether IP restriction is enabled
    /// </summary>
    public bool EnableIpRestriction { get; set; }
    
    /// <summary>
    ///     Names of Libraries to create TimelineImages
    /// </summary>
    public string[] TimelineIncludedLibrary { get; set; } = [];
    
    /// <summary>
    ///     Enable an additional filter to attempt to remove VR distortion from the timeline image (Experimental)
    /// </summary>
    public bool TimelineRemoveDistortion { get; set; } = false;
    
    /// <summary>
    /// Per-library configurations
    /// </summary>
    public List<LibraryConfiguration> Libraries { get; set; }
}