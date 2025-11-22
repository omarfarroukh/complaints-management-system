using CMS.Application.DTOs;

namespace CMS.Application.Interfaces;

// <summary>
/// Service for managing security operations including IP blocking and login auditing
/// </summary>
public interface ISecurityService
{
    /// <summary>
    /// Check if an IP address is blocked
    /// </summary>
    Task<bool> IsIpBlockedAsync(string ipAddress);

    /// <summary>
    /// Block an IP address with a reason
    /// </summary>
    Task BlockIpAsync(string ipAddress, string reason);

    /// <summary>
    /// Unblock an IP address
    /// </summary>
    Task UnblockIpAsync(string ipAddress);

    /// <summary>
    /// Log a login attempt (success or failure)
    /// </summary>
    Task LogLoginAttemptAsync(string email, string ipAddress, string? userId, bool success, string? failureReason);

    /// <summary>
    /// Get recent login attempts
    /// </summary>
    Task<List<LoginAttemptDto>> GetRecentLoginAttemptsAsync(int count = 100);

    /// <summary>
    /// Get top IPs with the most failed login attempts in the last 24 hours
    /// </summary>
    Task<Dictionary<string, int>> GetTopFailedIpsAsync(int count = 20);

    /// <summary>
    /// Get all currently blacklisted IP addresses
    /// </summary>
    Task<List<IpBlacklistDto>> GetBlacklistedIpsAsync();

    /// <summary>
    /// Clean up old login attempts (for periodic maintenance)
    /// </summary>
    Task CleanupOldLoginAttemptsAsync(int daysToKeep = 30);
}