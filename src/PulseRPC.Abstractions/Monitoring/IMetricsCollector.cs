namespace PulseRPC.Monitoring;

/// <summary>
/// 指标收集器接口
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// 获取或创建计数器
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="tags">标签</param>
    /// <returns>计数器实例</returns>
    ICounter GetCounter(string name, string? description = null, IDictionary<string, string>? tags = null);

    /// <summary>
    /// 获取或创建仪表
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="tags">标签</param>
    /// <returns>仪表实例</returns>
    IGauge GetGauge(string name, string? description = null, IDictionary<string, string>? tags = null);

    /// <summary>
    /// 获取或创建直方图
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="description">指标描述</param>
    /// <param name="tags">标签</param>
    /// <returns>直方图实例</returns>
    IHistogram GetHistogram(string name, string? description = null, IDictionary<string, string>? tags = null);

    /// <summary>
    /// 记录自定义指标
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="value">指标值</param>
    /// <param name="tags">标签</param>
    void RecordMetric(string name, double value, IDictionary<string, string>? tags = null);
}

/// <summary>
/// 计数器接口
/// </summary>
public interface ICounter
{
    /// <summary>
    /// 递增计数器
    /// </summary>
    /// <param name="delta">增量值</param>
    void Increment(double delta = 1.0);

    /// <summary>
    /// 获取当前值
    /// </summary>
    double Value { get; }
}

/// <summary>
/// 仪表接口
/// </summary>
public interface IGauge
{
    /// <summary>
    /// 设置值
    /// </summary>
    /// <param name="value">值</param>
    void Set(double value);

    /// <summary>
    /// 获取当前值
    /// </summary>
    double Value { get; }
}

/// <summary>
/// 直方图接口
/// </summary>
public interface IHistogram
{
    /// <summary>
    /// 记录观测值
    /// </summary>
    /// <param name="value">观测值</param>
    void Record(double value);

    /// <summary>
    /// 获取总计数
    /// </summary>
    long Count { get; }

    /// <summary>
    /// 获取总和
    /// </summary>
    double Sum { get; }
} 