using System.Collections.Concurrent;
using System.Diagnostics;

namespace PulseRPC.Monitoring.Metrics;

/// <summary>
/// 计数器实现
/// </summary>
public class Counter(string name, string? description, IDictionary<string, string> tags)
    : ICounter
{
    private readonly Lock _lock = new();
    private double _value;

    public double Value => _value;

    public void Increment(double value = 1.0)
    {
        if (value < 0)
            throw new ArgumentException("Counter increment value must be non-negative", nameof(value));

        lock (_lock)
        {
            _value += value;
        }
    }

    public CounterSnapshot GetSnapshot()
    {
        return new CounterSnapshot
        {
            Name = name,
            Description = description,
            Tags = new Dictionary<string, string>(tags),
            Value = _value,
            Timestamp = DateTime.UtcNow
        };
    }

    public void Reset()
    {
        lock (_lock)
        {
            _value = 0;
        }
    }
}

/// <summary>
/// 仪表实现
/// </summary>
public class Gauge(string name, string? description, IDictionary<string, string> tags)
    : IGauge
{
    private readonly Lock _lock = new();
    private double _value;

    public double Value => _value;

    public void Set(double value)
    {
        lock (_lock)
        {
            _value = value;
        }
    }

    public void Increment(double value = 1.0)
    {
        lock (_lock)
        {
            _value += value;
        }
    }

    public void Decrement(double value = 1.0)
    {
        lock (_lock)
        {
            _value -= value;
        }
    }

    public GaugeSnapshot GetSnapshot()
    {
        return new GaugeSnapshot
        {
            Name = name,
            Description = description,
            Tags = new Dictionary<string, string>(tags),
            Value = _value,
            Timestamp = DateTime.UtcNow
        };
    }

    public void Reset()
    {
        lock (_lock)
        {
            _value = 0;
        }
    }
}

/// <summary>
/// 直方图实现
/// </summary>
public class Histogram : IHistogram
{
    private readonly string _name;
    private readonly string? _description;
    private readonly IDictionary<string, string> _tags;
    private readonly double[] _buckets;
    private readonly ConcurrentDictionary<double, long> _bucketCounts = new();
    private long _count;
    private double _sum;
    private readonly Lock _lock = new();

    private static readonly double[] DefaultBuckets =
    [
        0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1.0, 2.5, 5.0, 7.5, 10.0, double.PositiveInfinity
    ];

    public Histogram(string name, string? description, double[]? buckets, IDictionary<string, string> tags)
    {
        _name = name;
        _description = description;
        _tags = tags;
        _buckets = buckets ?? DefaultBuckets;

        // 初始化分桶计数
        foreach (var bucket in _buckets)
        {
            _bucketCounts[bucket] = 0;
        }
    }

    public long Count => _count;
    public double Sum => _sum;

    public void Observe(double value)
    {
        lock (_lock)
        {
            _count++;
            _sum += value;

            // 更新分桶计数
            foreach (var bucket in _buckets)
            {
                if (value <= bucket)
                {
                    _bucketCounts.AddOrUpdate(bucket, 1, (_, count) => count + 1);
                }
            }
        }
    }

    public HistogramSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new HistogramSnapshot
            {
                Name = _name,
                Description = _description,
                Tags = new Dictionary<string, string>(_tags),
                Count = _count,
                Sum = _sum,
                Buckets = _bucketCounts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Timestamp = DateTime.UtcNow
            };
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _count = 0;
            _sum = 0;

            foreach (var bucket in _buckets)
            {
                _bucketCounts[bucket] = 0;
            }
        }
    }
}

/// <summary>
/// 计时器实现
/// </summary>
public class Timer : ITimer
{
    private readonly string _name;
    private readonly string? _description;
    private readonly IDictionary<string, string> _tags;
    private readonly Histogram _histogram;
    private readonly object _lock = new();

    public Timer(string name, string? description, IDictionary<string, string> tags)
    {
        _name = name;
        _description = description;
        _tags = tags;

        // 使用适合时间的分桶
        var timeBuckets = new[]
        {
            0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0, double.PositiveInfinity
        };

        _histogram = new Histogram(name, description, timeBuckets, tags);
    }

