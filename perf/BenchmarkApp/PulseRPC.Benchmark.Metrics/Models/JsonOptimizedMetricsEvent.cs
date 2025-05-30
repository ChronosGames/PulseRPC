using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseRPC.Benchmark.Metrics.Models;

/// <summary>
/// 针对System.Text.Json优化的指标事件
/// </summary>
public class JsonOptimizedMetricsEvent
{
    /// <summary>
    /// 事件时间戳
    /// </summary>
    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 指标名称
    /// </summary>
    [JsonPropertyName("name")]
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// 指标值（使用JsonElement支持任意类型）
    /// </summary>
    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }

    /// <summary>
    /// 标签字典
    /// </summary>
    [JsonPropertyName("tags")]
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 指标类型
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetricType Type { get; set; }

    /// <summary>
    /// 事件来源
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 事件级别
    /// </summary>
    [JsonPropertyName("level")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MetricLevel Level { get; set; } = MetricLevel.Info;

    /// <summary>
    /// 数据单位
    /// </summary>
    [JsonPropertyName("unit")]
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 附加属性（可选）
    /// </summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Properties { get; set; }

    /// <summary>
    /// 设置数值类型的指标值
    /// </summary>
    /// <param name="value">数值</param>
    public void SetValue(double value)
    {
        Value = JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// 设置整数类型的指标值
    /// </summary>
    /// <param name="value">整数</param>
    public void SetValue(long value)
    {
        Value = JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// 设置字符串类型的指标值
    /// </summary>
    /// <param name="value">字符串</param>
    public void SetValue(string value)
    {
        Value = JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// 设置布尔类型的指标值
    /// </summary>
    /// <param name="value">布尔值</param>
    public void SetValue(bool value)
    {
        Value = JsonSerializer.SerializeToElement(value);
    }

    /// <summary>
    /// 获取数值类型的指标值
    /// </summary>
    /// <returns>数值，如果转换失败返回0</returns>
    public double GetDoubleValue()
    {
        return Value.ValueKind == JsonValueKind.Number ? Value.GetDouble() : 0.0;
    }

    /// <summary>
    /// 获取整数类型的指标值
    /// </summary>
    /// <returns>整数，如果转换失败返回0</returns>
    public long GetLongValue()
    {
        return Value.ValueKind == JsonValueKind.Number ? Value.GetInt64() : 0L;
    }

    /// <summary>
    /// 获取字符串类型的指标值
    /// </summary>
    /// <returns>字符串，如果转换失败返回空字符串</returns>
    public string GetStringValue()
    {
        return Value.ValueKind == JsonValueKind.String ? Value.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// 添加标签
    /// </summary>
    /// <param name="key">标签键</param>
    /// <param name="value">标签值</param>
    public void AddTag(string key, string value)
    {
        Tags[key] = value;
    }

    /// <summary>
    /// 添加属性
    /// </summary>
    /// <param name="key">属性键</param>
    /// <param name="value">属性值</param>
    public void AddProperty<T>(string key, T value)
    {
        Properties ??= new Dictionary<string, JsonElement>();
        Properties[key] = JsonSerializer.SerializeToElement(value);
    }
}

/// <summary>
/// 指标类型枚举
/// </summary>
public enum MetricType
{
    /// <summary>
    /// 计数器
    /// </summary>
    Counter,

    /// <summary>
    /// 仪表盘
    /// </summary>
    Gauge,

    /// <summary>
    /// 直方图
    /// </summary>
    Histogram,

    /// <summary>
    /// 时间序列
    /// </summary>
    TimeSeries,

    /// <summary>
    /// 百分位数
    /// </summary>
    Percentile,

    /// <summary>
    /// 比率
    /// </summary>
    Rate,

    /// <summary>
    /// 自定义
    /// </summary>
    Custom
}

/// <summary>
/// 指标级别枚举
/// </summary>
public enum MetricLevel
{
    /// <summary>
    /// 调试
    /// </summary>
    Debug,

    /// <summary>
    /// 信息
    /// </summary>
    Info,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 严重错误
    /// </summary>
    Critical
}

/// <summary>
/// DateTime到Unix时间戳的自定义转换器
/// </summary>
public class DateTimeOffsetConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var unixTimeSeconds = reader.GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds).DateTime;
        }

        return reader.GetDateTime();
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var unixTimeSeconds = ((DateTimeOffset)value).ToUnixTimeSeconds();
        writer.WriteNumberValue(unixTimeSeconds);
    }
}
