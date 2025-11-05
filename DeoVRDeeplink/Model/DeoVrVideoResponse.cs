using System.Text.Json.Serialization;

namespace DeoVRDeeplink.Model;

/// <summary>
/// Represents a response containing video details from DeoVR.
/// </summary>
public class DeoVrVideoResponse
{
    /// <summary>
    /// Gets or sets the list of available encodings for the video.
    /// </summary>
    [JsonPropertyName("encodings")]
    public List<DeoVrEncoding> Encodings { get; set; } = [];

    /// <summary>
    /// Gets or sets the title of the video.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier of the video.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the length of the video in seconds.
    /// </summary>
    [JsonPropertyName("videoLength")]
    public int VideoLength { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the video is 3D.
    /// </summary>
    [JsonPropertyName("is3d")]
    public bool Is3D { get; set; }

    /// <summary>
    /// Gets or sets the screen type of the video (e.g., flat, curved).
    /// </summary>
    [JsonPropertyName("screenType")]
    public string ScreenType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stereo mode of the video (e.g., "mono", "stereo").
    /// </summary>
    [JsonPropertyName("stereoMode")]
    public string StereoMode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL of the video thumbnail image.
    /// </summary>
    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URL of the timelinepreview image (Like Jellyfin trickplay)
    /// </summary>
    [JsonPropertyName("timelinePreview")]
    public string TimelinePreview { get; set; } = string.Empty;
    
     /// <summary>
    /// Gets or sets the list of timestamps for the video.
    /// </summary>
    [JsonPropertyName("timeStamps")]
    public List<DeoVrTimestamps> Timestamps { get; set; } = [];
}

/// <summary>
/// Represents a specific encoding for a DeoVR video.
/// </summary>
public class DeoVrEncoding
{
    /// <summary>
    /// Gets or sets the name of the encoding (e.g., "1080p", "4K").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of video sources for this encoding.
    /// </summary>
    [JsonPropertyName("videoSources")]
    public List<DeoVrVideoSource> VideoSources { get; set; } = [];
}

/// <summary>
/// Represents a video source for a specific encoding.
/// </summary>
public class DeoVrVideoSource
{
    /// <summary>
    /// Gets or sets the resolution of the video source (e.g., 2160 for 2160p).
    /// </summary>
    [JsonPropertyName("resolution")]
    public int Resolution { get; set; }

    /// <summary>
    /// Gets or sets the URL to the video source.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Represents a named timestamp within a DeoVR video.
/// </summary>
public class DeoVrTimestamps
{
    /// <summary>
    /// Gets or sets the timestamp in seconds.
    /// </summary>
    [JsonPropertyName("ts")]
    public int ts { get; set; }

    /// <summary>
    /// Gets or sets the name or description of the timestamp.
    /// </summary>
    [JsonPropertyName("name")]
    public string? name { get; set; }
}
