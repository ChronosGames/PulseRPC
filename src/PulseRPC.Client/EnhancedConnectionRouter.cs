using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace PulseRPC.Client;

/// <summary>
/// 生产级连接路由器实现
/// 特性：缓存、指标、熔断、多策略路由
/// </summary>
internal sealed class EnhancedConnectionRouter : IConnectionRouter, IDisposable
{
    private readonly ILogger<EnhancedConnectionRouter> _logger;
    private readonly IConnectionRegistry _connectionRegistry;
    private readonly ILoadBalancer _loadBalancer;

    // 路由规则存储
    private readonly ConcurrentDictionary<string, RoutingRule> _rules = new();
    private readonly ReaderWriterLockSlim _rulesLock = new();
    private ImmutableArray<RoutingRule> _cachedSortedRules = ImmutableArray<RoutingRule>.Empty;

    // 性能缓存
    private readonly ConcurrentDictionary<string, CachedRouteResult> _routeCache = new();
    private readonly Timer _cacheCleanupTimer;

    // 指标收集
    private readonly RouterMetrics _metrics = new();

    // 熔断器
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers = new();

    // 配置
    private readonly EnhancedRouterOptions _options;

    // 释放标志
    private volatile bool _disposed;

    public EnhancedConnectionRouter(
        IConnectionRegistry connectionRegistry,
        ILoadBalancer loadBalancer,
        EnhancedRouterOptions? options = null,
        ILogger<EnhancedConnectionRouter>? logger = null)
    {
        _connectionRegistry = connectionRegistry ?? throw new ArgumentNullException(nameof(connectionRegistry));
        _loadBalancer = loadBalancer ?? throw new ArgumentNullException(nameof(loadBalancer));
        _options = options ?? new EnhancedRouterOptions();
        _logger = logger ?? NullLogger<EnhancedConnectionRouter>.Instance;

        // 启动缓存清理定时器
        _cacheCleanupTimer = new Timer(CleanupCache, null,
            _options.CacheCleanupInterval, _options.CacheCleanupInterval);

        AddDefaultRules();

        _logger.LogInformation("增强连接路由器已初始化，缓存TTL: {CacheTtl}ms",
            _options.CacheTtl.TotalMilliseconds);
    }

    /// <summary>
    /// 路由指标
    /// </summary>
    public RouterMetrics Metrics => _metrics;

    /// <summary>
    /// 注册路由规则
    /// </summary>
    public void RegisterRule(RoutingRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (string.IsNullOrEmpty(rule.Id))
            throw new ArgumentException("路由规则ID不能为空", nameof(rule));

        _rules.AddOrUpdate(rule.Id, rule, (_, _) => rule);
        InvalidateRuleCache();

        _logger.LogInformation("注册路由规则: {RuleId} ({RuleName}), 优先级: {Priority}",
            rule.Id, rule.Name, rule.Priority);
    }

