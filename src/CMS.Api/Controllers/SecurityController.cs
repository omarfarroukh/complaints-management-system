using CMS.Api.Filters;
using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Application.Wrappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMS.Api.Controllers;

/// <summary>
/// Security management and monitoring (Admin only)
/// </summary>
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
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Returns a list of all currently blacklisted IPs including:
    /// - IP address
    /// - Reason for blocking
    /// - Block timestamp
    /// - Expiry time (if temporary)
    /// 
    /// **Caching:** Results cached for 60 seconds (shared cache)
    /// </remarks>
    /// <returns>List of blacklisted IPs</returns>
    /// <response code="200">Blacklist retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpGet("blacklisted-ips")]
    [Cached(60, "security", IsShared = true)] // Cache for 1 minute
    [ProducesResponseType(typeof(ApiResponse<List<IpBlacklistDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Manually adds an IP address to the blacklist.
    /// 
    /// **Effect:**
    /// - Blocked IP cannot access any API endpoints
    /// - Requests from this IP will be rejected immediately
    /// 
    /// **Use Cases:**
    /// - Blocking malicious actors
    /// - Preventing DDoS attacks
    /// - Restricting access from specific locations
    /// 
    /// **Cache Invalidation:** Clears security cache
    /// </remarks>
    /// <param name="request">IP address and reason for blocking</param>
    /// <returns>Success confirmation</returns>
    /// <response code="200">IP blocked successfully</response>
    /// <response code="400">Invalid IP address or missing reason</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpPost("block-ip")]
    [Transactional]
    [InvalidateCache("security")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Removes an IP address from the blacklist and clears its rate limit history.
    /// 
    /// **Effect:**
    /// - IP address regains access to API endpoints
    /// - Rate limit counters are reset for this IP
    /// 
    /// **Cache Invalidation:** Clears security cache
    /// </remarks>
    /// <param name="request">IP address to unblock</param>
    /// <returns>Success confirmation</returns>
    /// <response code="200">IP unblocked successfully</response>
    /// <response code="400">Invalid IP address</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpPost("unblock-ip")]
    [Transactional]
    [InvalidateCache("security")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Returns a log of recent login attempts (both successful and failed).
    /// 
    /// **Data Includes:**
    /// - Email used
    /// - IP address
    /// - Timestamp
    /// - Success/Failure status
    /// - Failure reason (if applicable)
    /// 
    /// **Use Cases:**
    /// - Auditing access
    /// - Investigating suspicious activity
    /// - Troubleshooting login issues
    /// 
    /// **Caching:** Results cached for 30 seconds
    /// </remarks>
    /// <param name="count">Number of attempts to retrieve (default: 100)</param>
    /// <returns>List of recent login attempts</returns>
    /// <response code="200">Login attempts retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpGet("login-attempts")]
    [Cached(30, "security", IsShared = true)] // Cache for 30 seconds
    [ProducesResponseType(typeof(ApiResponse<List<LoginAttemptDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// Get top IPs with most failed login attempts
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Returns a list of IP addresses with the highest number of failed login attempts in the last 24 hours.
    /// 
    /// **Use Cases:**
    /// - Identifying potential brute force attacks
    /// - Identifying credential stuffing sources
    /// - Proactive blocking of suspicious IPs
    /// 
    /// **Caching:** Results cached for 60 seconds
    /// </remarks>
    /// <param name="count">Number of IPs to retrieve (default: 20)</param>
    /// <returns>Dictionary of IP addresses and failure counts</returns>
    /// <response code="200">Top failed IPs retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpGet("failed-ips")]
    [Cached(60, "security", IsShared = true)] // Cache for 1 minute
    [ProducesResponseType(typeof(ApiResponse<Dictionary<string, int>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// Get overall security statistics
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Returns a dashboard summary of security metrics.
    /// 
    /// **Metrics:**
    /// - Total login attempts
    /// - Successful vs Failed login counts
    /// - Number of currently blocked IPs
    /// - Top attacking IPs
    /// 
    /// **Caching:** Results cached for 30 seconds
    /// </remarks>
    /// <returns>Security statistics summary</returns>
    /// <response code="200">Statistics retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpGet("statistics")]
    [Cached(30, "security", IsShared = true)]
    [ProducesResponseType(typeof(ApiResponse<SecurityStatisticsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// Clean up old login attempts history
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Manually triggers a cleanup of old login attempt logs to free up database space.
    /// 
    /// **Note:** This is typically handled by a background job, but can be triggered manually if needed.
    /// </remarks>
    /// <param name="daysToKeep">Number of days of history to retain (default: 30)</param>
    /// <returns>Success confirmation</returns>
    /// <response code="200">Cleanup completed successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpPost("cleanup-old-attempts")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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
    /// Check the status of a specific IP address
    /// </summary>
    /// <remarks>
    /// **Authorization:** Admin role required
    /// 
    /// Checks if a specific IP address is currently blocked, whitelisted, or active.
    /// </remarks>
    /// <param name="ipAddress">IP address to check</param>
    /// <returns>Status details for the IP</returns>
    /// <response code="200">IP status retrieved successfully</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (requires Admin role)</response>
    [HttpGet("check-ip/{ipAddress}")]
    [ProducesResponseType(typeof(ApiResponse<IpStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
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