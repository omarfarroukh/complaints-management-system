using CMS.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CMS.Infrastructure.Services;

public class RateLimitService : IRateLimitService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ISecurityService _securityService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RateLimitService> _logger;

    public RateLimitService(
        IConnectionMultiplexer redis,
        ISecurityService securityService,
        IConfiguration configuration,
        ILogger<RateLimitService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _securityService = securityService ?? throw new ArgumentNullException(nameof(securityService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> IncrementRequestCountAsync(string ipAddress)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"rate_limit:{ipAddress}";

            var count = await db.StringIncrementAsync(key);

            // Set 1-minute expiry on first increment
            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromMinutes(1));
            }

            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment request count for IP: {IpAddress}", ipAddress);
            // Return high number to avoid bypassing rate limit on Redis failure
            return 0;
        }
    }

    public async Task<int> IncrementLoginFailureAsync(string ipAddress, string email)
    {
        try
        {
            var db = _redis.GetDatabase();

            // Track unique emails attempted from this IP (for detecting credential stuffing)
            var setKey = $"login_failures_emails:{ipAddress}";
            await db.SetAddAsync(setKey, email);
            await db.KeyExpireAsync(setKey, TimeSpan.FromMinutes(10));

            // Count total failures
            var failureKey = $"login_failures:{ipAddress}";
            var count = await db.StringIncrementAsync(failureKey);

            // Set 10-minute expiry on first increment
            if (count == 1)
            {
                await db.KeyExpireAsync(failureKey, TimeSpan.FromMinutes(10));
            }

            _logger.LogWarning(
                "Login failure #{Count} from IP: {IpAddress} for email: {Email}", 
                count, 
                ipAddress, 
                email);

            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment login failure for IP: {IpAddress}", ipAddress);
            return 0;
        }
    }

    public async Task<bool> ShouldBlacklistAsync(string ipAddress)
    {
        try
        {
            var db = _redis.GetDatabase();

            // Check general rate limit
            var requestCount = await db.StringGetAsync($"rate_limit:{ipAddress}");
            if (requestCount.HasValue)
            {
                var count = (int)requestCount;
                var threshold = GetMaxRequestsPerMinute();
                
                if (count > threshold)
                {
                    _logger.LogWarning(
                        "IP {IpAddress} exceeded rate limit: {Count}/{Threshold} requests/min", 
                        ipAddress, 
                        count, 
                        threshold);
                    return true;
                }
            }

            // Check login failures
            var failureCount = await db.StringGetAsync($"login_failures:{ipAddress}");
            if (failureCount.HasValue)
            {
                var count = (int)failureCount;
                var threshold = GetMaxLoginFailures();
                
                if (count >= threshold)
                {
                    _logger.LogWarning(
                        "IP {IpAddress} exceeded login failure threshold: {Count}/{Threshold}", 
                        ipAddress, 
                        count, 
                        threshold);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check blacklist status for IP: {IpAddress}", ipAddress);
            // On Redis failure, don't blacklist (fail open, but log heavily)
            return false;
        }
    }

    public async Task AutoBlacklistAsync(string ipAddress, string reason)
    {
        try
        {
            _logger.LogWarning(
                "🚨 Auto-blacklisting IP: {IpAddress} - Reason: {Reason}", 
                ipAddress, 
                reason);

            await _securityService.BlockIpAsync(ipAddress, reason);

            // Clear the counters after blacklisting
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync($"rate_limit:{ipAddress}");
            await db.KeyDeleteAsync($"login_failures:{ipAddress}");
            await db.KeyDeleteAsync($"login_failures_emails:{ipAddress}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-blacklist IP: {IpAddress}", ipAddress);
            throw;
        }
    }

    public Task<bool> IsWhitelistedAsync(string ipAddress)
    {
        try
        {
            var whitelistedIps = _configuration.GetSection("RateLimiting:WhitelistedIps")
                .Get<string[]>() ?? Array.Empty<string>();

            var isWhitelisted = whitelistedIps.Contains(ipAddress);
            
            if (isWhitelisted)
            {
                _logger.LogDebug("IP {IpAddress} is whitelisted, skipping rate limit", ipAddress);
            }

            return Task.FromResult(isWhitelisted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check whitelist for IP: {IpAddress}", ipAddress);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Get unique emails attempted from an IP (for credential stuffing detection)
    /// </summary>
    public async Task<int> GetUniqueEmailAttemptsAsync(string ipAddress)
    {
        try
        {
            var db = _redis.GetDatabase();
            var setKey = $"login_failures_emails:{ipAddress}";
            var count = await db.SetLengthAsync(setKey);
            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unique email attempts for IP: {IpAddress}", ipAddress);
            return 0;
        }
    }

    /// <summary>
    /// Clear all rate limit data for an IP (useful for unblocking)
    /// </summary>
    public async Task ClearRateLimitDataAsync(string ipAddress)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync($"rate_limit:{ipAddress}");
            await db.KeyDeleteAsync($"login_failures:{ipAddress}");
            await db.KeyDeleteAsync($"login_failures_emails:{ipAddress}");
            
            _logger.LogInformation("Cleared rate limit data for IP: {IpAddress}", ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear rate limit data for IP: {IpAddress}", ipAddress);
        }
    }

    private int GetMaxRequestsPerMinute()
    {
        return _configuration.GetValue<int>("RateLimiting:MaxRequestsPerMinute", 100);
    }

    private int GetMaxLoginFailures()
    {
        return _configuration.GetValue<int>("RateLimiting:MaxLoginFailures", 20);
    }
}