using System.Security.Cryptography;
using System.Text;

namespace DeoVRDeeplink.Utilities;

/// <summary>
/// Provides utility methods for generating and validating secure, time-limited HMAC signatures for video streaming.
/// </summary>
public static class SignatureValidator
{
    /// <summary>
    /// Generates the raw payload string used for signing by concatenating the source parameters.
    /// </summary>
    private static string GetSignaturePayload(object movieId, object mediaSourceId, long expiry) 
        => $"{movieId}:{mediaSourceId}:{expiry}";

    /// <summary>
    /// Computes an HMAC-SHA256 hash of the provided data using the specified secret.
    /// </summary>
    private static string SignUrl(string data, string secret)
    {
#if NET9_0
    return Convert.ToHexStringLower(
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(data)
        )
    );
#else
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#endif
    }

    /// <summary>
    /// Generates a complete HMAC-SHA256 signature for a video stream request.
    /// </summary>
    public static string GenerateSignature(object movieId, object mediaSourceId, long expiry, string secret) 
        => SignUrl(GetSignaturePayload(movieId, mediaSourceId, expiry), secret);

    /// <summary>
    /// Determines whether a given Unix timestamp is in the past compared to the current UTC time.
    /// </summary>
    public static bool IsExpired(long expiry) 
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry;

    /// <summary>
    /// Validates a provided signature and outputs the expected signature for logging.
    /// </summary>
    public static bool TryValidateSignature(
        object movieId, 
        object mediaSourceId, 
        long expiry, 
        string signature, 
        string secret, 
        out string expectedSignature) 
        => string.Equals(signature, expectedSignature = GenerateSignature(movieId, mediaSourceId, expiry, secret), StringComparison.OrdinalIgnoreCase);
}
