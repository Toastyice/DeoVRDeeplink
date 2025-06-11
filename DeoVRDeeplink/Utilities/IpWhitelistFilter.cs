using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace DeoVRDeeplink.Utilities;

public class IpWhitelistAttribute : TypeFilterAttribute
{
    public IpWhitelistAttribute() : base(typeof(IpWhitelistFilter))
    {
    }
}

public class IpWhitelistFilter : IAuthorizationFilter
{
    private readonly ILogger<IpWhitelistFilter> _logger;

    public IpWhitelistFilter(ILogger<IpWhitelistFilter> logger)
    {
        _logger = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var clientIp = context.HttpContext.Connection.RemoteIpAddress;

        if (clientIp == null)
        {
            _logger.LogWarning("Unable to determine client IP address");
            context.Result = new ForbidResult();
            return;
        }

        var config = DeoVrDeeplinkPlugin.Instance?.Configuration;
        if (config?.AllowedIpRanges == null || !config.AllowedIpRanges.Any())
        {
            // If no IP ranges configured, allow all (or deny all - your choice)
            return;
        }

        var isAllowed = config.AllowedIpRanges.Any(clientIp.IsInCidrRange);

        if (!isAllowed)
        {
            _logger.LogWarning("Unauthorized access attempt from IP: {IpAddress}", clientIp);
            context.Result = new ForbidResult();
        }
    }
}


/// <summary>
/// Extension methods for IP address operations
/// </summary>
public static class IpAddressExtensions
{
    /// <summary>
    /// Checks if an IP address is within a CIDR range
    /// </summary>
    /// <param name="address">The IP address to check</param>
    /// <param name="cidrNotation">The CIDR notation string (e.g., "192.168.1.0/24")</param>
    /// <returns>True if the IP is in the CIDR range, false otherwise</returns>
    public static bool IsInCidrRange(this IPAddress address, string cidrNotation)
    {
        if (string.IsNullOrWhiteSpace(cidrNotation))
            return false;

        try
        {
            // Parse CIDR notation
            var parts = cidrNotation.Split('/');
            if (parts.Length != 2)
                return false;

            // Parse the network address part
            if (!IPAddress.TryParse(parts[0], out var network))
                return false;

            // Parse the prefix length part
            if (!int.TryParse(parts[1], out int prefixLength))
                return false;

            // Ensure we're comparing the same address family (IPv4 or IPv6)
            if (address.AddressFamily != network.AddressFamily)
                return false;

            // Get the bytes of both addresses
            var addressBytes = address.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();

            // Maximum prefix length validation based on address family
            var maxPrefixLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            
            if (prefixLength < 0 || prefixLength > maxPrefixLength)
                return false;

            // Calculate how many full bytes are in the prefix
            var byteCount = prefixLength / 8;
            
            // Check the full bytes
            for (var i = 0; i < byteCount && i < networkBytes.Length; i++)
            {
                if (addressBytes[i] != networkBytes[i])
                    return false;
            }

            // If the prefix length isn't a multiple of 8, we need to check the remaining bits
            var remainingBits = prefixLength % 8;
            if (remainingBits <= 0 || byteCount >= networkBytes.Length) return true;
            // Create a mask for the remaining bits
            var mask = (byte)(0xFF << (8 - remainingBits));

            // Apply mask and compare
            return (addressBytes[byteCount] & mask) == (networkBytes[byteCount] & mask);
        }
        catch (Exception)
        {
            return false;
        }
    }
}