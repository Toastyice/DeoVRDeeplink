using Microsoft.AspNetCore.Http;

namespace DeoVRDeeplink.Utilities;

/// <summary>
/// Helper class for URL-related operations.
/// </summary>
public static class UrlHelper
{
    /// <summary>
    /// Gets the accessible server URL from the current HTTP context.
    /// Respects X-Forwarded-Proto header for reverse proxy scenarios.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>The full server URL with scheme, host, and path base.</returns>
    public static string GetServerUrl(HttpContext? context)
    {
        var req = context?.Request;
        if (req == null)
        {
            return string.Empty;
        }

        // Get the scheme from the X-Forwarded-Proto header if it exists.
        // This header is set by the reverse proxy (Nginx in this case) to indicate
        // the original protocol used by the client (e.g., "https").
        var forwardedScheme = req.Headers["X-Forwarded-Proto"].FirstOrDefault();

        // Use the forwarded scheme if it's available and not empty, otherwise fall back 
        // to the scheme of the direct request. This ensures the correct scheme is used
        // whether the service is accessed directly or through a reverse proxy.
        var scheme = !string.IsNullOrEmpty(forwardedScheme) ? forwardedScheme : req.Scheme;

        // Construct the full server URL using the determined scheme, host, and path base.
        return $"{scheme}://{req.Host}{req.PathBase}";
    }
}
