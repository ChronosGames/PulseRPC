using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Abstractions;

/// <summary>
/// 指标聚合器接口
/// </summary>
public interface IMetricsAggregator : IMetricsPlugin
{
    /// <summary>
    /// 聚合指标数据
    /// </summary>
    /// <param name="metrics">原始指标数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聚合结果</returns>
    Task<AggregationResult> AggregateMetricsAsync(IEnumerable<JsonOptimizedMetricsEvent> metrics, CancellationToken cancellationToken = default);

    /// <summary>
    /// 聚合快照数据
    /// </summary>
    /// <param name="snapshots">快照数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>聚合结果</returns>
    Task<AggregationResult> AggregateSnapshotsAsync(IEnumerable<JsonOptimizedMetricsSnapshot> snapshots, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取聚合结果
    /// </summary>
    /// <param name="timeRange">时间范围</param>
    /// <param name="metricNames">指标名称过滤</param>
    /// <returns>聚合结果列表</returns>
    Task<List<AggregationResult>> GetAggregatedResultsAsync(TimeRange? timeRange = null, IEnumerable<string>? metricNames = null);

    /// <summary>
    /// 配置时间窗口
    /// </summary>
    /// <param name="windowConfig">窗口配置</param>
    /// <returns>配置是否成功</returns>
    Task<bool> ConfigureTimeWindowsAsync(TimeWindowConfiguration windowConfig);

    /// <summary>
    /// 清空聚合数据
    /// </summary>
    /// <returns>清空任务</returns>
    Task ClearAggregatedDataAsync();

    /// <summary>
    /// 获取聚合统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    Task<AggregatorStatistics> GetStatisticsAsync();

    /// <summary>
    /// 聚合器配置
    /// </summary>
    AggregatorConfiguration Configuration { get; }

    /// <summary>
    /// 聚合完成事件
    /// </summary>
    event Action<AggregationCompletedEventArgs>? AggregationCompleted;

    /// <summary>
    /// 窗口更新事件
    /// </summary>
    event Action<WindowUpdatedEventArgs>? WindowUpdated;
}

/// <summary>
/// 聚合结果
/// </summary>
public class AggregationResult
{
    /// <summary>
    /// 聚合器名称
    /// </summary>
    public string AggregatorName { get; set; } = string.Empty;

    /// <summary>
    /// 聚合时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 时间窗口
    /// </summary>
    public TimeRange TimeWindow { get; set; } = new();

    /// <summary>
    /// 聚合的指标类型
    /// </summary>
    public string MetricType { get; set; } = string.Empty;

    /// <summary>
    /// 聚合数据
    /// </summary>
    public Dictionary<string, AggregatedMetric> AggregatedMetrics { get; set; } = new();

    /// <summary>
    /// 聚合持续时间
    /// </summary>
    public TimeSpan AggregationDuration { get; set; }

