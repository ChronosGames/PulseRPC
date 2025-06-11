using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Client.Options;

namespace PulseServiceDiscovery.Client.Caching;

/// <summary>
/// 基于内存的服务缓存实现
/// </summary>
public class MemoryServiceCache : IServiceCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryServiceCache> _logger;
    private readonly CacheOptions _options;

    public MemoryServiceCache(
        IMemoryCache memoryCache,
        ILogger<MemoryServiceCache> logger,
        IOptions<ClientOptions> clientOptions)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = clientOptions?.Value?.Cache ?? throw new ArgumentNullException(nameof(clientOptions));
    }

    public Task<IReadOnlyList<ServiceEndpoint>?> GetAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);

        if (_memoryCache.TryGetValue(key, out var cachedValue) && cachedValue is IReadOnlyList<ServiceEndpoint> endpoints)
        {
            _logger.LogDebug("Cache hit for service: {ServiceName}, found {Count} endpoints", serviceName, endpoints.Count);
            return Task.FromResult<IReadOnlyList<ServiceEndpoint>?>(endpoints);
        }

        _logger.LogDebug("Cache miss for service: {ServiceName}", serviceName);
        return Task.FromResult<IReadOnlyList<ServiceEndpoint>?>(null);
    }

    public Task SetAsync(string serviceName, IReadOnlyList<ServiceEndpoint> endpoints, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        var key = GetCacheKey(serviceName);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.DefaultTtl,
            SlidingExpiration = _options.SlidingExpiration,
            Priority = CacheItemPriority.Normal
        };

        // 注册过期回调
        options.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
        {
            _logger.LogDebug("Cache entry evicted: {Key}, Reason: {Reason}", evictedKey, reason);
        });

        _memoryCache.Set(key, endpoints, options);

        _logger.LogDebug("Cached {Count} endpoints for service: {ServiceName}, TTL: {TTL}",
            endpoints.Count, serviceName, _options.DefaultTtl);

        return Task.CompletedTask;
    }

    public Task SetAsync(string serviceName, IReadOnlyList<ServiceEndpoint> endpoints, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        var key = GetCacheKey(serviceName);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl,
            SlidingExpiration = _options.SlidingExpiration,
            Priority = CacheItemPriority.Normal
        };

        // 注册过期回调
        options.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
        {
            _logger.LogDebug("Cache entry evicted: {Key}, Reason: {Reason}", evictedKey, reason);
        });

        _memoryCache.Set(key, endpoints, options);

        _logger.LogDebug("Cached {Count} endpoints for service: {ServiceName}, Custom TTL: {TTL}",
            endpoints.Count, serviceName, ttl);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);
        _memoryCache.Remove(key);

        _logger.LogDebug("Removed cache entry for service: {ServiceName}", serviceName);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // MemoryCache没有直接的Clear方法，需要通过反射或者重新创建
        if (_memoryCache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0); // 压缩所有缓存项
        }

        _logger.LogInformation("Memory cache cleared");
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);
        var exists = _memoryCache.TryGetValue(key, out _);

        return Task.FromResult(exists);
    }

    public Task RefreshAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);

        // 刷新缓存项的滑动过期时间
        if (_memoryCache.TryGetValue(key, out var value) && value is IReadOnlyList<ServiceEndpoint> endpoints)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.DefaultTtl,
                SlidingExpiration = _options.SlidingExpiration,
                Priority = CacheItemPriority.Normal
            };

            _memoryCache.Set(key, endpoints, options);
            _logger.LogDebug("Refreshed cache entry for service: {ServiceName}", serviceName);
        }

        return Task.CompletedTask;
    }

    public Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>
        {
            ["CacheType"] = "Memory",
            ["DefaultTtl"] = _options.DefaultTtl.ToString(),
            ["SlidingExpiration"] = _options.SlidingExpiration?.ToString() ?? "None"
        };

        // 尝试获取内存缓存的统计信息
        if (_memoryCache is MemoryCache memoryCache)
        {
            // 这些属性可能需要反射访问，因为它们是内部的
            try
            {
                var field = typeof(MemoryCache).GetField("_options",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (field?.GetValue(memoryCache) is MemoryCacheOptions options)
                {
                    stats["SizeLimit"] = options.SizeLimit?.ToString() ?? "Unlimited";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get memory cache statistics");
            }
        }

        return Task.FromResult(stats);
    }

    private string GetCacheKey(string serviceName)
    {
        return $"{_options.KeyPrefix}:services:{serviceName}";
    }
}
