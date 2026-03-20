using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace DeoVRDeeplink.Utilities;

/// <summary>
/// Provides helpers for selecting and formatting item images.
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// Returns the best available image URL for an item.
    /// 
    /// Priority:
    /// 1. Backdrop (preferred for VR – typically 16:9)
    /// 2. Primary (fallback)
    /// 
    /// Large images are downscaled to a consistent 16:9 format.
    /// </summary>
    public static string GetImageUrl(BaseItem item, string baseUrl)
    {
        const int maxSize = 1024;
        var image = new[] { ImageType.Backdrop, ImageType.Primary }
            .Select(type => new
            {
                Type = type,
                Info = Array.Find(item.ImageInfos, i => i.Type == type && IsValid(i))
            })
            .FirstOrDefault(x => x.Info != null);

        if (image?.Info == null)
            return string.Empty;

        var img = image.Info;

        // Small images: return as-is
        if (img.Width is > 0 and <= maxSize && img.Height <= maxSize)
            return $"{baseUrl}/Items/{item.Id}/Images/{image.Type}";

        // Large images: enforce 16:9 scaling for consistent VR layout
        return $"{baseUrl}/Items/{item.Id}/Images/{image.Type}?fillWidth=960&fillHeight=540&quality=90";
    }

    /// <summary>
    /// Basic sanity check for usable images.
    /// </summary>
    private static bool IsValid(ItemImageInfo img) =>
        !string.IsNullOrEmpty(img.Path) &&
        (!img.IsLocalFile || (img.Width > 0 && img.Height > 0));
}