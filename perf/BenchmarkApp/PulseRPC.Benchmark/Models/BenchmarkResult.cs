namespace PulseRPC.Benchmark.Models;

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
    /// 稳定性指标
    /// </summary>
    public StabilityMetrics Stability { get; set; } = new();
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
    /// 标准差（毫秒）
    /// </summary>
    public double StandardDeviationMs { get; set; }

    /// <summary>
    /// 样本数量
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
    /// 总数据传输量（字节）
    /// </summary>
    public long TotalBytesTransferred { get; set; }

    /// <summary>
    /// 平均带宽（字节/秒）
    /// </summary>
    public double AverageBandwidthBps { get; set; }

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
    /// 平均内存使用量（字节）
    /// </summary>
    public long AverageMemoryUsageBytes { get; set; }

    /// <summary>
    /// 峰值内存使用量（字节）
    /// </summary>
    public long PeakMemoryUsageBytes { get; set; }

    /// <summary>
    /// GC Gen0 回收次数
    /// </summary>
    public int GcGen0Collections { get; set; }

    /// <summary>
    /// GC Gen1 回收次数
    /// </summary>
    public int GcGen1Collections { get; set; }

    /// <summary>
    /// GC Gen2 回收次数
    /// </summary>
    public int GcGen2Collections { get; set; }
}

/// <summary>
/// 稳定性指标
/// </summary>
public class StabilityMetrics
{
    /// <summary>
    /// 是否检测到内存泄漏
    /// </summary>
    public bool MemoryLeakDetected { get; set; }

    /// <summary>
    /// 内存增长速率（字节/秒）
    /// </summary>
    public double MemoryGrowthRate { get; set; }

    /// <summary>
    /// 连接失败次数
    /// </summary>
    public int ConnectionFailures { get; set; }

    /// <summary>
    /// 内存样本数量
    /// </summary>
    public int MemorySampleCount { get; set; }
}