    /// <summary>
    /// 移除路由规则
    /// </summary>
    public bool RemoveRule(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId))
            return false;

        var removed = _rules.TryRemove(ruleId, out var rule);
        if (removed)
        {
            InvalidateRuleCache();
            _logger.LogInformation("移除路由规则: {RuleId}", ruleId);
        }

        return removed;
    }

    /// <summary>
    /// 路由到最佳连接
    /// </summary>
    public async Task<IConnection> RouteAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EnhancedConnectionRouter));

        if (string.IsNullOrEmpty(routingKey))
            throw new ArgumentException("路由键不能为空", nameof(routingKey));

        var stopwatch = Stopwatch.StartNew();
        _metrics.TotalRequests++;

        try
        {
            // 尝试从缓存获取
            var cacheKey = GenerateCacheKey(routingKey, context);
            if (_options.EnableCache && TryGetFromCache(cacheKey, out var cachedConnection))
            {
                _metrics.CacheHits++;
                _logger.LogDebug("路由缓存命中: {RoutingKey} -> {ConnectionId}", routingKey, cachedConnection.Id);
                return cachedConnection;
            }

            _metrics.CacheMisses++;

            // 获取候选连接
            var candidateConnections = await GetCandidateConnectionsAsync(routingKey, context, cancellationToken);
            if (candidateConnections.Count == 0)
            {
                _metrics.FailedRoutes++;
                throw new InvalidOperationException($"未找到可用连接: {routingKey}");
            }

            // 应用路由规则
            var selectedConnection = await ApplyRoutingRulesAsync(routingKey, candidateConnections, context, cancellationToken);
            if (selectedConnection == null)
            {
                // 后备策略：使用负载均衡器
                selectedConnection = _loadBalancer.SelectConnection(candidateConnections,
                    context?.LoadBalancingHint ?? LoadBalancingHint.None);
            }

            if (selectedConnection == null)
            {
                _metrics.FailedRoutes++;
                throw new InvalidOperationException($"路由失败，无法选择连接: {routingKey}");
            }

            // 检查熔断器
            var circuitBreaker = GetCircuitBreaker(selectedConnection.Id);
            if (circuitBreaker.State == CircuitBreakerState.Open)
            {
                _logger.LogWarning("连接 {ConnectionId} 熔断器开启，尝试下一个连接", selectedConnection.Id);

                // 尝试选择其他连接
                var alternativeConnections = candidateConnections.Where(c => c.Id != selectedConnection.Id).ToList();
                if (alternativeConnections.Any())
                {
                    selectedConnection = _loadBalancer.SelectConnection(alternativeConnections);
                }

                if (selectedConnection == null || GetCircuitBreaker(selectedConnection.Id).State == CircuitBreakerState.Open)
                {
                    _metrics.FailedRoutes++;
                    throw new InvalidOperationException($"所有候选连接均不可用: {routingKey}");
                }
            }

            // 缓存结果
            if (_options.EnableCache)
            {
                CacheRouteResult(cacheKey, selectedConnection);
            }

            _metrics.SuccessfulRoutes++;
            _logger.LogDebug("路由成功: {RoutingKey} -> {ConnectionId} (耗时: {ElapsedMs}ms)",
                routingKey, selectedConnection.Id, stopwatch.ElapsedMilliseconds);

            return selectedConnection;
        }
        catch (Exception ex)
        {
            _metrics.FailedRoutes++;
            _logger.LogError(ex, "路由失败: {RoutingKey}", routingKey);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.UpdateLatency(stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// 获取所有匹配的连接
    /// </summary>
    public IReadOnlyList<IConnection> GetMatchingConnections(string routingKey, RoutingContext? context = null)
    {
        if (string.IsNullOrEmpty(routingKey))
            return Array.Empty<IConnection>();

        return GetCandidateConnectionsAsync(routingKey, context, CancellationToken.None).Result;
    }

    /// <summary>
    /// 获取候选连接
    /// </summary>
    private async Task<IReadOnlyList<IConnection>> GetCandidateConnectionsAsync(
        string routingKey,
        RoutingContext? context,
        CancellationToken cancellationToken)
    {
        // 1. 精确ID匹配
        var connectionById = _connectionRegistry.GetConnection(routingKey);
        if (connectionById != null && IsConnectionHealthy(connectionById))
        {
            return new[] { connectionById };
        }

        // 2. 解析路由键获取通道名称
        var channelName = ExtractChannelNameFromRoutingKey(routingKey, context);

        // 3. 按通道名称过滤（name-id 路由策略的核心）
        var allConnections = _connectionRegistry.GetAllConnections();
        var filteredConnections = allConnections.AsEnumerable();

        if (!string.IsNullOrEmpty(channelName))
        {
            filteredConnections = filteredConnections.Where(c =>
                c.Descriptor.Name == channelName);
        }

        // 4. 按标签过滤
        if (context?.Tags?.Any() == true)
        {
            filteredConnections = filteredConnections.Where(c =>
                context.Tags.All(tag => c.Descriptor.Tags.GetValueOrDefault(tag.Key) == tag.Value));
        }

        // 5. 按区域过滤
        if (!string.IsNullOrEmpty(context?.PreferredRegion))
        {
            var regionalConnections = filteredConnections.Where(c =>
                c.Descriptor.Tags.GetValueOrDefault("region") == context.PreferredRegion).ToList();

            if (regionalConnections.Any())
            {
                filteredConnections = regionalConnections;
            }
        }

        // 6. 健康状态过滤
        var healthyConnections = filteredConnections
            .Where(IsConnectionHealthy)
            .ToList();

        _logger.LogDebug("候选连接过滤结果: 路由键={RoutingKey}, 通道={ChannelName}, 候选数={Count}",
            routingKey, channelName, healthyConnections.Count);

        return healthyConnections;
    }

    /// <summary>
    /// 从路由键和上下文中提取通道名称
    /// </summary>
    private string ExtractChannelNameFromRoutingKey(string routingKey, RoutingContext? context)
    {
        // 1. 优先从上下文获取通道名称
        if (context?.Properties?.TryGetValue("ChannelName", out var channelNameObj) == true &&
            channelNameObj is string channelName && !string.IsNullOrEmpty(channelName))
        {
            return channelName;
        }

        // 2. 从路由键解析（格式：channelName@serviceType）
        if (routingKey.Contains('@'))
        {
            var parts = routingKey.Split('@', 2);
            if (parts.Length == 2)
            {
                return parts[0];
            }
        }

        // 3. 默认通道
        return "default";
    }

    /// <summary>
    /// 应用路由规则
    /// </summary>
    private async Task<IConnection?> ApplyRoutingRulesAsync(
        string routingKey,
        IReadOnlyList<IConnection> connections,
        RoutingContext? context,
        CancellationToken cancellationToken)
    {
        var sortedRules = GetSortedRules();

        foreach (var rule in sortedRules)
        {
            if (!rule.Enabled)
                continue;

            try
            {
                var isMatch = await Task.Run(() => rule.Matcher(routingKey, context), cancellationToken);
                if (isMatch)
                {
                    var selectedConnection = await Task.Run(() => rule.Selector(connections, context), cancellationToken);
                    if (selectedConnection != null && IsConnectionHealthy(selectedConnection))
                    {
                        _logger.LogDebug("应用路由规则 {RuleId}: {RoutingKey} -> {ConnectionId}",
                            rule.Id, routingKey, selectedConnection.Id);
                        return selectedConnection;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "路由规则 {RuleId} 执行失败", rule.Id);
                _metrics.RuleErrors++;
            }
        }

        return null;
    }

    /// <summary>
    /// 检查连接是否健康
    /// </summary>
    private bool IsConnectionHealthy(IConnection connection)
    {
        if (connection == null)
            return false;

        // 检查基本状态
        if (connection.State != ExtendedConnectionState.Connected &&
            connection.State != ExtendedConnectionState.Active)
        {
            return false;
        }

        // 检查熔断器状态
        var circuitBreaker = GetCircuitBreaker(connection.Id);
        return circuitBreaker.State != CircuitBreakerState.Open;
    }

    /// <summary>
    /// 获取熔断器
    /// </summary>
    private CircuitBreaker GetCircuitBreaker(string connectionId)
    {
        return _circuitBreakers.GetOrAdd(connectionId, _ => new CircuitBreaker(_options.CircuitBreakerOptions));
    }

    /// <summary>
    /// 获取排序后的规则
    /// </summary>
    private ImmutableArray<RoutingRule> GetSortedRules()
    {
        var cached = _cachedSortedRules;
        if (!cached.IsEmpty)
            return cached;

        _rulesLock.EnterReadLock();
        try
        {
            cached = _cachedSortedRules;
            if (!cached.IsEmpty)
                return cached;
        }
        finally
        {
            _rulesLock.ExitReadLock();
        }

        _rulesLock.EnterWriteLock();
        try
        {
            if (_cachedSortedRules.IsEmpty)
            {
                _cachedSortedRules = _rules.Values
                    .OrderByDescending(r => r.Priority)
                    .ThenBy(r => r.Id)
                    .ToImmutableArray();
            }
            return _cachedSortedRules;
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 失效规则缓存
    /// </summary>
    private void InvalidateRuleCache()
    {
        _rulesLock.EnterWriteLock();
        try
        {
            _cachedSortedRules = ImmutableArray<RoutingRule>.Empty;
        }
        finally
        {
            _rulesLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 生成缓存键
    /// </summary>
    private string GenerateCacheKey(string routingKey, RoutingContext? context)
    {
        if (context == null)
            return routingKey;

        var keyBuilder = new System.Text.StringBuilder(routingKey);

        if (context.Tags?.Any() == true)
        {
            keyBuilder.Append("|tags:");
            foreach (var tag in context.Tags.OrderBy(t => t.Key))
            {
                keyBuilder.Append($"{tag.Key}={tag.Value};");
            }
        }

        if (!string.IsNullOrEmpty(context.PreferredRegion))
        {
            keyBuilder.Append($"|region:{context.PreferredRegion}");
        }

        if (!string.IsNullOrEmpty(context.UserId))
        {
            keyBuilder.Append($"|user:{context.UserId}");
        }

        return keyBuilder.ToString();
    }

    /// <summary>
    /// 尝试从缓存获取
    /// </summary>
    private bool TryGetFromCache(string cacheKey, out IConnection connection)
    {
        connection = null!;

        if (!_routeCache.TryGetValue(cacheKey, out var cachedResult))
            return false;

        if (DateTime.UtcNow - cachedResult.CachedAt > _options.CacheTtl)
        {
            _routeCache.TryRemove(cacheKey, out _);
            return false;
        }

        // 验证缓存的连接仍然有效
        if (!IsConnectionHealthy(cachedResult.Connection))
        {
            _routeCache.TryRemove(cacheKey, out _);
            return false;
        }

        connection = cachedResult.Connection;
        return true;
    }

    /// <summary>
    /// 缓存路由结果
    /// </summary>
    private void CacheRouteResult(string cacheKey, IConnection connection)
    {
        if (_routeCache.Count >= _options.MaxCacheSize)
        {
            // 简单的LRU清理策略
            var oldestKey = _routeCache
                .OrderBy(kv => kv.Value.CachedAt)
                .Take(_options.MaxCacheSize / 4)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (oldestKey != null)
            {
                _routeCache.TryRemove(oldestKey, out _);
            }
        }

        _routeCache[cacheKey] = new CachedRouteResult(connection, DateTime.UtcNow);
    }

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    private void CleanupCache(object? state)
    {
        if (_disposed)
            return;

        try
        {
            var cutoffTime = DateTime.UtcNow - _options.CacheTtl;
            var keysToRemove = _routeCache
                .Where(kv => kv.Value.CachedAt < cutoffTime)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _routeCache.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogDebug("清理过期路由缓存: {Count} 项", keysToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理路由缓存时发生错误");
        }
    }

    /// <summary>
    /// 添加默认路由规则
    /// </summary>
    private void AddDefaultRules()
    {
        // 健康连接优先规则
        RegisterRule(new RoutingRule
        {
            Id = "health-priority",
            Name = "健康连接优先",
            Priority = 1000,
            Matcher = (_, _) => true,
            Selector = (connections, context) =>
            {
                // 优先选择活跃连接
                var activeConnection = connections.FirstOrDefault(c => c.State == ExtendedConnectionState.Active);
                if (activeConnection != null)
                    return activeConnection;

                // 其次选择已连接的连接
                return connections.FirstOrDefault(c => c.State == ExtendedConnectionState.Connected);
            }
        });

        // 区域亲和性规则
        RegisterRule(new RoutingRule
        {
            Id = "region-affinity",
            Name = "区域亲和性",
            Priority = 800,
            Matcher = (key, context) => !string.IsNullOrEmpty(context?.PreferredRegion),
            Selector = (connections, context) =>
            {
                return connections.FirstOrDefault(c =>
                    c.Descriptor.Tags.GetValueOrDefault("region") == context?.PreferredRegion);
            }
        });

        // 负载均衡规则
        RegisterRule(new RoutingRule
        {
            Id = "load-balancing",
            Name = "负载均衡",
            Priority = 100,
            Matcher = (_, _) => true,
            Selector = (connections, context) =>
            {
                var hint = context?.LoadBalancingHint ?? LoadBalancingHint.None;
                return _loadBalancer.SelectConnection(connections, hint);
            }
        });
    }

    /// <summary>
    /// 报告连接成功
    /// </summary>
    public void ReportSuccess(string connectionId)
    {
        var circuitBreaker = GetCircuitBreaker(connectionId);
        circuitBreaker.RecordSuccess();
    }

    /// <summary>
    /// 报告连接失败
    /// </summary>
    public void ReportFailure(string connectionId, Exception exception)
    {
        var circuitBreaker = GetCircuitBreaker(connectionId);
        circuitBreaker.RecordFailure();

        _logger.LogWarning(exception, "连接 {ConnectionId} 报告失败", connectionId);
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cacheCleanupTimer?.Dispose();
        _rulesLock?.Dispose();

        _logger.LogInformation("增强连接路由器已释放");
    }
}

/// <summary>
/// 缓存的路由结果
/// </summary>
internal readonly record struct CachedRouteResult(IConnection Connection, DateTime CachedAt)
{
    public IConnection Connection { get; } = Connection;
    public DateTime CachedAt { get; } = CachedAt;
}

/// <summary>
/// 路由器指标
/// </summary>
public class RouterMetrics
{
    private long _totalRequests;
    private long _successfulRoutes;
    private long _failedRoutes;
    private long _cacheHits;
    private long _cacheMisses;
    private long _ruleErrors;
    private readonly object _latencyLock = new();
    private TimeSpan _totalLatency;
    private long _latencyCount;

    public long TotalRequests
    {
        get => Interlocked.Read(ref _totalRequests);
        set => Interlocked.Exchange(ref _totalRequests, value);
    }

    public long SuccessfulRoutes
    {
        get => Interlocked.Read(ref _successfulRoutes);
        set => Interlocked.Exchange(ref _successfulRoutes, value);
    }

    public long FailedRoutes
    {
        get => Interlocked.Read(ref _failedRoutes);
        set => Interlocked.Exchange(ref _failedRoutes, value);
    }

    public long CacheHits
    {
        get => Interlocked.Read(ref _cacheHits);
        set => Interlocked.Exchange(ref _cacheHits, value);
    }

    public long CacheMisses
    {
        get => Interlocked.Read(ref _cacheMisses);
        set => Interlocked.Exchange(ref _cacheMisses, value);
    }

    public long RuleErrors
    {
        get => Interlocked.Read(ref _ruleErrors);
        set => Interlocked.Exchange(ref _ruleErrors, value);
    }

    public double SuccessRate => TotalRequests == 0 ? 0 : (double)SuccessfulRoutes / TotalRequests;
    public double CacheHitRate => (CacheHits + CacheMisses) == 0 ? 0 : (double)CacheHits / (CacheHits + CacheMisses);

    public TimeSpan AverageLatency
    {
        get
        {
            lock (_latencyLock)
            {
                return _latencyCount == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(_totalLatency.Ticks / _latencyCount);
            }
        }
    }

    public void UpdateLatency(TimeSpan latency)
    {
        lock (_latencyLock)
        {
            _totalLatency = _totalLatency.Add(latency);
            _latencyCount++;
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _successfulRoutes, 0);
        Interlocked.Exchange(ref _failedRoutes, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _ruleErrors, 0);

        lock (_latencyLock)
        {
            _totalLatency = TimeSpan.Zero;
            _latencyCount = 0;
        }
    }
}

/// <summary>
/// 增强路由器配置选项
/// </summary>
public class EnhancedRouterOptions
{
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// 缓存TTL
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大缓存大小
    /// </summary>
    public int MaxCacheSize { get; set; } = 10000;

    /// <summary>
    /// 缓存清理间隔
    /// </summary>
    public TimeSpan CacheCleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 熔断器选项
    /// </summary>
    public CircuitBreakerOptions CircuitBreakerOptions { get; set; } = new();
}

/// <summary>
/// 熔断器状态
/// </summary>
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// 熔断器选项
/// </summary>
public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    public int SuccessThreshold { get; set; } = 2;
}

/// <summary>
/// 简单熔断器实现
/// </summary>
public class CircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private volatile CircuitBreakerState _state = CircuitBreakerState.Closed;
    private volatile int _failureCount;
    private volatile int _successCount;
    private DateTime _lastFailureTime;

    public CircuitBreaker(CircuitBreakerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CircuitBreakerState State => _state;

    public void RecordSuccess()
    {
        if (_state == CircuitBreakerState.HalfOpen)
        {
            var count = Interlocked.Increment(ref _successCount);
            if (count >= _options.SuccessThreshold)
            {
                Reset();
            }
        }
        else if (_state == CircuitBreakerState.Closed)
        {
            Interlocked.Exchange(ref _failureCount, 0);
        }
    }

    public void RecordFailure()
    {
        _lastFailureTime = DateTime.UtcNow;

        if (_state == CircuitBreakerState.HalfOpen)
        {
            Trip();
        }
        else if (_state == CircuitBreakerState.Closed)
        {
            var count = Interlocked.Increment(ref _failureCount);
            if (count >= _options.FailureThreshold)
            {
                Trip();
            }
        }
    }

    public bool CanExecute()
    {
        if (_state == CircuitBreakerState.Closed)
            return true;

        if (_state == CircuitBreakerState.Open)
        {
            if (DateTime.UtcNow - _lastFailureTime >= _options.Timeout)
            {
                _state = CircuitBreakerState.HalfOpen;
                Interlocked.Exchange(ref _successCount, 0);
                return true;
            }
            return false;
        }

        // HalfOpen
        return true;
    }

    private void Trip()
    {
        _state = CircuitBreakerState.Open;
        _lastFailureTime = DateTime.UtcNow;
    }

    private void Reset()
    {
        _state = CircuitBreakerState.Closed;
        Interlocked.Exchange(ref _failureCount, 0);
        Interlocked.Exchange(ref _successCount, 0);
    }
}
