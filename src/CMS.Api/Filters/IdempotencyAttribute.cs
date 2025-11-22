using System;

namespace CMS.Api.Filters
{
    /// <summary>
    /// Marks an endpoint as requiring idempotency support.
    /// Responses will be cached and replayed for duplicate requests with the same Idempotency-Key header.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class IdempotencyAttribute : Attribute
    {
        /// <summary>
        /// How long (in minutes) to cache the response
        /// </summary>
        public int CacheDurationMinutes { get; }

        /// <summary>
        /// Creates a new idempotency attribute
        /// </summary>
        /// <param name="cacheDurationMinutes">Duration to cache the response (default: 60 minutes)</param>
        public IdempotencyAttribute(int cacheDurationMinutes = 60)
        {
            if (cacheDurationMinutes <= 0)
                throw new ArgumentException("Cache duration must be positive", nameof(cacheDurationMinutes));

            CacheDurationMinutes = cacheDurationMinutes;
        }
    }
}