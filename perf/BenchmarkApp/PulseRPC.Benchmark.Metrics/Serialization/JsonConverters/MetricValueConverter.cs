using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseRPC.Benchmark.Metrics.Serialization.JsonConverters;

/// <summary>
/// 指标值的灵活转换器，支持多种数据类型
/// </summary>
public class MetricValueConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number when reader.TryGetInt64(out var longValue) => longValue,
            JsonTokenType.Number when reader.TryGetDouble(out var doubleValue) => doubleValue,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.StartObject => JsonSerializer.Deserialize<Dictionary<string, object>>(ref reader, options),
            JsonTokenType.StartArray => JsonSerializer.Deserialize<object[]>(ref reader, options),
            _ => JsonSerializer.Deserialize<JsonElement>(ref reader, options)
        };
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case float floatValue:
                writer.WriteNumberValue(floatValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case DateTime dateTimeValue:
                writer.WriteStringValue(dateTimeValue.ToString("O"));
                break;
            case TimeSpan timeSpanValue:
                writer.WriteNumberValue(timeSpanValue.TotalMilliseconds);
                break;
            case Guid guidValue:
                writer.WriteStringValue(guidValue.ToString());
                break;
            default:
                // 对于复杂对象，使用默认序列化
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                break;
        }
    }

    public override bool CanConvert(Type typeToConvert)
    {
        // 支持任何类型
        return true;
    }
}

/// <summary>
/// 高精度数值转换器（用于性能指标）
/// </summary>
public class HighPrecisionNumberConverter : JsonConverter<double>
{
    private readonly int _precision;

    public HighPrecisionNumberConverter(int precision = 6)
    {
        _precision = precision;
    }

    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        // 保持指定精度
        var rounded = Math.Round(value, _precision);
        writer.WriteNumberValue(rounded);
    }
}

/// <summary>
/// 字节大小转换器（自动选择合适的单位）
/// </summary>
public class ByteSizeConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            return ParseByteSize(str);
        }

        throw new JsonException("无法解析字节大小值");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        // 始终写入数值，但可以添加单位信息到属性中
        writer.WriteNumberValue(value);
    }

    private static long ParseByteSize(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return 0;

        input = input.Trim().ToUpperInvariant();

        if (long.TryParse(input, out var directValue))
            return directValue;

        var units = new Dictionary<string, long>
        {
            { "B", 1L },
            { "KB", 1024L },
            { "MB", 1024L * 1024L },
            { "GB", 1024L * 1024L * 1024L },
            { "TB", 1024L * 1024L * 1024L * 1024L }
        };

        foreach (var unit in units)
        {
            if (input.EndsWith(unit.Key))
            {
                var numberPart = input.Substring(0, input.Length - unit.Key.Length).Trim();
                if (double.TryParse(numberPart, out var number))
                {
                    return (long)(number * unit.Value);
                }
            }
        }

        throw new JsonException($"无法解析字节大小: {input}");
    }
}
