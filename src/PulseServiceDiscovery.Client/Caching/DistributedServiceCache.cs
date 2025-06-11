using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseServiceDiscovery.Abstractions.Models;
using PulseServiceDiscovery.Client.Options;
using System.Text.Json;

namespace PulseServiceDiscovery.Client.Caching;

/// <summary>
/// 基于分布式缓存的服务缓存实现
/// </summary>
public class DistributedServiceCache : IServiceCache
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<DistributedServiceCache> _logger;
    private readonly CacheOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public DistributedServiceCache(
        IDistributedCache distributedCache,
        ILogger<DistributedServiceCache> logger,
        IOptions<ClientOptions> clientOptions)
    {
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = clientOptions?.Value?.Cache ?? throw new ArgumentNullException(nameof(clientOptions));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<IReadOnlyList<ServiceEndpoint>?> GetAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);

        try
        {
            var cachedData = await _distributedCache.GetStringAsync(key, cancellationToken);

            if (!string.IsNullOrEmpty(cachedData))
            {
                var endpoints = JsonSerializer.Deserialize<List<ServiceEndpoint>>(cachedData, _jsonOptions);
                _logger.LogDebug("Cache hit for service: {ServiceName}, found {Count} endpoints",
                    serviceName, endpoints?.Count ?? 0);
                return endpoints?.AsReadOnly();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cached data for service: {ServiceName}", serviceName);
        }

        _logger.LogDebug("Cache miss for service: {ServiceName}", serviceName);
        return null;
    }

    public async Task SetAsync(string serviceName, IReadOnlyList<ServiceEndpoint> endpoints, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        await SetInternalAsync(serviceName, endpoints, _options.DefaultTtl, cancellationToken);
    }

    public async Task SetAsync(string serviceName, IReadOnlyList<ServiceEndpoint> endpoints, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        if (endpoints == null)
            throw new ArgumentNullException(nameof(endpoints));

        await SetInternalAsync(serviceName, endpoints, ttl, cancellationToken);
    }

    private async Task SetInternalAsync(string serviceName, IReadOnlyList<ServiceEndpoint> endpoints, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(serviceName);

        try
        {
            var json = JsonSerializer.Serialize(endpoints, _jsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            };

            // 如果配置了滑动过期，也设置滑动过期
            if (_options.SlidingExpiration.HasValue)
            {
                options.SlidingExpiration = _options.SlidingExpiration.Value;
            }

            await _distributedCache.SetStringAsync(key, json, options, cancellationToken);

            _logger.LogDebug("Cached {Count} endpoints for service: {ServiceName}, TTL: {TTL}",
                endpoints.Count, serviceName, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache data for service: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task RemoveAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);

        try
        {
            await _distributedCache.RemoveAsync(key, cancellationToken);
            _logger.LogDebug("Removed cache entry for service: {ServiceName}", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache entry for service: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // 分布式缓存通常没有Clear方法，这里只记录警告
        _logger.LogWarning("Clear operation is not supported by distributed cache. Consider using pattern-based removal if needed.");

        // 如果需要实现清理，可以维护一个键列表或使用支持模式删除的缓存提供程序
        await Task.CompletedTask;
    }

    public async Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);

        try
        {
            var cachedData = await _distributedCache.GetAsync(key, cancellationToken);
            return cachedData != null && cachedData.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence for service: {ServiceName}", serviceName);
            return false;
        }
    }

    public async Task RefreshAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));

        var key = GetCacheKey(serviceName);

        try
        {
            await _distributedCache.RefreshAsync(key, cancellationToken);
            _logger.LogDebug("Refreshed cache entry for service: {ServiceName}", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache entry for service: {ServiceName}", serviceName);
            throw;
        }
    }

    public Task<Dictionary<string, object>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new Dictionary<string, object>
        {
            ["CacheType"] = "Distributed",
            ["DefaultTtl"] = _options.DefaultTtl.ToString(),
            ["SlidingExpiration"] = _options.SlidingExpiration?.ToString() ?? "None",
            ["KeyPrefix"] = _options.KeyPrefix,
            ["SerializerOptions"] = new Dictionary<string, object>
            {
                ["PropertyNamingPolicy"] = _jsonOptions.PropertyNamingPolicy?.GetType().Name ?? "None",
                ["WriteIndented"] = _jsonOptions.WriteIndented,
                ["DefaultIgnoreCondition"] = _jsonOptions.DefaultIgnoreCondition.ToString()
            }
        };

        return Task.FromResult(stats);
    }

    private string GetCacheKey(string serviceName)
    {
        return $"{_options.KeyPrefix}:services:{serviceName}";
    }
}