    public void Record(TimeSpan duration)
    {
        _histogram.Observe(duration.TotalSeconds);
    }

    public ITimerContext StartTimer()
    {
        return new TimerContext(this);
    }

    public TimerSnapshot GetSnapshot()
    {
        var histogramSnapshot = _histogram.GetSnapshot();
        return new TimerSnapshot
        {
            Name = _name,
            Description = _description,
            Tags = new Dictionary<string, string>(_tags),
            Count = histogramSnapshot.Count,
            Sum = histogramSnapshot.Sum,
            Buckets = histogramSnapshot.Buckets,
            Timestamp = DateTime.UtcNow
        };
    }

    public void Reset()
    {
        _histogram.Reset();
    }
}

/// <summary>
/// 计时器上下文实现
/// </summary>
public class TimerContext : ITimerContext
{
    private readonly Timer _timer;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public TimerContext(Timer timer)
    {
        _timer = timer;
        _stopwatch = Stopwatch.StartNew();
    }

    public TimeSpan Stop()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TimerContext));

        _stopwatch.Stop();
        var elapsed = _stopwatch.Elapsed;
        _timer.Record(elapsed);
        return elapsed;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_stopwatch.IsRunning)
            {
                Stop();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// 计数器快照
/// </summary>
public class CounterSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 仪表快照
/// </summary>
public class GaugeSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 直方图快照
/// </summary>
public class HistogramSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public long Count { get; set; }
    public double Sum { get; set; }
    public Dictionary<double, long> Buckets { get; set; } = new();
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 计算平均值
    /// </summary>
    public double Average => Count > 0 ? Sum / Count : 0;

    /// <summary>
    /// 计算分位数
    /// </summary>
    /// <param name="quantile">分位数 (0.0 - 1.0)</param>
    /// <returns>分位数值</returns>
    public double GetQuantile(double quantile)
    {
        if (quantile < 0 || quantile > 1)
            throw new ArgumentOutOfRangeException(nameof(quantile), "Quantile must be between 0 and 1");

        if (Count == 0)
            return 0;

        var targetCount = (long)(Count * quantile);
        long cumulativeCount = 0;

        foreach (var bucket in Buckets.OrderBy(b => b.Key))
        {
            cumulativeCount += bucket.Value;
            if (cumulativeCount >= targetCount)
            {
                return bucket.Key;
            }
        }

        return Buckets.Keys.Max();
    }
}

/// <summary>
/// 计时器快照
/// </summary>
public class TimerSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new();
    public long Count { get; set; }
    public double Sum { get; set; }
    public Dictionary<double, long> Buckets { get; set; } = new();
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 平均耗时（秒）
    /// </summary>
    public double AverageSeconds => Count > 0 ? Sum / Count : 0;

    /// <summary>
    /// 平均耗时
    /// </summary>
    public TimeSpan AverageTime => TimeSpan.FromSeconds(AverageSeconds);

    /// <summary>
    /// 获取耗时分位数
    /// </summary>
    /// <param name="quantile">分位数 (0.0 - 1.0)</param>
    /// <returns>耗时</returns>
    public TimeSpan GetQuantileTime(double quantile)
    {
        var quantileSeconds = GetQuantileSeconds(quantile);
        return TimeSpan.FromSeconds(quantileSeconds);
    }

    /// <summary>
    /// 获取耗时分位数（秒）
    /// </summary>
    /// <param name="quantile">分位数 (0.0 - 1.0)</param>
    /// <returns>耗时（秒）</returns>
    public double GetQuantileSeconds(double quantile)
    {
        if (quantile < 0 || quantile > 1)
            throw new ArgumentOutOfRangeException(nameof(quantile), "Quantile must be between 0 and 1");

        if (Count == 0)
            return 0;

        var targetCount = (long)(Count * quantile);
        long cumulativeCount = 0;

        foreach (var bucket in Buckets.OrderBy(b => b.Key))
        {
            cumulativeCount += bucket.Value;
            if (cumulativeCount >= targetCount)
            {
                return bucket.Key;
            }
        }

        return Buckets.Keys.Max();
    }
}
