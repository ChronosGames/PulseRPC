using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;

namespace PulseRPC.Client;

/// <summary>
/// 生产级连接注册表实现 - 支持缓存、索引、监控和性能优化
/// </summary>
internal sealed class SimpleConnectionRegistry : IConnectionRegistry, IDisposable
{
    private readonly ILogger<SimpleConnectionRegistry> _logger;
    private readonly EnhancedRegistryOptions _options;

    // 核心存储
    private readonly ConcurrentDictionary<string, IClientChannel> _connections = new();
    private readonly ReaderWriterLockSlim _indexLock = new();

    // 索引优化
    private readonly ConcurrentDictionary<string, ConcurrentHashSet<string>> _tagIndex = new();
    private readonly ConcurrentDictionary<string, ConcurrentHashSet<string>> _nameIndex = new();
    private readonly ConcurrentDictionary<ExtendedConnectionState, ConcurrentHashSet<string>> _stateIndex = new();

    // 缓存优化
    private readonly ConcurrentDictionary<string, CachedQueryResult> _queryCache = new();
    private readonly Timer _cacheCleanupTimer;

    // 监控和统计
    private readonly RegistryMetrics _metrics = new();
    private readonly object _metricsLock = new();

    // 状态管理
    private volatile bool _disposed;

    /// <summary>
    /// 连接注册事件
    /// </summary>
    public event EventHandler<ConnectionRegisteredEventArgs>? ConnectionRegistered;

    /// <summary>
    /// 连接注销事件
    /// </summary>
    public event EventHandler<ConnectionUnregisteredEventArgs>? ConnectionUnregistered;

    /// <summary>
    /// 构造函数
    /// </summary>
    public SimpleConnectionRegistry(
        EnhancedRegistryOptions? options = null,
        ILogger<SimpleConnectionRegistry>? logger = null)
    {
        _options = options ?? new EnhancedRegistryOptions();
        _logger = logger ?? NullLogger<SimpleConnectionRegistry>.Instance;

        // 启动缓存清理定时器
        _cacheCleanupTimer = new Timer(
            CleanupExpiredCache,
            null,
            _options.CacheCleanupInterval,
            _options.CacheCleanupInterval);

        _logger.LogInformation("SimpleConnectionRegistry initialized with options: {Options}", _options);
    }

    /// <summary>
    /// 注册连接
    /// </summary>
    public void RegisterConnection(IClientChannel connection)
    {
        ThrowIfDisposed();

        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        if (string.IsNullOrEmpty(connection.Id))
            throw new ArgumentException("连接ID不能为空", nameof(connection));

        var added = _connections.TryAdd(connection.Id, connection);
        if (added)
        {
            try
            {
                // 更新索引
                UpdateIndexesOnAdd(connection);

                // 清理相关缓存
                InvalidateRelevantCache(connection);

                // 更新统计
                UpdateMetricsOnAdd(connection);

                // 触发事件
                ConnectionRegistered?.Invoke(this, new ConnectionRegisteredEventArgs { Connection = connection });

                _logger.LogDebug("Connection {ConnectionId} registered successfully", connection.Id);
            }
            catch (Exception ex)
            {
                // 回滚操作
                _connections.TryRemove(connection.Id, out _);
                _logger.LogError(ex, "Failed to complete registration for connection {ConnectionId}", connection.Id);
                throw;
            }
        }
        else
        {
            _logger.LogWarning("Connection {ConnectionId} already exists, registration skipped", connection.Id);
        }
    }

    /// <summary>
    /// 注销连接
    /// </summary>
    public void UnregisterConnection(string connectionId, string reason = "手动注销")
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        if (_connections.TryRemove(connectionId, out var removedConnection))
        {
            try
            {
                // 更新索引
                UpdateIndexesOnRemove(removedConnection);

                // 清理相关缓存
                InvalidateRelevantCache(removedConnection);

                // 更新统计
                UpdateMetricsOnRemove(removedConnection);

                // 触发事件
                ConnectionUnregistered?.Invoke(this, new ConnectionUnregisteredEventArgs
                {
                    ConnectionId = connectionId,
                    Reason = reason
                });

                _logger.LogDebug("Connection {ConnectionId} unregistered, reason: {Reason}", connectionId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during unregistration of connection {ConnectionId}", connectionId);
            }
        }
        else
        {
            _logger.LogDebug("Connection {ConnectionId} not found during unregistration", connectionId);
        }
    }

