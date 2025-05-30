using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseRPC.Benchmark.Metrics.Models;

/// <summary>
/// 针对System.Text.Json优化的指标快照
/// </summary>
public class JsonOptimizedMetricsSnapshot
{
    /// <summary>
    /// 快照时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 指标事件字典
    /// </summary>
    [JsonPropertyName("metrics")]
    public Dictionary<string, JsonOptimizedMetricsEvent> Metrics { get; set; } = new();

    /// <summary>
    /// 收集耗时
    /// </summary>
    [JsonPropertyName("duration")]
    [JsonConverter(typeof(TimeSpanMillisecondsConverter))]
    public TimeSpan CollectionDuration { get; set; }

    /// <summary>
    /// 收集器名称
    /// </summary>
    [JsonPropertyName("collector")]
    public string CollectorName { get; set; } = string.Empty;

    /// <summary>
    /// 快照序列号
    /// </summary>
    [JsonPropertyName("sequence")]
    public long SequenceNumber { get; set; }

    /// <summary>
    /// 快照版本
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// 指标总数
    /// </summary>
    [JsonPropertyName("count")]
    public int MetricsCount => Metrics.Count;

    /// <summary>
    /// 数据完整性哈希（可选）
    /// </summary>
    [JsonPropertyName("checksum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Checksum { get; set; }

    /// <summary>
    /// 采样配置（可选）
    /// </summary>
    [JsonPropertyName("sampling")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SamplingConfig? Sampling { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>
    /// 添加指标事件
    /// </summary>
    /// <param name="name">指标名称</param>
    /// <param name="metricsEvent">指标事件</param>
    public void AddMetric(string name, JsonOptimizedMetricsEvent metricsEvent)
    {
        Metrics[name] = metricsEvent;
    }

    /// <summary>
    /// 批量添加指标事件
    /// </summary>
    /// <param name="metrics">指标事件字典</param>
    public void AddMetrics(Dictionary<string, JsonOptimizedMetricsEvent> metrics)
    {
        foreach (var kvp in metrics)
        {
            Metrics[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// 获取指定类型的指标
    /// </summary>
    /// <param name="type">指标类型</param>
    /// <returns>指标事件集合</returns>
    public IEnumerable<JsonOptimizedMetricsEvent> GetMetricsByType(MetricType type)
    {
        return Metrics.Values.Where(m => m.Type == type);
    }

    /// <summary>
    /// 获取指定标签的指标
    /// </summary>
    /// <param name="tagKey">标签键</param>
    /// <param name="tagValue">标签值</param>
    /// <returns>指标事件集合</returns>
    public IEnumerable<JsonOptimizedMetricsEvent> GetMetricsByTag(string tagKey, string tagValue)
    {
        return Metrics.Values.Where(m => m.Tags.TryGetValue(tagKey, out var value) && value == tagValue);
    }

    /// <summary>
    /// 获取时间范围内的指标
    /// </summary>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <returns>指标事件集合</returns>
    public IEnumerable<JsonOptimizedMetricsEvent> GetMetricsByTimeRange(DateTime startTime, DateTime endTime)
    {
        return Metrics.Values.Where(m => m.Timestamp >= startTime && m.Timestamp <= endTime);
    }

    /// <summary>
    /// 计算快照的数据大小（估算，字节）
    /// </summary>
    /// <returns>估算的数据大小</returns>
    public long EstimateDataSize()
    {
        // 简单估算，实际大小会因JSON格式化而有所不同
        long size = 200; // 基础结构大小

        foreach (var metric in Metrics.Values)
        {
            size += metric.MetricName.Length * 2; // UTF-16
            size += metric.Source.Length * 2;
            size += metric.Unit.Length * 2;
            size += metric.Tags.Sum(t => (t.Key.Length + t.Value.Length) * 2);
            size += 100; // 其他字段估算
        }

        return size;
    }

    /// <summary>
    /// 添加元数据
    /// </summary>
    /// <param name="key">元数据键</param>
    /// <param name="value">元数据值</param>
    public void AddMetadata<T>(string key, T value)
    {
        Metadata ??= new Dictionary<string, JsonElement>();
        Metadata[key] = JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// 清空所有指标
    /// </summary>
    public void Clear()
    {
        Metrics.Clear();
        Metadata?.Clear();
    }

    /// <summary>
    /// 创建快照的精简版本（只包含基本信息）
    /// </summary>
    /// <returns>精简的快照</returns>
    public JsonOptimizedMetricsSnapshot CreateSummary()
    {
        return new JsonOptimizedMetricsSnapshot
        {
            Timestamp = Timestamp,
            CollectionDuration = CollectionDuration,
            CollectorName = CollectorName,
            SequenceNumber = SequenceNumber,
            Version = Version,
            // 只包含计数，不包含具体指标数据
            Metadata = new Dictionary<string, JsonElement>
            {
                ["original_metrics_count"] = JsonSerializer.SerializeToElement(MetricsCount),
                ["estimated_size_bytes"] = JsonSerializer.SerializeToElement(EstimateDataSize())
            }
        };
    }
}

/// <summary>
/// 采样配置
/// </summary>
public class SamplingConfig
{
    /// <summary>
    /// 采样率（0.0 - 1.0）
    /// </summary>
    [JsonPropertyName("rate")]
    public double Rate { get; set; } = 1.0;

    /// <summary>
    /// 采样策略
    /// </summary>
    [JsonPropertyName("strategy")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SamplingStrategy Strategy { get; set; } = SamplingStrategy.Random;

    /// <summary>
    /// 采样间隔（毫秒）
    /// </summary>
    [JsonPropertyName("interval_ms")]
    public int IntervalMs { get; set; } = 1000;

    /// <summary>
    /// 最大样本数
    /// </summary>
    [JsonPropertyName("max_samples")]
    public int MaxSamples { get; set; } = 10000;
}

/// <summary>
/// 采样策略
/// </summary>
public enum SamplingStrategy
{
    /// <summary>
    /// 随机采样
    /// </summary>
    Random,

    /// <summary>
    /// 时间间隔采样
    /// </summary>
    TimeInterval,

    /// <summary>
    /// 固定数量采样
    /// </summary>
    FixedCount,

    /// <summary>
    /// 自适应采样
    /// </summary>
    Adaptive
}

/// <summary>
/// TimeSpan到毫秒的自定义转换器
/// </summary>
public class TimeSpanMillisecondsConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return TimeSpan.FromMilliseconds(reader.GetDouble());
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (TimeSpan.TryParse(str, out var timeSpan))
            {
                return timeSpan;
            }
        }

        throw new JsonException("无法解析TimeSpan值");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.TotalMilliseconds);
    }
}
