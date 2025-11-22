using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using CMS.Domain.Entities;
using CMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CMS.Infrastructure.Services;

/// <summary>
/// Service for security operations including IP blocking and login auditing
/// </summary>
public class SecurityService : ISecurityService
{
    private readonly AppDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SecurityService> _logger;

    public SecurityService(
        AppDbContext context, 
        IDistributedCache cache,
        ILogger<SecurityService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsIpBlockedAsync(string ipAddress)
    {
        try
        {
            // 1. Check cache first (fast path)
            var cacheKey = $"BLOCKED_{ipAddress}";
            var cached = await _cache.GetStringAsync(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("IP {IpAddress} found in blocked cache", ipAddress);
                return true;
            }

            // 2. Check database
            var isBlocked = await _context.IpBlacklist
                .AnyAsync(x => x.IpAddress == ipAddress && x.IsActive);

            // 3. Cache the result for future requests
            if (isBlocked)
            {
                await _cache.SetStringAsync(cacheKey, "true",
                    new DistributedCacheEntryOptions 
                    { 
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) 
                    });
                
                _logger.LogWarning("IP {IpAddress} is blocked (cached result)", ipAddress);
            }

            return isBlocked;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if IP is blocked: {IpAddress}", ipAddress);
            // Fail open - don't block on errors, but log heavily
            return false;
        }
    }

    public async Task BlockIpAsync(string ipAddress, string reason)
    {
        try
        {
            var existing = await _context.IpBlacklist
                .FirstOrDefaultAsync(x => x.IpAddress == ipAddress);
            
            if (existing == null)
            {
                // Create new blacklist entry
                var entry = new IpBlacklist 
                { 
                    IpAddress = ipAddress, 
                    Reason = reason,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                
                _context.IpBlacklist.Add(entry);
                _logger.LogWarning("🚫 Blocking new IP: {IpAddress} - Reason: {Reason}", ipAddress, reason);
            }
            else
            {
                // Reactivate existing entry
                existing.IsActive = true;
                existing.Reason = reason;
                existing.CreatedAt = DateTime.UtcNow; // Update timestamp
                
                _logger.LogWarning("🚫 Re-blocking IP: {IpAddress} - Reason: {Reason}", ipAddress, reason);
            }
            
            await _context.SaveChangesAsync();

            // Cache the blocked status (1 hour)
            var cacheKey = $"BLOCKED_{ipAddress}";
            await _cache.SetStringAsync(cacheKey, "true",
                new DistributedCacheEntryOptions 
                { 
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) 
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to block IP: {IpAddress}", ipAddress);
            throw;
        }
    }

    public async Task UnblockIpAsync(string ipAddress)
    {
        try
        {
            var item = await _context.IpBlacklist
                .FirstOrDefaultAsync(x => x.IpAddress == ipAddress);
            
            if (item != null)
            {
                item.IsActive = false;
                await _context.SaveChangesAsync();
                
                _logger.LogInformation("✅ Unblocked IP: {IpAddress}", ipAddress);
            }

            // Remove from cache
            var cacheKey = $"BLOCKED_{ipAddress}";
            await _cache.RemoveAsync(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unblock IP: {IpAddress}", ipAddress);
            throw;
        }
    }

    public async Task LogLoginAttemptAsync(
        string email, 
        string ipAddress, 
        string? userId, 
        bool success, 
        string? failureReason)
    {
        try
        {
            var attempt = new LoginAttempt
            {
                Email = email,
                IpAddress = ipAddress,
                UserId = userId,
                Success = success,
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow
            };
            
            _context.LoginAttempts.Add(attempt);
            await _context.SaveChangesAsync();

            // Log based on success/failure
            if (success)
            {
                _logger.LogInformation(
                    "✅ Successful login: {Email} from {IpAddress}", 
                    email, 
                    ipAddress);
            }
            else
            {
                _logger.LogWarning(
                    "❌ Failed login attempt: {Email} from {IpAddress} - Reason: {Reason}", 
                    email, 
                    ipAddress, 
                    failureReason ?? "Unknown");
            }
        }
        catch (Exception ex)
        {
            // Don't throw - logging failures shouldn't break the login flow
            _logger.LogError(
                ex, 
                "Failed to log login attempt for {Email} from {IpAddress}", 
                email, 
                ipAddress);
        }
    }

    public async Task<List<LoginAttemptDto>> GetRecentLoginAttemptsAsync(int count = 100)
    {
        try
        {
            return await _context.LoginAttempts
                .OrderByDescending(x => x.CreatedAt)
                .Take(count)
                .Select(x => new LoginAttemptDto
                {
                    Id = x.Id,
                    Email = x.Email,
                    IpAddress = x.IpAddress,
                    Success = x.Success,
                    FailureReason = x.FailureReason,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent login attempts");
            throw;
        }
    }

    public async Task<Dictionary<string, int>> GetTopFailedIpsAsync(int count = 20)
    {
        try
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-24);

            var result = await _context.LoginAttempts
                .Where(x => !x.Success && x.CreatedAt >= cutoffTime)
                .GroupBy(x => x.IpAddress)
                .Select(g => new { IpAddress = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(count)
                .ToDictionaryAsync(x => x.IpAddress, x => x.Count);

            _logger.LogInformation("Retrieved top {Count} failed IPs", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve top failed IPs");
            throw;
        }
    }

    public async Task<List<IpBlacklistDto>> GetBlacklistedIpsAsync()
    {
        try
        {
            return await _context.IpBlacklist
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new IpBlacklistDto
                {
                    Id = x.Id,
                    IpAddress = x.IpAddress,
                    Reason = x.Reason ?? string.Empty,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve blacklisted IPs");
            throw;
        }
    }

    public async Task CleanupOldLoginAttemptsAsync(int daysToKeep = 30)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            
            var oldAttempts = await _context.LoginAttempts
                .Where(x => x.CreatedAt < cutoffDate)
                .ToListAsync();

            if (oldAttempts.Any())
            {
                _context.LoginAttempts.RemoveRange(oldAttempts);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation(
                    "🧹 Cleaned up {Count} old login attempts older than {Days} days", 
                    oldAttempts.Count, 
                    daysToKeep);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old login attempts");
        }
    }
}

// END OF FILE - DO NOT ADD ANYTHING BELOW THIS LINE