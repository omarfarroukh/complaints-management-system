namespace CMS.Application.Interfaces;

public interface ICacheService
{
    /// <summary>
    /// retreives typed data from cache
    /// </summary>
    Task<T?> GetAsync<T>(string key);

    /// <summary>
    /// Saves data to cache with expiration and associated tags
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan expiration, params string[] tags);

    /// <summary>
    /// Removes a specific key
    /// </summary>
    Task RemoveAsync(string key);

    /// <summary>
    /// Removes all keys associated with specific tags
    /// </summary>
    Task RemoveByTagAsync(params string[] tags);
}