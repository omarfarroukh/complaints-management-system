using CMS.Application.DTOs;
using CMS.Application.Interfaces;
using StackExchange.Redis;

namespace CMS.Infrastructure.Services
{
    public class ComplaintLockService : IComplaintLockService
    {
        private readonly IConnectionMultiplexer _redis;
        private const int DefaultTtlSeconds = 300; // 5 minutes

        public ComplaintLockService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task<bool> AcquireLockAsync(Guid complaintId, string userId, int ttlSeconds = 300)
        {
            var db = _redis.GetDatabase();
            var key = GetLockKey(complaintId);
            var lockDuration = TimeSpan.FromSeconds(ttlSeconds);

            // Try to set the key if it doesn't exist
            var acquired = await db.StringSetAsync(key, userId, lockDuration, When.NotExists);

            if (!acquired)
            {
                // If already locked, check if it's the same user (re-entrant)
                var currentHolder = await db.StringGetAsync(key);
                if (currentHolder == userId)
                {
                    // Extend the lock
                    await db.KeyExpireAsync(key, lockDuration);
                    return true;
                }
            }

            return acquired;
        }

        public async Task<bool> ReleaseLockAsync(Guid complaintId, string userId)
        {
            var db = _redis.GetDatabase();
            var key = GetLockKey(complaintId);

            var currentHolder = await db.StringGetAsync(key);
            if (currentHolder == userId)
            {
                return await db.KeyDeleteAsync(key);
            }
            return false;
        }

        public async Task<string?> GetCurrentLockHolderAsync(Guid complaintId)
        {
            var db = _redis.GetDatabase();
            var key = GetLockKey(complaintId);
            return await db.StringGetAsync(key);
        }

        public async Task<ComplaintLockDto?> GetLockInfoAsync(Guid complaintId)
        {
            var db = _redis.GetDatabase();
            var key = GetLockKey(complaintId);
            var holder = await db.StringGetAsync(key);

            if (holder.IsNullOrEmpty)
                return null;

            var ttl = await db.KeyTimeToLiveAsync(key);
            var lockedAt = DateTime.UtcNow - (ttl.HasValue ? TimeSpan.FromSeconds(DefaultTtlSeconds) - ttl.Value : TimeSpan.Zero);

            return new ComplaintLockDto
            {
                ComplaintId = complaintId,
                LockedBy = holder.ToString(),
                LockedAt = lockedAt,
                ExpiresAt = DateTime.UtcNow.Add(ttl ?? TimeSpan.Zero),
                Success = true,
                Message = $"Locked by {holder}"
            };
        }

        private string GetLockKey(Guid complaintId) => $"complaint_lock:{complaintId}";
    }
}
