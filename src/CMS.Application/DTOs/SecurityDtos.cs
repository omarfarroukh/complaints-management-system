using System.ComponentModel.DataAnnotations;

namespace CMS.Application.DTOs;

public class BlacklistIpDto
{
    [Required] public string IpAddress { get; set; } = string.Empty;
    [Required] public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Request to block an IP address
/// </summary>
public class BlockIpRequest
{
    /// <summary>
    /// The IP address to block
    /// </summary>
    [Required]
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Reason for blocking the IP
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Request to unblock an IP address
/// </summary>
public class UnblockIpRequest
{
    /// <summary>
    /// The IP address to unblock
    /// </summary>
    [Required]
    public string IpAddress { get; set; } = string.Empty;
}

/// <summary>
/// Summary of security statistics
/// </summary>
public class SecurityStatisticsDto
{
    /// <summary>
    /// Total number of login attempts recorded
    /// </summary>
    public int TotalLoginAttempts { get; set; }

    /// <summary>
    /// Number of successful logins
    /// </summary>
    public int SuccessfulLogins { get; set; }

    /// <summary>
    /// Number of failed login attempts
    /// </summary>
    public int FailedLogins { get; set; }

    /// <summary>
    /// Number of currently blocked IP addresses
    /// </summary>
    public int BlockedIpsCount { get; set; }

    /// <summary>
    /// Top IPs with the most failed attempts
    /// </summary>
    public Dictionary<string, int> TopFailedIps { get; set; } = new();

    /// <summary>
    /// Timestamp when these statistics were generated
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Status of a specific IP address
/// </summary>
public class IpStatusDto
{
    /// <summary>
    /// The IP address being checked
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// Whether the IP is currently blocked
    /// </summary>
    public bool IsBlocked { get; set; }

    /// <summary>
    /// Whether the IP is whitelisted
    /// </summary>
    public bool IsWhitelisted { get; set; }

    /// <summary>
    /// Text description of the status (Blocked, Whitelisted, Active)
    /// </summary>
    public string Status { get; set; } = string.Empty;
}