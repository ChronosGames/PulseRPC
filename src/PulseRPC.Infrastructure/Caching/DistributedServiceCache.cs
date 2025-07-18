using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace PulseRPC.Caching;

/// <summary>
/// 基于分布式缓存的服务缓存实现
/// </summary>
public class DistributedServiceCache(
    IDistributedCache distributedCache,
    ILogger<DistributedServiceCache> logger,
    IOptions<CachingOptions> clientOptions)
    : ICacheService
{
    private readonly IDistributedCache _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
    private readonly ILogger<DistributedServiceCache> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly CachingOptions _options = clientOptions.Value ?? throw new ArgumentNullException(nameof(clientOptions));
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private readonly ConcurrentDictionary<string, CacheEntry> _accessLog = new();
    private long _hitCount = 0;
    private long _missCount = 0;
    private bool _disposed = false;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var cachedData = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (!string.IsNullOrEmpty(cachedData))
            {
                var value = JsonSerializer.Deserialize<T>(cachedData, _jsonOptions);
                UpdateStatistics(key, true);
                return value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cached data for key: {Key}", key);
        }

        UpdateStatistics(key, false);
        return default;
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var value = await _distributedCache.GetStringAsync(key, cancellationToken);
            UpdateStatistics(key, value != null);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cached string for key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry ?? _options.DefaultTtl,
                SlidingExpiration = (expiry ?? _options.DefaultTtl) / 2
            };

            await _distributedCache.SetStringAsync(key, json, options, cancellationToken);
            _logger.LogDebug("Cached value for key: {Key}, TTL: {TTL}", key, options.AbsoluteExpirationRelativeToNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache value for key: {Key}", key);
            throw;
        }
    }

    public async Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry ?? _options.DefaultTtl,
                SlidingExpiration = (expiry ?? _options.DefaultTtl) / 2
            };

            await _distributedCache.SetStringAsync(key, value, options, cancellationToken);
            _logger.LogDebug("Cached string for key: {Key}, TTL: {TTL}", key, options.AbsoluteExpirationRelativeToNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache string for key: {Key}", key);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            var cachedData = await _distributedCache.GetAsync(key, cancellationToken);
            return cachedData != null && cachedData.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence for key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        try
        {
            await _distributedCache.RemoveAsync(key, cancellationToken);
            _logger.LogDebug("Removed cache entry for key: {Key}", key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache entry for key: {Key}", key);
            return false;
        }
    }

    public async Task<long> RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持模式删除，这里返回0
        _logger.LogWarning("Pattern-based removal is not supported by distributed cache");
        return 0;
    }

    public async Task<IDictionary<string, T?>> GetMultipleAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T?>();
        foreach (var key in keys)
        {
            result[key] = await GetAsync<T>(key, cancellationToken);
        }
        return result;
    }

    public async Task SetMultipleAsync<T>(IDictionary<string, T> keyValuePairs, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        foreach (var kvp in keyValuePairs)
        {
            await SetAsync(kvp.Key, kvp.Value, expiry, cancellationToken);
        }
    }

    public async Task<long> RemoveMultipleAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        long removedCount = 0;
        foreach (var key in keys)
        {
            if (await RemoveAsync(key, cancellationToken))
            {
                removedCount++;
            }
        }
        return removedCount;
    }

    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var value = await GetAsync<T>(key, cancellationToken);
        if (value != null)
        {
            return value;
        }

        value = await factory();
        await SetAsync(key, value, expiry, cancellationToken);
        return value;
    }

    public async Task<long> IncrementAsync(string key, long value = 1, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var currentValue = await GetAsync<long>(key, cancellationToken);
        var newValue = currentValue + value;
        await SetAsync(key, newValue, expiry, cancellationToken);
        return newValue;
    }

    public async Task<long> DecrementAsync(string key, long value = 1, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        return await IncrementAsync(key, -value, expiry, cancellationToken);
    }

    public async Task<double> IncrementAsync(string key, double value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var currentValue = await GetAsync<double>(key, cancellationToken);
        var newValue = currentValue + value;
        await SetAsync(key, newValue, expiry, cancellationToken);
        return newValue;
    }

    public async Task<bool> ExpireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await GetStringAsync(key, cancellationToken);
            if (value != null)
            {
                await SetStringAsync(key, value, expiry, cancellationToken);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set expiry for key: {Key}", key);
            return false;
        }
    }

    public async Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持获取TTL，这里返回null
        _logger.LogWarning("GetTimeToLive is not supported by distributed cache");
        return null;
    }

    public async Task<bool> PersistAsync(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持持久化，这里返回false
        _logger.LogWarning("Persist is not supported by distributed cache");
        return false;
    }

    public async Task<T?> HashGetAsync<T>(string key, string field, CancellationToken cancellationToken = default)
    {
        var hashKey = $"{key}:{field}";
        return await GetAsync<T>(hashKey, cancellationToken);
    }

    public async Task HashSetAsync<T>(string key, string field, T value, CancellationToken cancellationToken = default)
    {
        var hashKey = $"{key}:{field}";
        await SetAsync(hashKey, value, null, cancellationToken);
    }

    public async Task<bool> HashExistsAsync(string key, string field, CancellationToken cancellationToken = default)
    {
        var hashKey = $"{key}:{field}";
        return await ExistsAsync(hashKey, cancellationToken);
    }

    public async Task<bool> HashDeleteAsync(string key, string field, CancellationToken cancellationToken = default)
    {
        var hashKey = $"{key}:{field}";
        return await RemoveAsync(hashKey, cancellationToken);
    }

    public async Task<IDictionary<string, T?>> HashGetAllAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持获取所有哈希字段，这里返回空字典
        _logger.LogWarning("HashGetAll is not supported by distributed cache");
        return new Dictionary<string, T?>();
    }

    public async Task<long> HashLengthAsync(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持获取哈希长度，这里返回0
        _logger.LogWarning("HashLength is not supported by distributed cache");
        return 0;
    }

    public async Task<long> ListPushAsync<T>(string key, T value, bool toLeft = true, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持列表操作，这里返回0
        _logger.LogWarning("ListPush is not supported by distributed cache");
        return 0;
    }

    public async Task<T?> ListPopAsync<T>(string key, bool fromLeft = true, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持列表操作，这里返回default
        _logger.LogWarning("ListPop is not supported by distributed cache");
        return default;
    }

    public async Task<IList<T?>> ListRangeAsync<T>(string key, long start = 0, long stop = -1, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持列表操作，这里返回空列表
        _logger.LogWarning("ListRange is not supported by distributed cache");
        return new List<T?>();
    }

    public async Task<long> ListLengthAsync(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持列表操作，这里返回0
        _logger.LogWarning("ListLength is not supported by distributed cache");
        return 0;
    }

    public async Task<bool> SetAddAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持集合操作，这里返回false
        _logger.LogWarning("SetAdd is not supported by distributed cache");
        return false;
    }

    public async Task<bool> SetRemoveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持集合操作，这里返回false
        _logger.LogWarning("SetRemove is not supported by distributed cache");
        return false;
    }

    public async Task<bool> SetContainsAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持集合操作，这里返回false
        _logger.LogWarning("SetContains is not supported by distributed cache");
        return false;
    }

    public async Task<ISet<T?>> SetMembersAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持集合操作，这里返回空集合
        _logger.LogWarning("SetMembers is not supported by distributed cache");
        return new HashSet<T?>();
    }

    public async Task<long> SetLengthAsync(string key, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持集合操作，这里返回0
        _logger.LogWarning("SetLength is not supported by distributed cache");
        return 0;
    }

    public async Task<IDisposable?> AcquireLockAsync(string key, TimeSpan expiry, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持分布式锁，这里返回null
        _logger.LogWarning("AcquireLock is not supported by distributed cache");
        return null;
    }

    public async Task<bool> ReleaseLockAsync(string key, string lockValue, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持分布式锁，这里返回false
        _logger.LogWarning("ReleaseLock is not supported by distributed cache");
        return false;
    }

    public async Task PublishAsync<T>(string channel, T message, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持发布订阅，这里什么都不做
        _logger.LogWarning("Publish is not supported by distributed cache");
    }

    public async Task SubscribeAsync<T>(string channel, Func<string, T, Task> handler, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持发布订阅，这里什么都不做
        _logger.LogWarning("Subscribe is not supported by distributed cache");
    }

    public async Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持发布订阅，这里什么都不做
        _logger.LogWarning("Unsubscribe is not supported by distributed cache");
    }

    public Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>
        {
            ["CacheType"] = "Distributed",
            ["DefaultTtl"] = _options.DefaultTtl.ToString(),
            ["SlidingExpiration"] = (_options.DefaultTtl / 2).ToString(),
            ["KeyPrefix"] = "pulse",
            ["SerializerOptions"] = new Dictionary<string, object>
            {
                ["PropertyNamingPolicy"] = _jsonOptions.PropertyNamingPolicy?.GetType().Name ?? "None",
                ["WriteIndented"] = _jsonOptions.WriteIndented,
                ["DefaultIgnoreCondition"] = _jsonOptions.DefaultIgnoreCondition.ToString()
            }
        };

        return Task.FromResult(stats);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _distributedCache.GetAsync("ping", cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ping distributed cache");
            return false;
        }
    }

    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常不支持清空所有数据，这里什么都不做
        _logger.LogWarning("FlushAll is not supported by distributed cache");
    }

    private void UpdateStatistics(string key, bool hit)
    {
        if (hit)
        {
            Interlocked.Increment(ref _hitCount);
        }
        else
        {
            Interlocked.Increment(ref _missCount);
        }

        _accessLog.AddOrUpdate(key, new CacheEntry { LastAccess = DateTime.UtcNow }, (k, old) => new CacheEntry { LastAccess = DateTime.UtcNow });
    }

    private record CacheEntry
    {
        public DateTime LastAccess { get; init; }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _accessLog.Clear();
            _logger.LogDebug("DistributedServiceCache disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing DistributedServiceCache");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