    /// <summary>
    /// 处理的数据点数量
    /// </summary>
    public int ProcessedDataPoints { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 添加聚合指标
    /// </summary>
    public void AddAggregatedMetric(string name, AggregatedMetric metric)
    {
        AggregatedMetrics[name] = metric;
    }

    /// <summary>
    /// 获取聚合指标
    /// </summary>
    public AggregatedMetric? GetAggregatedMetric(string name)
    {
        return AggregatedMetrics.TryGetValue(name, out var metric) ? metric : null;
    }
}

/// <summary>
/// 聚合指标
/// </summary>
public class AggregatedMetric
{
    /// <summary>
    /// 指标名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 最小值
    /// </summary>
    public double Min { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    public double Max { get; set; }

    /// <summary>
    /// 平均值
    /// </summary>
    public double Average { get; set; }

    /// <summary>
    /// 中位数
    /// </summary>
    public double Median { get; set; }

    /// <summary>
    /// 标准差
    /// </summary>
    public double StandardDeviation { get; set; }

    /// <summary>
    /// 百分位数
    /// </summary>
    public Dictionary<int, double> Percentiles { get; set; } = new();

    /// <summary>
    /// 样本数量
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// 总和
    /// </summary>
    public double Sum { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();
}

/// <summary>
/// 时间范围
/// </summary>
public class TimeRange
{
    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 持续时间
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// 是否包含指定时间
    /// </summary>
    public bool Contains(DateTime timestamp)
    {
        return timestamp >= StartTime && timestamp <= EndTime;
    }

    /// <summary>
    /// 与另一个时间范围是否重叠
    /// </summary>
    public bool Overlaps(TimeRange other)
    {
        return StartTime <= other.EndTime && EndTime >= other.StartTime;
    }
}

/// <summary>
/// 时间窗口配置
/// </summary>
public class TimeWindowConfiguration
{
    /// <summary>
    /// 窗口大小
    /// </summary>
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 滑动间隔
    /// </summary>
    public TimeSpan SlideInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 窗口类型
    /// </summary>
    public WindowType WindowType { get; set; } = WindowType.Sliding;

    /// <summary>
    /// 最大窗口数量
    /// </summary>
    public int MaxWindows { get; set; } = 100;

    /// <summary>
    /// 是否自动清理过期窗口
    /// </summary>
    public bool AutoCleanup { get; set; } = true;

    /// <summary>
    /// 过期时间
    /// </summary>
    public TimeSpan ExpirationTime { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// 窗口类型
/// </summary>
public enum WindowType
{
    /// <summary>
    /// 滑动窗口
    /// </summary>
    Sliding,

    /// <summary>
    /// 固定窗口
    /// </summary>
    Fixed,

    /// <summary>
    /// 会话窗口
    /// </summary>
    Session
}

/// <summary>
/// 聚合器配置
/// </summary>
public class AggregatorConfiguration
{
    /// <summary>
    /// 默认时间窗口配置
    /// </summary>
    public TimeWindowConfiguration DefaultWindowConfig { get; set; } = new();

    /// <summary>
    /// 是否启用统计计算
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// 要计算的百分位数
    /// </summary>
    public List<int> Percentiles { get; set; } = new() { 50, 90, 95, 99 };

    /// <summary>
    /// 最大内存使用量（字节）
    /// </summary>
    public long MaxMemoryUsage { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// 聚合缓冲区大小
    /// </summary>
    public int BufferSize { get; set; } = 10000;

    /// <summary>
    /// 是否启用并行处理
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// 并行度
    /// </summary>
    public int ParallelismDegree { get; set; } = Environment.ProcessorCount;
}

/// <summary>
/// 聚合器统计信息
/// </summary>
public class AggregatorStatistics
{
    /// <summary>
    /// 总处理数据点数
    /// </summary>
    public long TotalProcessedDataPoints { get; set; }

    /// <summary>
    /// 总聚合次数
    /// </summary>
    public long TotalAggregations { get; set; }

    /// <summary>
    /// 平均聚合时间
    /// </summary>
    public TimeSpan AverageAggregationTime { get; set; }

    /// <summary>
    /// 当前活跃窗口数
    /// </summary>
    public int ActiveWindows { get; set; }

    /// <summary>
    /// 内存使用量
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// 最后聚合时间
    /// </summary>
    public DateTime LastAggregationTime { get; set; }

    /// <summary>
    /// 错误次数
    /// </summary>
    public long ErrorCount { get; set; }
}

/// <summary>
/// 聚合完成事件参数
/// </summary>
public class AggregationCompletedEventArgs : EventArgs
{
    /// <summary>
    /// 聚合结果
    /// </summary>
    public AggregationResult Result { get; }

    /// <summary>
    /// 聚合器名称
    /// </summary>
    public string AggregatorName { get; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime CompletionTime { get; }

    public AggregationCompletedEventArgs(AggregationResult result, string aggregatorName)
    {
        Result = result;
        AggregatorName = aggregatorName;
        CompletionTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 窗口更新事件参数
/// </summary>
public class WindowUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// 窗口时间范围
    /// </summary>
    public TimeRange WindowRange { get; }

    /// <summary>
    /// 更新类型
    /// </summary>
    public WindowUpdateType UpdateType { get; }

    /// <summary>
    /// 聚合器名称
    /// </summary>
    public string AggregatorName { get; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdateTime { get; }

    public WindowUpdatedEventArgs(TimeRange windowRange, WindowUpdateType updateType, string aggregatorName)
    {
        WindowRange = windowRange;
        UpdateType = updateType;
        AggregatorName = aggregatorName;
        UpdateTime = DateTime.UtcNow;
    }
}

/// <summary>
/// 窗口更新类型
/// </summary>
public enum WindowUpdateType
{
    /// <summary>
    /// 创建新窗口
    /// </summary>
    Created,

    /// <summary>
    /// 更新现有窗口
    /// </summary>
    Updated,

    /// <summary>
    /// 关闭窗口
    /// </summary>
    Closed,

    /// <summary>
    /// 过期清理
    /// </summary>
    Expired
}
