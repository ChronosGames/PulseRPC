using System.Reflection;
using PulseRPC.Benchmark.Metrics.Abstractions;
using PulseRPC.Benchmark.Metrics.Models;

namespace PulseRPC.Benchmark.Server.Extensions;

/// <summary>
/// IMetricsCollector扩展方法
/// </summary>
public static class IMetricsCollectorExtensions
{
    /// <summary>
    /// 异步收集复杂对象指标
    /// </summary>
    /// <param name="collector">指标收集器</param>
    /// <param name="name">指标组名称</param>
    /// <param name="data">要收集的数据对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>收集任务</returns>
    public static Task CollectAsync(
        this IMetricsCollector collector,
        string name,
        object data,
        CancellationToken cancellationToken = default)
    {
        // 参数验证
        if (collector == null)
            throw new ArgumentNullException(nameof(collector));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("指标名称不能为空", nameof(name));
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        // 检查取消令牌
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // 分解对象为多个指标
            DecomposeObjectToMetrics(collector, name, data);

            // 返回已完成的任务
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            // 返回失败的任务
            return Task.FromException(ex);
        }
    }

    /// <summary>
    /// 将复杂对象分解为多个指标调用
    /// </summary>
    private static void DecomposeObjectToMetrics(IMetricsCollector collector, string baseName, object data)
    {
        var type = data.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // 创建基础标签
        var baseTags = new Dictionary<string, string>
        {
            ["metric_group"] = baseName,
            ["source"] = "server",
            ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        foreach (var property in properties)
        {
            try
            {
                var value = property.GetValue(data);
                if (value == null) continue;

                var metricName = $"{baseName}.{property.Name.ToLowerInvariant()}";
                var tags = new Dictionary<string, string>(baseTags)
                {
                    ["property"] = property.Name
                };

                // 根据属性类型确定指标类型和处理方式
                ProcessPropertyValue(collector, metricName, value, property.PropertyType, tags);
            }
            catch (Exception ex)
            {
                // 记录属性处理失败，但继续处理其他属性
                collector.RecordMetric(
                    $"{baseName}.error",
                    $"Failed to process property {property.Name}: {ex.Message}",
                    MetricType.Custom,
                    baseTags,
                    "error");
            }
        }
    }

    /// <summary>
    /// 处理属性值并记录指标
    /// </summary>
    private static void ProcessPropertyValue(
        IMetricsCollector collector,
        string metricName,
        object value,
        Type propertyType,
        Dictionary<string, string> tags)
    {
        // 处理数值类型
        if (IsNumericType(propertyType))
        {
            var numericValue = Convert.ToDouble(value);
            collector.RecordMetric(metricName, numericValue, MetricType.Gauge, tags, GetUnitForProperty(metricName));
        }
        // 处理时间类型
        else if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
        {
            var dateTime = (DateTime)value;
            var timestamp = dateTime.Ticks;
            collector.RecordMetric(metricName, timestamp, MetricType.Gauge, tags, "ticks");
        }
        // 处理TimeSpan类型
        else if (propertyType == typeof(TimeSpan) || propertyType == typeof(TimeSpan?))
        {
            var timeSpan = (TimeSpan)value;
            var milliseconds = timeSpan.TotalMilliseconds;
            collector.RecordMetric(metricName, milliseconds, MetricType.Gauge, tags, "ms");
        }
        // 处理布尔类型
        else if (propertyType == typeof(bool) || propertyType == typeof(bool?))
        {
            var boolValue = (bool)value;
            collector.RecordMetric(metricName, boolValue ? 1 : 0, MetricType.Gauge, tags, "bool");
        }
        // 处理字符串和其他类型
        else
        {
            var stringValue = value.ToString() ?? string.Empty;
            collector.RecordMetric(metricName, stringValue, MetricType.Custom, tags, "text");
        }
    }

    /// <summary>
    /// 检查是否为数值类型
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType == typeof(int) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(float) ||
               underlyingType == typeof(double) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(byte) ||
               underlyingType == typeof(sbyte) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(ushort) ||
               underlyingType == typeof(uint) ||
               underlyingType == typeof(ulong);
    }

    /// <summary>
    /// 根据指标名称获取适当的单位
    /// </summary>
    private static string GetUnitForProperty(string metricName)
    {
        var lowerName = metricName.ToLowerInvariant();

        if (lowerName.Contains("memory") || lowerName.Contains("bytes"))
            return "bytes";
        if (lowerName.Contains("percent") || lowerName.Contains("usage"))
            return "percent";
        if (lowerName.Contains("count") || lowerName.Contains("thread") || lowerName.Contains("handle"))
            return "count";
        if (lowerName.Contains("port"))
            return "port";
        if (lowerName.Contains("connection"))
            return "connections";
        if (lowerName.Contains("generation"))
            return "gen";

        return "value";
    }
}
