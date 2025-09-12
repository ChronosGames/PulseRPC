using TransportOptions = PulseRPC.Transport.TransportOptions;

namespace PulseRPC.Benchmark.Core.Models;

/// <summary>
/// 基准测试结果
/// </summary>
public class BenchmarkResult
{
    /// <summary>
    /// 场景名称
    /// </summary>
    public string ScenarioName { get; set; } = string.Empty;

    /// <summary>
    /// 测试开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 测试结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 测试总时长
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 测试配置
    /// </summary>
    public BenchmarkConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// 延迟指标
    /// </summary>
    public LatencyMetrics Latency { get; set; } = new();

    /// <summary>
    /// 吞吐量指标
    /// </summary>
    public ThroughputMetrics Throughput { get; set; } = new();

    /// <summary>
    /// 资源使用指标
    /// </summary>
    public ResourceMetrics Resources { get; set; } = new();

    /// <summary>
    /// 自定义指标
    /// </summary>
    public Dictionary<string, object> CustomMetrics { get; set; } = new();

    /// <summary>
    /// 测试详细日志
    /// </summary>
    public List<string> Logs { get; set; } = new();

    /// <summary>
    /// 获取自定义指标
    /// </summary>
    /// <typeparam name="T">指标类型</typeparam>
    /// <param name="key">指标键</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>指标值</returns>
    public T GetCustomMetric<T>(string key, T defaultValue = default!)
    {
        if (CustomMetrics.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// 设置自定义指标
    /// </summary>
    /// <param name="key">指标键</param>
    /// <param name="value">指标值</param>
    public void SetCustomMetric(string key, object value)
    {
        CustomMetrics[key] = value;
    }

    /// <summary>
    /// 添加日志条目
    /// </summary>
    /// <param name="message">日志消息</param>
    public void AddLog(string message)
    {
        Logs.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
    }
}

/// <summary>
/// 延迟指标
/// </summary>
public class LatencyMetrics
{
    /// <summary>
    /// 平均延迟（毫秒）
    /// </summary>
    public double AverageMs { get; set; }

    /// <summary>
    /// 最小延迟（毫秒）
    /// </summary>
    public double MinMs { get; set; }

    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public double MaxMs { get; set; }

    /// <summary>
    /// 中位数延迟（毫秒）
    /// </summary>
    public double MedianMs { get; set; }

    /// <summary>
    /// P50延迟（毫秒）
    /// </summary>
    public double P50Ms { get; set; }

    /// <summary>
    /// P90延迟（毫秒）
    /// </summary>
    public double P90Ms { get; set; }

    /// <summary>
    /// P95延迟（毫秒）
    /// </summary>
    public double P95Ms { get; set; }

    /// <summary>
    /// P99延迟（毫秒）
    /// </summary>
    public double P99Ms { get; set; }

    /// <summary>
    /// P99.9延迟（毫秒）
    /// </summary>
    public double P999Ms { get; set; }

    /// <summary>
    /// 标准差（毫秒）
    /// </summary>
    public double StandardDeviationMs { get; set; }

    /// <summary>
    /// 延迟样本数量
    /// </summary>
    public int SampleCount { get; set; }
}

/// <summary>
/// 吞吐量指标
/// </summary>
public class ThroughputMetrics
{
    /// <summary>
    /// 总操作数
    /// </summary>
    public long TotalOperations { get; set; }

    /// <summary>
    /// 成功操作数
    /// </summary>
    public long SuccessfulOperations { get; set; }

    /// <summary>
    /// 失败操作数
    /// </summary>
    public long FailedOperations { get; set; }

    /// <summary>
    /// 平均每秒操作数
    /// </summary>
    public double OperationsPerSecond { get; set; }

    /// <summary>
    /// 峰值每秒操作数
    /// </summary>
    public double PeakOperationsPerSecond { get; set; }

    /// <summary>
    /// 总数据传输量（字节）
    /// </summary>
    public long TotalBytesTransferred { get; set; }

    /// <summary>
    /// 平均带宽（字节/秒）
    /// </summary>
    public double AverageBandwidthBps { get; set; }

    /// <summary>
    /// 峰值带宽（字节/秒）
    /// </summary>
    public double PeakBandwidthBps { get; set; }

    /// <summary>
    /// 成功率百分比
    /// </summary>
    public double SuccessRatePercentage => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;
}

/// <summary>
/// 资源使用指标
/// </summary>
public class ResourceMetrics
{
    /// <summary>
    /// 平均CPU使用率百分比
    /// </summary>
    public double AverageCpuUsagePercentage { get; set; }

    /// <summary>
    /// 峰值CPU使用率百分比
    /// </summary>
    public double PeakCpuUsagePercentage { get; set; }

    /// <summary>
    /// 平均内存使用量（字节）
    /// </summary>
    public long AverageMemoryUsageBytes { get; set; }

    /// <summary>
    /// 峰值内存使用量（字节）
    /// </summary>
    public long PeakMemoryUsageBytes { get; set; }

    /// <summary>
    /// 垃圾回收次数
    /// </summary>
    public int GarbageCollectionCount { get; set; }

    /// <summary>
    /// 垃圾回收总时间（毫秒）
    /// </summary>
    public long GarbageCollectionTimeMs { get; set; }

    /// <summary>
    /// 网络发送字节数
    /// </summary>
    public long NetworkBytesSent { get; set; }

    /// <summary>
    /// 网络接收字节数
    /// </summary>
    public long NetworkBytesReceived { get; set; }

    /// <summary>
    /// 活动连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 线程池线程数
    /// </summary>
    public int ThreadPoolThreadCount { get; set; }

    /// <summary>
    /// 工作线程数
    /// </summary>
    public int WorkerThreadCount { get; set; }

    /// <summary>
    /// 完成端口线程数
    /// </summary>
    public int CompletionPortThreadCount { get; set; }
}

/// <summary>
/// 基准测试配置
/// </summary>
public class BenchmarkConfiguration
{
    /// <summary>
    /// 服务器主机地址
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// TCP端口
    /// </summary>
    public int TcpPort { get; set; } = 12345;

    /// <summary>
    /// KCP端口
    /// </summary>
    public int KcpPort { get; set; } = 12346;

    /// <summary>
    /// 是否启用KCP传输
    /// </summary>
    public bool EnableKcp { get; set; } = false;

    /// <summary>
    /// 服务器端口（保留向后兼容）
    /// </summary>
    public int Port
    {
        get => TcpPort;
        set => TcpPort = value;
    }

    /// <summary>
    /// 传输类型（保留向后兼容）
    /// </summary>
    public string TransportType { get; set; } = "tcp";

    /// <summary>
    /// 测试迭代次数
    /// </summary>
    public int Iterations { get; set; } = 1000;

    /// <summary>
    /// 并发连接数
    /// </summary>
    public int ConcurrentConnections { get; set; } = 1;

    /// <summary>
    /// 测试持续时间（秒）
    /// </summary>
    public int DurationSeconds { get; set; } = 30;

    /// <summary>
    /// 预热迭代次数
    /// </summary>
    public int WarmupIterations { get; set; } = 100;

    /// <summary>
    /// 预热时间（秒）
    /// </summary>
    public int WarmupSeconds { get; set; } = 5;

    /// <summary>
    /// 每个连接的操作数（如果设置，将忽略持续时间）
    /// </summary>
    public int? OperationsPerConnection { get; set; }

    /// <summary>
    /// 消息大小（字节）
    /// </summary>
    public int MessageSizeBytes { get; set; } = 1024;

    /// <summary>
    /// 测试间隔（毫秒）
    /// </summary>
    public int TestIntervalMs { get; set; } = 0;

    /// <summary>
    /// 请求间隔（毫秒，0表示无间隔）
    /// </summary>
    public int RequestIntervalMs { get; set; } = 0;

    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool EnableVerboseLogging { get; set; } = false;

    /// <summary>
    /// 是否收集资源指标
    /// </summary>
    public bool CollectResourceMetrics { get; set; } = true;

    /// <summary>
    /// TCP传输层选项
    /// </summary>
    public TransportOptions? TcpOptions { get; set; }

    /// <summary>
    /// KCP传输层选项
    /// </summary>
    public TransportOptions? KcpOptions { get; set; }

    /// <summary>
    /// 自定义配置
    /// </summary>
    public Dictionary<string, object> CustomConfiguration { get; set; } = new();

    /// <summary>
    /// 获取自定义配置
    /// </summary>
    /// <typeparam name="T">配置类型</typeparam>
    /// <param name="key">配置键</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>配置值</returns>
    public T GetCustomConfig<T>(string key, T defaultValue = default!)
    {
        if (CustomConfiguration.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// 设置自定义配置
    /// </summary>
    /// <param name="key">配置键</param>
    /// <param name="value">配置值</param>
    public void SetCustomConfig(string key, object value)
    {
        CustomConfiguration[key] = value;
    }
}

/// <summary>
/// 传输类型常量
/// </summary>
public static class TransportTypes
{
    /// <summary>
    /// TCP 传输
    /// </summary>
    public const string Tcp = "tcp";

    /// <summary>
    /// KCP 传输
    /// </summary>
    public const string Kcp = "kcp";

    /// <summary>
    /// UDP 传输
    /// </summary>
    public const string Udp = "udp";

    /// <summary>
    /// 内存传输（测试用）
    /// </summary>
    public const string Memory = "memory";

    /// <summary>
    /// WebSocket 传输
    /// </summary>
    public const string WebSocket = "websocket";
}
