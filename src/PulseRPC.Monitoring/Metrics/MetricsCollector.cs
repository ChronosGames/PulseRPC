using System.Collections.Concurrent;
using System.Diagnostics;

namespace PulseRPC.Monitoring.Metrics;

/// <summary>
/// 指标收集器 - 收集性能和业务指标
/// </summary>
public class MetricsCollector : IMetricsCollector
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private readonly ConcurrentDictionary<string, Gauge> _gauges = new();
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new();
    private readonly ConcurrentDictionary<string, Timer> _timers = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// 获取或创建计数器
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="tags">标签</param>
    /// <returns>计数器实例</returns>
    public ICounter GetCounter(string name, string? description = null, IDictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        return _counters.GetOrAdd(key, _ => new Counter(name, description, tags ?? new Dictionary<string, string>()));
    }

    /// <summary>
    /// 获取或创建仪表
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="tags">标签</param>
    /// <returns>仪表实例</returns>
    public IGauge GetGauge(string name, string? description = null, IDictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        return _gauges.GetOrAdd(key, _ => new Gauge(name, description, tags ?? new Dictionary<string, string>()));
    }

    /// <summary>
    /// 获取或创建直方图
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="buckets">分桶配置</param>
    /// <param name="tags">标签</param>
    /// <returns>直方图实例</returns>
    public IHistogram GetHistogram(string name, string? description = null, double[]? buckets = null, IDictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        return _histograms.GetOrAdd(key, _ => new Histogram(name, description, buckets, tags ?? new Dictionary<string, string>()));
    }

    /// <summary>
    /// 获取或创建计时器
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="tags">标签</param>
    /// <returns>计时器实例</returns>
    public ITimer GetTimer(string name, string? description = null, IDictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        return _timers.GetOrAdd(key, _ => new Timer(name, description, tags ?? new Dictionary<string, string>()));
    }

    /// <summary>
    /// 记录RPC调用指标
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="status">调用状态</param>
    /// <param name="duration">调用耗时</param>
    /// <param name="requestSize">请求大小</param>
    /// <param name="responseSize">响应大小</param>
    public void RecordRpcCall(string serviceName, string methodName, string status, TimeSpan duration, long requestSize = 0, long responseSize = 0)
    {
        var tags = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["method"] = methodName,
            ["status"] = status
        };

        // RPC调用次数
        GetCounter("rpc_calls_total", "Total number of RPC calls", tags).Increment();

        // RPC调用耗时
        GetHistogram("rpc_call_duration_seconds", "RPC call duration in seconds",
                [0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0], tags)
            .Observe(duration.TotalSeconds);

        // 请求和响应大小
        if (requestSize > 0)
        {
            GetHistogram("rpc_request_size_bytes", "RPC request size in bytes",
                    [64, 256, 1024, 4096, 16384, 65536, 262144, 1048576], tags)
                .Observe(requestSize);
        }

        if (responseSize > 0)
        {
            GetHistogram("rpc_response_size_bytes", "RPC response size in bytes",
                    [64, 256, 1024, 4096, 16384, 65536, 262144, 1048576], tags)
                .Observe(responseSize);
        }

        // 错误率统计
        if (status != "success")
        {
            GetCounter("rpc_errors_total", "Total number of RPC errors", tags).Increment();
        }
    }

    /// <summary>
    /// 记录负载均衡指标
    /// </summary>
    /// <param name="strategy">负载均衡策略</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="endpointCount">可用端点数</param>
    /// <param name="selectionTime">选择耗时</param>
    public void RecordLoadBalancing(string strategy, string serviceName, int endpointCount, TimeSpan selectionTime)
    {
        var tags = new Dictionary<string, string>
        {
            ["strategy"] = strategy,
            ["service"] = serviceName
        };

        // 负载均衡选择次数
        GetCounter("lb_selections_total", "Total number of load balancing selections", tags).Increment();

        // 负载均衡选择耗时
        GetHistogram("lb_selection_duration_seconds", "Load balancing selection duration in seconds",
                new[] { 0.0001, 0.0005, 0.001, 0.005, 0.01, 0.025, 0.05, 0.1 }, tags)
            .Observe(selectionTime.TotalSeconds);

        // 可用端点数量
        GetGauge("lb_available_endpoints", "Number of available endpoints for load balancing", tags)
            .Set(endpointCount);
    }

    /// <summary>
    /// 记录服务发现指标
    /// </summary>
    /// <param name="discoveryType">发现类型</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="endpointCount">发现的端点数</param>
    /// <param name="discoveryTime">发现耗时</param>
    /// <param name="cacheHit">是否命中缓存</param>
    public void RecordServiceDiscovery(string discoveryType, string serviceName, int endpointCount, TimeSpan discoveryTime, bool cacheHit = false)
    {
        var tags = new Dictionary<string, string>
        {
            ["type"] = discoveryType,
            ["service"] = serviceName,
            ["cache_hit"] = cacheHit.ToString().ToLowerInvariant()
        };

        // 服务发现次数
        GetCounter("sd_discoveries_total", "Total number of service discoveries", tags).Increment();

        // 服务发现耗时
        GetHistogram("sd_discovery_duration_seconds", "Service discovery duration in seconds",
                new[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0 }, tags)
            .Observe(discoveryTime.TotalSeconds);

        // 发现的端点数量
        GetGauge("sd_discovered_endpoints", "Number of discovered service endpoints", tags)
            .Set(endpointCount);

        // 缓存命中统计
        if (cacheHit)
        {
            GetCounter("sd_cache_hits_total", "Total number of service discovery cache hits", tags).Increment();
        }
        else
        {
            GetCounter("sd_cache_misses_total", "Total number of service discovery cache misses", tags).Increment();
        }
    }

    /// <summary>
    /// 记录健康检查指标
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="endpointId">端点ID</param>
    /// <param name="status">健康状态</param>
    /// <param name="checkTime">检查耗时</param>
    public void RecordHealthCheck(string serviceName, string endpointId, string status, TimeSpan checkTime)
    {
        var tags = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["endpoint"] = endpointId,
            ["status"] = status
        };

        // 健康检查次数
        GetCounter("hc_checks_total", "Total number of health checks", tags).Increment();

        // 健康检查耗时
        GetHistogram("hc_check_duration_seconds", "Health check duration in seconds",
                new[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0 }, tags)
            .Observe(checkTime.TotalSeconds);

        // 健康状态统计
        var statusTags = new Dictionary<string, string>
        {
            ["service"] = serviceName,
            ["status"] = status
        };
        GetCounter("hc_status_total", "Total number of health check results by status", statusTags).Increment();
    }

    /// <summary>
    /// 记录连接池指标
    /// </summary>
    /// <param name="poolName">连接池名称</param>
    /// <param name="activeConnections">活跃连接数</param>
    /// <param name="idleConnections">空闲连接数</param>
    /// <param name="totalConnections">总连接数</param>
    /// <param name="waitTime">等待连接时间</param>
    public void RecordConnectionPool(string poolName, int activeConnections, int idleConnections, int totalConnections, TimeSpan? waitTime = null)
    {
        var tags = new Dictionary<string, string> { ["pool"] = poolName };

        // 连接池状态
        GetGauge("cp_active_connections", "Number of active connections in pool", tags).Set(activeConnections);
        GetGauge("cp_idle_connections", "Number of idle connections in pool", tags).Set(idleConnections);
        GetGauge("cp_total_connections", "Total number of connections in pool", tags).Set(totalConnections);

        // 等待时间
        if (waitTime.HasValue)
        {
            GetHistogram("cp_wait_duration_seconds", "Connection pool wait duration in seconds",
                    new[] { 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0 }, tags)
                .Observe(waitTime.Value.TotalSeconds);
        }
    }

    /// <summary>
    /// 获取所有指标快照
    /// </summary>
    /// <returns>指标快照</returns>
    public MetricsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new MetricsSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Counters = _counters.Values.Select(c => c.GetSnapshot()).ToList(),
                Gauges = _gauges.Values.Select(g => g.GetSnapshot()).ToList(),
                Histograms = _histograms.Values.Select(h => h.GetSnapshot()).ToList(),
                Timers = _timers.Values.Select(t => t.GetSnapshot()).ToList()
            };
        }
    }

    /// <summary>
    /// 重置所有指标
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var counter in _counters.Values)
                counter.Reset();
            foreach (var gauge in _gauges.Values)
                gauge.Reset();
            foreach (var histogram in _histograms.Values)
                histogram.Reset();
            foreach (var timer in _timers.Values)
                timer.Reset();
        }
    }

    /// <summary>
    /// 构建指标键
    /// </summary>
    private static string BuildKey(string name, IDictionary<string, string>? tags)
    {
        if (tags == null || tags.Count == 0)
            return name;

        var sortedTags = tags.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key}={kvp.Value}");
        return $"{name}[{string.Join(",", sortedTags)}]";
    }
}

/// <summary>
/// 指标快照
/// </summary>
public class MetricsSnapshot
{
    /// <summary>
    /// 快照时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 计数器快照
    /// </summary>
    public List<CounterSnapshot> Counters { get; set; } = new();

    /// <summary>
    /// 仪表快照
    /// </summary>
    public List<GaugeSnapshot> Gauges { get; set; } = new();

    /// <summary>
    /// 直方图快照
    /// </summary>
    public List<HistogramSnapshot> Histograms { get; set; } = new();

    /// <summary>
    /// 计时器快照
    /// </summary>
    public List<TimerSnapshot> Timers { get; set; } = new();
}
