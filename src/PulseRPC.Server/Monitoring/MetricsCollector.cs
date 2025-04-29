using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace PulseRPC.Server.Monitoring;

/// <summary>
/// 用于收集和存储指标数据的收集器
/// </summary>
public class MetricsCollector : IDisposable
{
    private readonly MeterListener _listener;
    private readonly ConcurrentDictionary<string, long> _counterValues = new();
    private readonly ConcurrentDictionary<string, List<double>> _histogramValues = new();

    /// <summary>
    /// 创建并开始收集指标
    /// </summary>
    public MetricsCollector()
    {
        _listener = new MeterListener();

        // 配置要监听的仪表
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "PulseRPC.Server")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        // 处理计数器事件
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _counterValues[instrument.Name] = measurement;
        });

        // 处理直方图事件
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (!_histogramValues.TryGetValue(instrument.Name, out var values))
            {
                values = new List<double>();
                _histogramValues[instrument.Name] = values;
            }

            lock (values)
            {
                values.Add(measurement);

                // 限制存储的值数量，防止内存泄漏
                if (values.Count > 1000)
                {
                    values.RemoveAt(0);
                }
            }
        });

        // 启动监听器
        _listener.Start();
    }

    /// <summary>
    /// 获取计数器当前值
    /// </summary>
    /// <param name="name">计数器名称</param>
    /// <returns>计数器值，如果不存在返回0</returns>
    public long GetCounterValue(string name)
    {
        return _counterValues.TryGetValue(name, out var value) ? value : 0;
    }

    /// <summary>
    /// 获取直方图的最小值、平均值、最大值
    /// </summary>
    /// <param name="name">直方图名称</param>
    /// <returns>元组(最小值, 平均值, 最大值)</returns>
    public (double Min, double Avg, double Max) GetHistogramStats(string name)
    {
        if (_histogramValues.TryGetValue(name, out var values) && values.Count > 0)
        {
            lock (values)
            {
                return (
                    values.Min(),
                    values.Average(),
                    values.Max()
                );
            }
        }

        return (0, 0, 0);
    }

    /// <summary>
    /// 获取所有指标名称
    /// </summary>
    public IEnumerable<string> GetMetricNames()
    {
        return _counterValues.Keys.Concat(_histogramValues.Keys).Distinct();
    }

    /// <summary>
    /// 获取完整的指标快照
    /// </summary>
    public MetricsSnapshot GetCompleteSnapshot()
    {
        return new MetricsSnapshot
        {
            ActiveRequests = (int)GetCounterValue("pulserpc.server.active_requests"),
            TotalConnections = GetCounterValue("pulserpc.server.connections.total"),
            TotalRequests = GetCounterValue("pulserpc.server.requests.total"),
            FailedRequests = GetCounterValue("pulserpc.server.requests.failed")
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _listener.Dispose();
    }
}
