using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PulseRPC.Server.Monitoring;

/// <summary>
/// PulseRPC 性能指标收集器
/// </summary>
public class PulseMetrics
{
    private static readonly Meter _meter = new("PulseRPC.Server", "1.0.0");

    private static readonly Counter<long> _totalConnections = _meter.CreateCounter<long>(
        "pulserpc.server.connections.total",
        "连接数");

    private static readonly Counter<long> _totalRequests = _meter.CreateCounter<long>(
        "pulserpc.server.requests.total",
        "请求数");

    private static readonly Counter<long> _failedRequests = _meter.CreateCounter<long>(
        "pulserpc.server.requests.failed",
        "失败请求数");

    private static readonly Histogram<double> _requestDuration = _meter.CreateHistogram<double>(
        "pulserpc.server.request.duration",
        "ms",
        "请求处理时间");

    private static readonly ConcurrentDictionary<string, Counter<long>> _methodCallCounters = new();

    private static readonly ConcurrentDictionary<string, Stopwatch> _activeRequests = new();

    /// <summary>
    /// 记录新连接
    /// </summary>
    public static void RecordConnection()
    {
        _totalConnections.Add(1);
    }

    /// <summary>
    /// 开始记录请求
    /// </summary>
    /// <param name="requestId">请求ID</param>
    /// <returns>请求ID</returns>
    public static Guid StartRequest(Guid requestId)
    {
        _totalRequests.Add(1);
        _activeRequests.TryAdd(requestId.ToString(), Stopwatch.StartNew());
        return requestId;
    }

    /// <summary>
    /// 结束记录请求
    /// </summary>
    /// <param name="requestId">请求ID</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="success">是否成功</param>
    public static void EndRequest(Guid requestId, string serviceName, string methodName, bool success)
    {
        if (_activeRequests.TryRemove(requestId.ToString(), out var stopwatch))
        {
            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed.TotalMilliseconds;
            _requestDuration.Record(elapsed);

            // 记录方法调用次数
            var methodKey = $"{serviceName}.{methodName}";
            var counter = _methodCallCounters.GetOrAdd(methodKey, key =>
                _meter.CreateCounter<long>($"pulserpc.server.method.calls.{key.Replace(".", "_")}"));

            counter.Add(1);

            if (!success)
            {
                _failedRequests.Add(1);
            }
        }
    }

    /// <summary>
    /// 获取当前活跃请求数
    /// </summary>
    public static int ActiveRequestCount => _activeRequests.Count;

    /// <summary>
    /// 获取性能指标
    /// </summary>
    /// <returns>性能指标快照</returns>
    public static MetricsSnapshot GetSnapshot()
    {
        return new MetricsSnapshot
        {
            ActiveRequests = _activeRequests.Count,
            TotalConnections = GetCounterValue(_totalConnections),
            TotalRequests = GetCounterValue(_totalRequests),
            FailedRequests = GetCounterValue(_failedRequests)
        };
    }

    /// <summary>
    /// 获取计数器当前值
    /// </summary>
    private static long GetCounterValue<T>(Counter<T> counter) where T : struct
    {
        // System.Diagnostics.Metrics.Counter 无法直接获取当前值
        // 这是设计使然，通常值会被输出到监控系统
        // 返回一个默认值，实际项目中需使用 MeterListener 或 OpenTelemetry 收集
        try
        {
            // 反射尝试获取值，仅用于调试目的
            var field = counter.GetType().GetField("_value",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                var value = field.GetValue(counter);
                if (value != null && value is T typedValue)
                {
                    return Convert.ToInt64(typedValue);
                }
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// 性能指标快照
/// </summary>
public class MetricsSnapshot
{
    /// <summary>
    /// 活跃请求数
    /// </summary>
    public int ActiveRequests { get; set; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public long TotalConnections { get; set; }

    /// <summary>
    /// 总请求数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }
}
