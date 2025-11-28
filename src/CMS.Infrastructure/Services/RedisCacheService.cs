using CMS.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;
using System.IO.Compression;
using System.Text.Json;

namespace CMS.Infrastructure.Services;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _instancePrefix; 

    // Changed: We now inject the string directly instead of IOptions<RedisCacheOptions>
    // This fixes CS0234 and CS0246
    public RedisCacheService(
        IDistributedCache cache, 
        IConnectionMultiplexer redis, 
        string instancePrefix = "CMS_") 
    {
        _cache = cache;
        _redis = redis;
        _instancePrefix = instancePrefix ?? string.Empty; 
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var compressedBytes = await _cache.GetAsync(key);
        if (compressedBytes == null) return default;

        try
        {
            var jsonBytes = Decompress(compressedBytes);
            return JsonSerializer.Deserialize<T>(jsonBytes);
        }
        catch
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, params string[] tags)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value);
        var compressedBytes = Compress(jsonBytes);

        await _cache.SetAsync(key, compressedBytes, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration
        });

        if (tags != null && tags.Length > 0)
        {
            var db = _redis.GetDatabase();
            foreach (var tag in tags)
            {
                var tagKey = $"tag:{tag}";
                // Store RAW key in the Set
                await db.SetAddAsync(tagKey, key);
                await db.KeyExpireAsync(tagKey, expiration.Add(TimeSpan.FromMinutes(5)));
            }
        }
    }

    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    public async Task RemoveByTagAsync(params string[] tags)
    {
        var db = _redis.GetDatabase();
        foreach (var tag in tags)
        {
            var tagKey = $"tag:{tag}";
            var keys = await db.SetMembersAsync(tagKey);

            if (keys.Length > 0)
            {
                // Fix: Prepend the InstanceName (_instancePrefix) manually
                // because IDistributedCache adds it automatically, but manual KeyDeleteAsync does not.
                var redisKeys = keys
                    .Select(k => (RedisKey) AppendPrefix(k.ToString()))
                    .ToArray();

                await db.KeyDeleteAsync(redisKeys);
            }

            await db.KeyDeleteAsync(tagKey);
        }
    }

    private string AppendPrefix(string key)
    {
        // Avoid double prefixing if the logic changes later
        if (key.StartsWith(_instancePrefix)) return key;
        return _instancePrefix + key;
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var dstream = new GZipStream(output, CompressionLevel.Optimal))
        {
            dstream.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var dstream = new GZipStream(input, CompressionMode.Decompress);
        dstream.CopyTo(output);
        return output.ToArray();
    }
}