    /// <summary>
    /// 根据标签获取连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetConnectionsByTags(Dictionary<string, string> tags)
    {
        ThrowIfDisposed();

        if (tags == null || tags.Count == 0)
            return Array.Empty<IClientChannel>();

        // 尝试从缓存获取
        var cacheKey = GenerateCacheKey("tags", tags);
        if (_queryCache.TryGetValue(cacheKey, out var cachedResult) && !cachedResult.IsExpired)
        {
            _metrics.CacheHits++;
            return cachedResult.Connections;
        }

        _metrics.CacheMisses++;

        // 使用索引优化查询
        var candidateIds = GetCandidatesByTags(tags);
        var matchingConnections = new List<IClientChannel>();

        foreach (var connectionId in candidateIds)
        {
            if (_connections.TryGetValue(connectionId, out var connection) &&
                connection.Descriptor?.Tags != null &&
                MatchesTags(connection.Descriptor.Tags, tags))
            {
                matchingConnections.Add(connection);
            }
        }

        var result = matchingConnections.AsReadOnly();

        // 缓存结果
        if (_options.EnableQueryCache)
        {
            var cacheEntry = new CachedQueryResult
            {
                Connections = result,
                ExpiresAt = DateTime.UtcNow.Add(_options.QueryCacheExpiration),
                QueryType = "tags"
            };
            _queryCache.TryAdd(cacheKey, cacheEntry);
        }

        return result;
    }

    /// <summary>
    /// 获取连接
    /// </summary>
    public IClientChannel? GetConnection(string connectionId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(connectionId))
            return null;

