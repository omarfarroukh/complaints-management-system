using CMS.Api.Filters;
using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")] // Only admins can access security endpoints
public class SecurityController : ControllerBase
{
    private readonly ISecurityService _securityService;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<SecurityController> _logger;

    public SecurityController(
        ISecurityService securityService,
        IRateLimitService rateLimitService,
        ILogger<SecurityController> logger)
    {
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Get all blacklisted IP addresses
    /// </summary>
    [HttpGet("blacklisted-ips")]
    [Cached(60, "security", IsShared = true)] // Cache for 1 minute
    [ProducesResponseType(typeof(ApiResponse<List<IpBlacklistDto>>), 200)]
    public async Task<IActionResult> GetBlacklistedIps()
    {
        _logger.LogInformation("Admin retrieving blacklisted IPs");
        
        var blacklist = await _securityService.GetBlacklistedIpsAsync();
        
        return Ok(new ApiResponse<List<IpBlacklistDto>>(blacklist)
        {
            Message = $"Retrieved {blacklist.Count} blacklisted IPs"
        });
    }

    /// <summary>
    /// Block an IP address manually
    /// </summary>
    [HttpPost("block-ip")]
    [Transactional]
    [InvalidateCache("security")]
    [ProducesResponseType(typeof(ApiResponse<string>), 200)]
    public async Task<IActionResult> BlockIp([FromBody] BlockIpRequest request)
    {
        if (string.IsNullOrEmpty(request.IpAddress))
        {
            return BadRequest(new ApiResponse<string>
            {
                Succeeded = false,
                Message = "IP address is required"
            });
        }

        _logger.LogWarning(
            "Admin manually blocking IP: {IpAddress} - Reason: {Reason}", 
            request.IpAddress, 
            request.Reason);

        await _securityService.BlockIpAsync(request.IpAddress, request.Reason ?? "Manually blocked by admin");
        
        return Ok(new ApiResponse<string>
        {
            Message = $"IP {request.IpAddress} has been blocked"
        });
    }

    /// <summary>
    /// Unblock an IP address
    /// </summary>
    [HttpPost("unblock-ip")]
    [Transactional]
    [InvalidateCache("security")]
    [ProducesResponseType(typeof(ApiResponse<string>), 200)]
    public async Task<IActionResult> UnblockIp([FromBody] UnblockIpRequest request)
    {
        if (string.IsNullOrEmpty(request.IpAddress))
        {
            return BadRequest(new ApiResponse<string>
            {
                Succeeded = false,
                Message = "IP address is required"
            });
        }

        _logger.LogInformation("Admin unblocking IP: {IpAddress}", request.IpAddress);

        await _securityService.UnblockIpAsync(request.IpAddress);
        
        // Also clear rate limit data
        await _rateLimitService.ClearRateLimitDataAsync(request.IpAddress);
        
        return Ok(new ApiResponse<string>
        {
            Message = $"IP {request.IpAddress} has been unblocked"
        });
    }

    /// <summary>
    /// Get recent login attempts
    /// </summary>
    [HttpGet("login-attempts")]
    [Cached(30, "security", IsShared = true)] // Cache for 30 seconds
    [ProducesResponseType(typeof(ApiResponse<List<LoginAttemptDto>>), 200)]
    public async Task<IActionResult> GetLoginAttempts([FromQuery] int count = 100)
    {
        _logger.LogInformation("Admin retrieving recent login attempts");
        
        var attempts = await _securityService.GetRecentLoginAttemptsAsync(count);
        
        return Ok(new ApiResponse<List<LoginAttemptDto>>(attempts)
        {
            Message = $"Retrieved {attempts.Count} login attempts"
        });
    }

    /// <summary>
    /// Get top IPs with most failed login attempts (last 24 hours)
    /// </summary>
    [HttpGet("failed-ips")]
    [Cached(60, "security", IsShared = true)] // Cache for 1 minute
    [ProducesResponseType(typeof(ApiResponse<Dictionary<string, int>>), 200)]
    public async Task<IActionResult> GetTopFailedIps([FromQuery] int count = 20)
    {
        _logger.LogInformation("Admin retrieving top failed IPs");
        
        var failedIps = await _securityService.GetTopFailedIpsAsync(count);
        
        return Ok(new ApiResponse<Dictionary<string, int>>(failedIps)
        {
            Message = $"Retrieved top {failedIps.Count} failed IPs"
        });
    }

    /// <summary>
    /// Get security statistics
    /// </summary>
    [HttpGet("statistics")]
    [Cached(30, "security", IsShared = true)]
    [ProducesResponseType(typeof(ApiResponse<SecurityStatisticsDto>), 200)]
    public async Task<IActionResult> GetStatistics()
    {
        _logger.LogInformation("Admin retrieving security statistics");
        
        var attempts = await _securityService.GetRecentLoginAttemptsAsync(1000);
        var blacklist = await _securityService.GetBlacklistedIpsAsync();
        var failedIps = await _securityService.GetTopFailedIpsAsync(10);

        var stats = new SecurityStatisticsDto
        {
            TotalLoginAttempts = attempts.Count,
            SuccessfulLogins = attempts.Count(a => a.Success),
            FailedLogins = attempts.Count(a => !a.Success),
            BlockedIpsCount = blacklist.Count,
            TopFailedIps = failedIps,
            LastUpdated = DateTime.UtcNow
        };
        
        return Ok(new ApiResponse<SecurityStatisticsDto>(stats)
        {
            Message = "Security statistics retrieved"
        });
    }

    /// <summary>
    /// Clean up old login attempts (manual trigger)
    /// </summary>
    [HttpPost("cleanup-old-attempts")]
    [ProducesResponseType(typeof(ApiResponse<string>), 200)]
    public async Task<IActionResult> CleanupOldAttempts([FromQuery] int daysToKeep = 30)
    {
        _logger.LogInformation("Admin manually triggering cleanup of login attempts older than {Days} days", daysToKeep);
        
        await _securityService.CleanupOldLoginAttemptsAsync(daysToKeep);
        
        return Ok(new ApiResponse<string>
        {
            Message = $"Cleanup completed for attempts older than {daysToKeep} days"
        });
    }

    /// <summary>
    /// Check if a specific IP is blocked
    /// </summary>
    [HttpGet("check-ip/{ipAddress}")]
    [ProducesResponseType(typeof(ApiResponse<IpStatusDto>), 200)]
    public async Task<IActionResult> CheckIpStatus(string ipAddress)
    {
        _logger.LogInformation("Admin checking status for IP: {IpAddress}", ipAddress);
        
        var isBlocked = await _securityService.IsIpBlockedAsync(ipAddress);
        var isWhitelisted = await _rateLimitService.IsWhitelistedAsync(ipAddress);
        
        var status = new IpStatusDto
        {
            IpAddress = ipAddress,
            IsBlocked = isBlocked,
            IsWhitelisted = isWhitelisted,
            Status = isWhitelisted ? "Whitelisted" : (isBlocked ? "Blocked" : "Active")
        };
        
        return Ok(new ApiResponse<IpStatusDto>(status)
        {
            Message = $"IP status for {ipAddress}"
        });
    }
}

// ============================================================================
// REQUEST/RESPONSE DTOs
// ============================================================================

public class BlockIpRequest
{
    public string IpAddress { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class UnblockIpRequest
{
    public string IpAddress { get; set; } = string.Empty;
}

public class SecurityStatisticsDto
{
    public int TotalLoginAttempts { get; set; }
    public int SuccessfulLogins { get; set; }
    public int FailedLogins { get; set; }
    public int BlockedIpsCount { get; set; }
    public Dictionary<string, int> TopFailedIps { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class IpStatusDto
{
    public string IpAddress { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
    public bool IsWhitelisted { get; set; }
    public string Status { get; set; } = string.Empty;
}