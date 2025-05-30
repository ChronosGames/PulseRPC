using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Metrics.Abstractions;

/// <summary>
/// 指标收集器接口
/// </summary>
public interface IMetricsCollector : IMetricsPlugin
{
    /// <summary>
    /// 开始收集指标
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>启动任务</returns>
    Task StartCollectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止收集指标
    /// </summary>
    /// <returns>停止任务</returns>
    Task StopCollectionAsync();

    /// <summary>
    /// 获取当前指标快照
    /// </summary>
    /// <returns>指标快照</returns>
    Task<JsonOptimizedMetricsSnapshot> GetSnapshotAsync();

    /// <summary>
    /// 获取指标快照历史
    /// </summary>
    /// <param name="count">获取数量</param>
    /// <returns>历史快照列表</returns>
    Task<List<JsonOptimizedMetricsSnapshot>> GetSnapshotHistoryAsync(int count = 10);

    /// <summary>
    /// 清空收集的指标
    /// </summary>
    /// <returns>清空任务</returns>
    Task ClearMetricsAsync();

    /// <summary>
    /// 添加自定义指标
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="value">指标值</param>
    /// <param name="type">指标类型</param>
    /// <param name="tags">标签</param>
    /// <param name="unit">单位</param>
    void RecordMetric(string name, object value, MetricType type = MetricType.Custom, Dictionary<string, string>? tags = null, string unit = "");

    /// <summary>
    /// 收集器配置
    /// </summary>
    CollectorConfiguration Configuration { get; }

    /// <summary>
    /// 指标收集事件（实时指标）
    /// </summary>
    event Action<MetricsCollectedEventArgs>? MetricsCollected;

    /// <summary>
    /// 快照生成事件
    /// </summary>
    event Action<SnapshotCreatedEventArgs>? SnapshotCreated;
}

/// <summary>
/// 收集器配置
/// </summary>
public class CollectorConfiguration
{
    /// <summary>
    /// 采样间隔（毫秒）
    /// </summary>
    public int SamplingIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 最大历史快照数量
    /// </summary>
    public int MaxHistorySnapshots { get; set; } = 100;

    /// <summary>
    /// 缓冲区大小
    /// </summary>
    public int BufferSize { get; set; } = 10000;

    /// <summary>
    /// 是否启用自动快照生成
    /// </summary>
    public bool EnableAutoSnapshot { get; set; } = true;

    /// <summary>
    /// 快照生成间隔（毫秒）
    /// </summary>
    public int SnapshotIntervalMs { get; set; } = 5000;

    /// <summary>
    /// 是否收集系统指标
    /// </summary>
    public bool CollectSystemMetrics { get; set; } = true;

    /// <summary>
    /// 指标过滤器
    /// </summary>
    public List<string> MetricFilters { get; set; } = new();

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public Dictionary<string, string> TagFilters { get; set; } = new();
}

/// <summary>
/// 指标收集事件参数
/// </summary>
public class MetricsCollectedEventArgs : EventArgs
{
    /// <summary>
    /// 收集的指标事件
    /// </summary>
    public JsonOptimizedMetricsEvent MetricEvent { get; }

    /// <summary>
    /// 收集时间
    /// </summary>
    public DateTime CollectionTime { get; }

    /// <summary>
    /// 收集器名称
    /// </summary>
    public string CollectorName { get; }

    public MetricsCollectedEventArgs(JsonOptimizedMetricsEvent metricEvent, string collectorName)
    {
        MetricEvent = metricEvent;
        CollectionTime = DateTime.UtcNow;
        CollectorName = collectorName;
    }
}

/// <summary>
/// 快照创建事件参数
/// </summary>
public class SnapshotCreatedEventArgs : EventArgs
{
    /// <summary>
    /// 创建的快照
    /// </summary>
    public JsonOptimizedMetricsSnapshot Snapshot { get; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; }

    /// <summary>
    /// 包含的指标数量
    /// </summary>
    public int MetricsCount => Snapshot.MetricsCount;

    public SnapshotCreatedEventArgs(JsonOptimizedMetricsSnapshot snapshot)
    {
        Snapshot = snapshot;
        CreationTime = DateTime.UtcNow;
    }
}