        _metrics.TotalQueries++;
        return _connections.TryGetValue(connectionId, out var connection) ? connection : null;
    }

    /// <summary>
    /// 获取所有连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetAllConnections()
    {
        ThrowIfDisposed();

        // 尝试从缓存获取
        const string cacheKey = "all_connections";
        if (_queryCache.TryGetValue(cacheKey, out var cachedResult) && !cachedResult.IsExpired)
        {
            _metrics.CacheHits++;
            return cachedResult.Connections;
        }

        _metrics.CacheMisses++;
        _metrics.TotalQueries++;

        var result = _connections.Values.ToList().AsReadOnly();

        // 缓存结果（短期缓存）
        if (_options.EnableQueryCache)
        {
            var cacheEntry = new CachedQueryResult
            {
                Connections = result,
                ExpiresAt = DateTime.UtcNow.AddSeconds(30), // 短期缓存
                QueryType = "all"
            };
            _queryCache.TryAdd(cacheKey, cacheEntry);
        }

        return result;
    }

    /// <summary>
    /// 根据服务名称获取连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetConnectionsByServiceName(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return Array.Empty<IClientChannel>();

        var matchingConnections = _connections.Values
            .Where(c => string.Equals(c.Descriptor?.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matchingConnections.AsReadOnly();
    }

    /// <summary>
    /// 根据连接状态获取连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetConnectionsByState(ExtendedConnectionState state)
    {
        var matchingConnections = _connections.Values
            .Where(c => c.State == state)
            .ToList();

        return matchingConnections.AsReadOnly();
    }

    /// <summary>
    /// 获取健康的连接
    /// </summary>
    public IReadOnlyList<IClientChannel> GetHealthyConnections()
    {
        var healthyConnections = _connections.Values
            .Where(c => c.State == ExtendedConnectionState.Connected ||
                       c.State == ExtendedConnectionState.Active)
            .ToList();

        return healthyConnections.AsReadOnly();
    }

    /// <summary>
    /// 清理所有连接
    /// </summary>
    public void Clear()
    {
        var connectionIds = _connections.Keys.ToList();
        _connections.Clear();

        // 触发注销事件
        foreach (var connectionId in connectionIds)
        {
            ConnectionUnregistered?.Invoke(this, new ConnectionUnregisteredEventArgs
            {
                ConnectionId = connectionId,
                Reason = "注册表清理"
            });
        }
    }

    /// <summary>
    /// 获取连接数量
    /// </summary>
    public int Count => _connections.Count;

    #region Private Helper Methods

    /// <summary>
    /// 检查标签是否匹配
    /// </summary>
    private static bool MatchesTags(Dictionary<string, string> connectionTags, Dictionary<string, string> queryTags)
    {
        foreach (var queryTag in queryTags)
        {
            if (!connectionTags.TryGetValue(queryTag.Key, out var connectionTagValue) ||
                !string.Equals(connectionTagValue, queryTag.Value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 更新索引（添加连接）
    /// </summary>
    private void UpdateIndexesOnAdd(IClientChannel connection)
    {
        _indexLock.EnterWriteLock();
        try
        {
            // 更新名称索引
            if (!string.IsNullOrEmpty(connection.Descriptor?.Name))
            {
                var nameSet = _nameIndex.GetOrAdd(connection.Descriptor.Name, _ => new ConcurrentHashSet<string>());
                nameSet.Add(connection.Id);
            }

            // 更新标签索引
            if (connection.Descriptor?.Tags != null)
            {
                foreach (var tag in connection.Descriptor.Tags)
                {
                    var tagKey = $"{tag.Key}:{tag.Value}";
                    var tagSet = _tagIndex.GetOrAdd(tagKey, _ => new ConcurrentHashSet<string>());
                    tagSet.Add(connection.Id);
                }
            }

            // 更新状态索引
            var stateSet = _stateIndex.GetOrAdd(connection.State, _ => new ConcurrentHashSet<string>());
            stateSet.Add(connection.Id);
        }
        finally
        {
            _indexLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 更新索引（移除连接）
    /// </summary>
    private void UpdateIndexesOnRemove(IClientChannel connection)
    {
        _indexLock.EnterWriteLock();
        try
        {
            // 更新名称索引
            if (!string.IsNullOrEmpty(connection.Descriptor?.Name) &&
                _nameIndex.TryGetValue(connection.Descriptor.Name, out var nameSet))
            {
                nameSet.Remove(connection.Id);
                if (nameSet.Count == 0)
                {
                    _nameIndex.TryRemove(connection.Descriptor.Name, out _);
                }
            }

            // 更新标签索引
            if (connection.Descriptor?.Tags != null)
            {
                foreach (var tag in connection.Descriptor.Tags)
                {
                    var tagKey = $"{tag.Key}:{tag.Value}";
                    if (_tagIndex.TryGetValue(tagKey, out var tagSet))
                    {
                        tagSet.Remove(connection.Id);
                        if (tagSet.Count == 0)
                        {
                            _tagIndex.TryRemove(tagKey, out _);
                        }
                    }
                }
            }

            // 更新状态索引
            if (_stateIndex.TryGetValue(connection.State, out var stateSet))
            {
                stateSet.Remove(connection.Id);
                if (stateSet.Count == 0)
                {
                    _stateIndex.TryRemove(connection.State, out _);
                }
            }
        }
        finally
        {
            _indexLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 生成缓存键
    /// </summary>
    private static string GenerateCacheKey(string queryType, Dictionary<string, string> parameters)
    {
        var sortedParams = parameters.OrderBy(kvp => kvp.Key)
            .Select(kvp => $"{kvp.Key}={kvp.Value}")
            .ToArray();
        return $"{queryType}:{string.Join("&", sortedParams)}";
    }

    /// <summary>
    /// 根据标签获取候选连接ID
    /// </summary>
    private HashSet<string> GetCandidatesByTags(Dictionary<string, string> tags)
    {
        HashSet<string>? candidates = null;

        foreach (var tag in tags)
        {
            var tagKey = $"{tag.Key}:{tag.Value}";
            if (_tagIndex.TryGetValue(tagKey, out var tagSet))
            {
                var currentSet = tagSet.ToHashSet();
                if (candidates == null)
                {
                    candidates = currentSet;
                }
                else
                {
                    candidates.IntersectWith(currentSet);
                }
            }
            else
            {
                // 如果任何标签没有匹配，返回空集合
                return new HashSet<string>();
            }
        }

        return candidates ?? new HashSet<string>();
    }

    /// <summary>
    /// 失效相关缓存
    /// </summary>
    private void InvalidateRelevantCache(IClientChannel connection)
    {
        if (!_options.EnableQueryCache) return;

        var keysToRemove = new List<string>();

        foreach (var kvp in _queryCache)
        {
            var cacheKey = kvp.Key;
            var cacheEntry = kvp.Value;

            // 失效全部连接缓存
            if (cacheEntry.QueryType == "all")
            {
                keysToRemove.Add(cacheKey);
            }
            // 失效健康连接缓存
            else if (cacheEntry.QueryType == "healthy")
            {
                keysToRemove.Add(cacheKey);
            }
            // 失效状态相关缓存
            else if (cacheEntry.QueryType == "state" && cacheKey.Contains(connection.State.ToString()))
            {
                keysToRemove.Add(cacheKey);
            }
            // 失效名称相关缓存
            else if (cacheEntry.QueryType == "name" && !string.IsNullOrEmpty(connection.Descriptor?.Name) &&
                     cacheKey.Contains(connection.Descriptor.Name))
            {
                keysToRemove.Add(cacheKey);
            }
            // 失效标签相关缓存
            else if (cacheEntry.QueryType == "tags" && connection.Descriptor?.Tags != null)
            {
                foreach (var tag in connection.Descriptor.Tags)
                {
                    if (cacheKey.Contains($"{tag.Key}={tag.Value}"))
                    {
                        keysToRemove.Add(cacheKey);
                        break;
                    }
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            _queryCache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 更新统计（添加）
    /// </summary>
    private void UpdateMetricsOnAdd(IClientChannel connection)
    {
        lock (_metricsLock)
        {
            _metrics.TotalRegistrations++;
            _metrics.LastRegistrationTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 更新统计（移除）
    /// </summary>
    private void UpdateMetricsOnRemove(IClientChannel connection)
    {
        lock (_metricsLock)
        {
            _metrics.TotalUnregistrations++;
            _metrics.LastUnregistrationTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 重置统计
    /// </summary>
    private void ResetMetrics()
    {
        lock (_metricsLock)
        {
            _metrics.Reset();
        }
    }

    /// <summary>
    /// 清理所有索引
    /// </summary>
    private void ClearAllIndexes()
    {
        _indexLock.EnterWriteLock();
        try
        {
            _tagIndex.Clear();
            _nameIndex.Clear();
            _stateIndex.Clear();
        }
        finally
        {
            _indexLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    private void CleanupExpiredCache(object? state)
    {
        try
        {
            var expiredKeys = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var kvp in _queryCache)
            {
                if (kvp.Value.ExpiresAt < now)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _queryCache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache cleanup");
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SimpleConnectionRegistry));
        }
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _cacheCleanupTimer?.Dispose();
            _indexLock?.Dispose();
            Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal");
        }

        _logger.LogInformation("EnhancedConnectionRegistry disposed");
    }

    #endregion
}

/// <summary>
/// 增强注册表配置选项
/// </summary>
public class EnhancedRegistryOptions
{
    /// <summary>
    /// 是否启用查询缓存
    /// </summary>
    public bool EnableQueryCache { get; set; } = true;

    /// <summary>
    /// 查询缓存过期时间
    /// </summary>
    public TimeSpan QueryCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 缓存清理间隔
    /// </summary>
    public TimeSpan CacheCleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 最大缓存项数
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// 是否启用索引优化
    /// </summary>
    public bool EnableIndexOptimization { get; set; } = true;

    public override string ToString()
    {
        return $"QueryCache={EnableQueryCache}, CacheExpiration={QueryCacheExpiration}, CleanupInterval={CacheCleanupInterval}, MaxCacheSize={MaxCacheSize}, IndexOptimization={EnableIndexOptimization}";
    }
}

/// <summary>
/// 缓存查询结果
/// </summary>
internal class CachedQueryResult
{
    /// <summary>
    /// 缓存的连接列表
    /// </summary>
    public IReadOnlyList<IClientChannel> Connections { get; set; } = Array.Empty<IClientChannel>();

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 查询类型
    /// </summary>
    public string QueryType { get; set; } = string.Empty;

    /// <summary>
    /// 是否已过期
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

/// <summary>
/// 注册表性能统计
/// </summary>
public class RegistryMetrics
{
    /// <summary>
    /// 总查询次数
    /// </summary>
    public long TotalQueries { get; set; }

    /// <summary>
    /// 缓存命中次数
    /// </summary>
    public long CacheHits { get; set; }

    /// <summary>
    /// 缓存未命中次数
    /// </summary>
    public long CacheMisses { get; set; }

    /// <summary>
    /// 总注册次数
    /// </summary>
    public long TotalRegistrations { get; set; }

    /// <summary>
    /// 总注销次数
    /// </summary>
    public long TotalUnregistrations { get; set; }

    /// <summary>
    /// 最后注册时间
    /// </summary>
    public DateTime? LastRegistrationTime { get; set; }

    /// <summary>
    /// 最后注销时间
    /// </summary>
    public DateTime? LastUnregistrationTime { get; set; }

    /// <summary>
    /// 缓存命中率
    /// </summary>
    public double CacheHitRate => TotalQueries > 0 ? (double)CacheHits / TotalQueries : 0;

    /// <summary>
    /// 克隆统计信息
    /// </summary>
    public RegistryMetrics Clone()
    {
        return new RegistryMetrics
        {
            TotalQueries = TotalQueries,
            CacheHits = CacheHits,
            CacheMisses = CacheMisses,
            TotalRegistrations = TotalRegistrations,
            TotalUnregistrations = TotalUnregistrations,
            LastRegistrationTime = LastRegistrationTime,
            LastUnregistrationTime = LastUnregistrationTime
        };
    }

    /// <summary>
    /// 重置统计信息
    /// </summary>
    public void Reset()
    {
        TotalQueries = 0;
        CacheHits = 0;
        CacheMisses = 0;
        TotalRegistrations = 0;
        TotalUnregistrations = 0;
        LastRegistrationTime = null;
        LastUnregistrationTime = null;
    }

    public override string ToString()
    {
        return $"Queries={TotalQueries}, CacheHitRate={CacheHitRate:P2}, Registrations={TotalRegistrations}, Unregistrations={TotalUnregistrations}";
    }
}

/// <summary>
/// 注册表状态信息
/// </summary>
public class RegistryStatus
{
    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 状态分布
    /// </summary>
    public Dictionary<ExtendedConnectionState, int> StateDistribution { get; set; } = new();

    /// <summary>
    /// 缓存大小
    /// </summary>
    public int CacheSize { get; set; }

    /// <summary>
    /// 索引大小
    /// </summary>
    public Dictionary<string, int> IndexSizes { get; set; } = new();

    /// <summary>
    /// 性能指标
    /// </summary>
    public RegistryMetrics Metrics { get; set; } = new();

    public override string ToString()
    {
        var stateInfo = string.Join(", ", StateDistribution.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var indexInfo = string.Join(", ", IndexSizes.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"Connections={TotalConnections}, States=[{stateInfo}], Cache={CacheSize}, Indexes=[{indexInfo}], {Metrics}";
    }
}

/// <summary>
/// 线程安全的哈希集合
/// </summary>
internal class ConcurrentHashSet<T> : IDisposable where T : notnull
{
    private readonly HashSet<T> _set = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private volatile bool _disposed;

    /// <summary>
    /// 添加元素
    /// </summary>
    public bool Add(T item)
    {
        ThrowIfDisposed();
        _lock.EnterWriteLock();
        try
        {
            return _set.Add(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 移除元素
    /// </summary>
    public bool Remove(T item)
    {
        ThrowIfDisposed();
        _lock.EnterWriteLock();
        try
        {
            return _set.Remove(item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 检查是否包含元素
    /// </summary>
    public bool Contains(T item)
    {
        ThrowIfDisposed();
        _lock.EnterReadLock();
        try
        {
            return _set.Contains(item);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// 清空集合
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        _lock.EnterWriteLock();
        try
        {
            _set.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 获取元素数量
    /// </summary>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            _lock.EnterReadLock();
            try
            {
                return _set.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// 转换为哈希集合
    /// </summary>
    public HashSet<T> ToHashSet()
    {
        ThrowIfDisposed();
        _lock.EnterReadLock();
        try
        {
            return new HashSet<T>(_set);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConcurrentHashSet<T>));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _lock?.Dispose();
    }
}
