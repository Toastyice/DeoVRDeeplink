using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace DeoVRDeeplink.Utilities;

/// <summary>
/// Helper class for image-related operations.
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// Attempts to get an image URL for a specific image type.
    /// </summary>
    /// <param name="item">The item to get the image for.</param>
    /// <param name="imageType">The image type to retrieve.</param>
    /// <param name="baseUrl">The base server URL.</param>
    /// <returns>The image URL, or null if the image type is not available or invalid.</returns>
    public static string TryGetImageUrl(BaseItem item, string baseUrl, ImageType imageType = ImageType.Primary) =>
        Array.Find(item.ImageInfos, img => img.Type == imageType && IsValidImage(img)) != null
            ? $"{baseUrl}/Items/{item.Id}/Images/{imageType}?fillHeight=235&fillWidth=471&quality=96"
            : string.Empty;
    
    /// <summary>
    /// Validates whether an image info object represents a valid image.
    /// </summary>
    /// <param name="img">The image info to validate.</param>
    /// <returns>True if the image is valid, false otherwise.</returns>
    private static bool IsValidImage(ItemImageInfo img) => 
        !string.IsNullOrEmpty(img.Path) && (!img.IsLocalFile || img is { Width: > 0, Height: > 0 });
}
