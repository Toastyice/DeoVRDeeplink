using System.Security.Cryptography;
using System.Text;

namespace DeoVRDeeplink.Utilities;

public static class SignatureValidator
{
    /// <summary>
    /// Signs data with HMAC-SHA256 and returns lowercase hex string
    /// </summary>
    public static string SignUrl(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Validates a signed URL token
    /// </summary>
    public static bool ValidateSignature(
        string movieId, 
        string mediaSourceId, 
        long expiry, 
        string providedSignature,
        string secret)
    {
        // Check expiry first (fast check)
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now > expiry)
        {
            return false;
        }

        // Validate signature
        var dataToSign = $"{movieId}:{mediaSourceId}:{expiry}";
        var expectedSignature = SignUrl(dataToSign, secret);
        
        return string.Equals(providedSignature, expectedSignature, StringComparison.OrdinalIgnoreCase);
    }
}
