using System.Text.Json;

namespace PulseRPC.Benchmark.Metrics.Serialization;

/// <summary>
/// JSON序列化选项配置
/// </summary>
public class JsonSerializationOptions
{
    /// <summary>
    /// 属性命名策略（默认：CamelCase）
    /// </summary>
    public JsonNamingPolicy? PropertyNamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;

    /// <summary>
    /// 是否格式化输出（默认：false）
    /// </summary>
    public bool WriteIndented { get; set; } = false;

    /// <summary>
    /// 是否忽略null值（默认：true）
    /// </summary>
    public bool IgnoreNullValues { get; set; } = true;

    /// <summary>
    /// 最大递归深度（默认：64）
    /// </summary>
    public int MaxDepth { get; set; } = 64;

    /// <summary>
    /// 是否启用源生成器（默认：true）
    /// </summary>
    public bool UseSourceGeneration { get; set; } = true;

    /// <summary>
    /// 是否允许注释（默认：false）
    /// </summary>
    public bool AllowComments { get; set; } = false;

    /// <summary>
    /// 是否允许尾随逗号（默认：false）
    /// </summary>
    public bool AllowTrailingCommas { get; set; } = false;

    /// <summary>
    /// 字符串转义策略（默认：Default）
    /// </summary>
    public string? Encoder { get; set; } = null;

    /// <summary>
    /// 流式处理阈值（字节数，默认：10KB）
    /// </summary>
    public int StreamingThreshold { get; set; } = 10240;

    /// <summary>
    /// JSON缓冲区大小（默认：64KB）
    /// </summary>
    public int JsonBufferSize { get; set; } = 65536;

    /// <summary>
    /// 是否启用压缩（默认：false）
    /// </summary>
    public bool UseCompression { get; set; } = false;

    /// <summary>
    /// 压缩级别（默认：Optimal）
    /// </summary>
    public string CompressionLevel { get; set; } = "Optimal";

    /// <summary>
    /// 自定义转换器类型名称列表
    /// </summary>
    public List<string> CustomConverters { get; set; } = new();

    /// <summary>
    /// 性能监控设置
    /// </summary>
    public PerformanceMonitoringOptions PerformanceMonitoring { get; set; } = new();
}

/// <summary>
/// JSON序列化性能监控选项
/// </summary>
public class PerformanceMonitoringOptions
{
    /// <summary>
    /// 是否启用性能监控（默认：true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否收集序列化时间（默认：true）
    /// </summary>
    public bool TrackSerializationTime { get; set; } = true;

    /// <summary>
    /// 是否收集内存使用情况（默认：true）
    /// </summary>
    public bool TrackMemoryUsage { get; set; } = true;

    /// <summary>
    /// 是否收集JSON大小统计（默认：true）
    /// </summary>
    public bool TrackJsonSize { get; set; } = true;

    /// <summary>
    /// 性能日志级别（默认：Debug）
    /// </summary>
    public string LogLevel { get; set; } = "Debug";
}
