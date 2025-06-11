using System.Text.Json.Serialization;

namespace DeoVRDeeplink.Model;

public class DeoVrScenesResponse
{
    [JsonPropertyName("scenes")]
    public List<DeoVrScene> Scenes { get; set; } = [];
}

public class DeoVrScene
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("list")]
    public List<DeoVrVideoItem> List { get; set; } = [];
}

public class DeoVrVideoItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("videoLength")]
    public int VideoLength { get; set; }

    [JsonPropertyName("video_url")]
    public string VideoUrl { get; set; } = string.Empty;

    [JsonPropertyName("thumbnailUrl")]
    public string ThumbnailUrl { get; set; } = string.Empty;
}
