using System.Security.Cryptography;
using System.Text;

namespace DeoVRDeeplink.Utilities;

public static class SignatureValidator
{
    /// <summary>
    /// Signs data with HMAC-SHA256 and returns lowercase hex string
    /// </summary>
    private static string SignUrl(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Creates a signed token containing movieId, mediaSourceId, and expiry
    /// Format: {movieId}:{mediaSourceId}:{expiry}:{signature}
    /// </summary>
    public static string CreateSignedToken(
        string movieId,
        string mediaSourceId,
        long expiry,
        string secret)
    {
        var dataToSign = $"{movieId}:{mediaSourceId}:{expiry}";
        var signature = SignUrl(dataToSign, secret);
        
        // Encode the data and signature together
        var token = $"{movieId}:{mediaSourceId}:{expiry}:{signature}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('='); // URL-safe base64
    }

    /// <summary>
    /// Validates and extracts data from a signed token
    /// </summary>
    public static bool ValidateAndExtractToken(
        string token,
        string secret,
        out string movieId,
        out string mediaSourceId,
        out long expiry)
    {
        movieId = string.Empty;
        mediaSourceId = string.Empty;
        expiry = 0;

        try
        {
            // Decode URL-safe base64
            var base64 = token.Replace('-', '+').Replace('_', '/');
            var padding = (4 - base64.Length % 4) % 4;
            base64 += new string('=', padding);
            
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var parts = decoded.Split(':');
            
            if (parts.Length != 4)
                return false;

            movieId = parts[0];
            mediaSourceId = parts[1];
            expiry = long.Parse(parts[2]);
            var providedSignature = parts[3];

            // Check expiry first (fast check)
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (now > expiry)
                return false;

            // Validate signature
            var dataToSign = $"{movieId}:{mediaSourceId}:{expiry}";
            var expectedSignature = SignUrl(dataToSign, secret);
            
            return string.Equals(providedSignature, expectedSignature, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}