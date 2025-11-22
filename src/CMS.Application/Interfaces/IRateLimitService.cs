using CMS.Application.DTOs;

namespace CMS.Application.Interfaces;

/// <summary>
/// Service for managing rate limiting and request throttling
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Increment the request count for an IP address
    /// </summary>
    Task<int> IncrementRequestCountAsync(string ipAddress);

    /// <summary>
    /// Increment login failure count for an IP address
    /// </summary>
    Task<int> IncrementLoginFailureAsync(string ipAddress, string email);

    /// <summary>
    /// Check if an IP should be blacklisted based on rate limit violations
    /// </summary>
    Task<bool> ShouldBlacklistAsync(string ipAddress);

    /// <summary>
    /// Automatically blacklist an IP address with a reason
    /// </summary>
    Task AutoBlacklistAsync(string ipAddress, string reason);

    /// <summary>
    /// Check if an IP address is whitelisted (bypass rate limits)
    /// </summary>
    Task<bool> IsWhitelistedAsync(string ipAddress);

    /// <summary>
    /// Get the number of unique emails attempted from an IP (credential stuffing detection)
    /// </summary>
    Task<int> GetUniqueEmailAttemptsAsync(string ipAddress);

    /// <summary>
    /// Clear all rate limit data for an IP address
    /// </summary>
    Task ClearRateLimitDataAsync(string ipAddress);
}