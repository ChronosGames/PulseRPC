using System.Threading;

namespace PulseRPC.Diagnostics;

/// <summary>
/// 固定内存、线程安全的累计延迟直方图。桶宽最大相对误差约为 5%，覆盖 100ns 到 10 分钟。
/// </summary>
internal sealed class LatencyHistogram
{
    private static readonly long[] s_upperBounds = CreateUpperBounds();
    private readonly long[] _counts = new long[s_upperBounds.Length];
    private long _count;
    private long _totalTicks;
    private long _minTicks = long.MaxValue;
    private long _maxTicks;

    public void Record(TimeSpan latency)
    {
        if (latency < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(latency));

        var ticks = latency.Ticks;
        var index = Array.BinarySearch(s_upperBounds, ticks);
        if (index < 0)
            index = ~index;
        if (index >= _counts.Length)
            index = _counts.Length - 1;

        Interlocked.Increment(ref _counts[index]);
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _totalTicks, ticks);
        UpdateMinimum(ticks);
        UpdateMaximum(ticks);
    }

    public LatencyHistogramSnapshot GetSnapshot()
    {
        var counts = new long[_counts.Length];
        for (var index = 0; index < counts.Length; index++)
            counts[index] = Interlocked.Read(ref _counts[index]);

        var count = Interlocked.Read(ref _count);
        var min = Interlocked.Read(ref _minTicks);
        return new LatencyHistogramSnapshot(
            s_upperBounds,
            counts,
            count,
            Interlocked.Read(ref _totalTicks),
            count == 0 || min == long.MaxValue ? 0 : min,
            Interlocked.Read(ref _maxTicks));
    }

    public void Merge(LatencyHistogramSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (!snapshot.HasCompatibleBounds(s_upperBounds))
            throw new ArgumentException("Histogram bucket bounds are incompatible.", nameof(snapshot));

        for (var index = 0; index < _counts.Length; index++)
            Interlocked.Add(ref _counts[index], snapshot.BucketCounts[index]);
        Interlocked.Add(ref _count, snapshot.Count);
        Interlocked.Add(ref _totalTicks, snapshot.TotalTicks);
        if (snapshot.Count > 0)
        {
            UpdateMinimum(snapshot.MinTicks);
            UpdateMaximum(snapshot.MaxTicks);
        }
    }

    public void Reset()
    {
        for (var index = 0; index < _counts.Length; index++)
            Interlocked.Exchange(ref _counts[index], 0);
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _totalTicks, 0);
        Interlocked.Exchange(ref _minTicks, long.MaxValue);
        Interlocked.Exchange(ref _maxTicks, 0);
    }

    private void UpdateMinimum(long value)
    {
        var current = Interlocked.Read(ref _minTicks);
        while (value < current)
        {
            var observed = Interlocked.CompareExchange(ref _minTicks, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private void UpdateMaximum(long value)
    {
        var current = Interlocked.Read(ref _maxTicks);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref _maxTicks, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private static long[] CreateUpperBounds()
    {
        const long maxTicks = TimeSpan.TicksPerMinute * 10;
        var bounds = new List<long> { 0, 1 };
        var current = 1L;
        while (current < maxTicks)
        {
            var next = Math.Max(current + 1, (long)Math.Ceiling(current * 1.05));
            current = Math.Min(next, maxTicks);
            bounds.Add(current);
        }
        bounds.Add(long.MaxValue);
        return bounds.ToArray();
    }
}

internal sealed class LatencyHistogramSnapshot
{
    public long[] UpperBoundsTicks { get; }
    public long[] BucketCounts { get; }
    public long Count { get; }
    public long TotalTicks { get; }
    public long MinTicks { get; }
    public long MaxTicks { get; }
    public double AverageMilliseconds => Count == 0
        ? 0
        : TimeSpan.FromTicks(TotalTicks / Count).TotalMilliseconds;

    public LatencyHistogramSnapshot(
        long[] upperBoundsTicks,
        long[] bucketCounts,
        long count,
        long totalTicks,
        long minTicks,
        long maxTicks)
    {
        UpperBoundsTicks = upperBoundsTicks;
        BucketCounts = bucketCounts;
        Count = count;
        TotalTicks = totalTicks;
        MinTicks = minTicks;
        MaxTicks = maxTicks;
    }

    public double GetPercentileMilliseconds(double percentile)
    {
        if (percentile <= 0 || percentile > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile));
        if (Count == 0)
            return 0;
        if (percentile == 1)
            return TimeSpan.FromTicks(MaxTicks).TotalMilliseconds;

        var rank = Math.Max(1, (long)Math.Ceiling(Count * percentile));
        long cumulative = 0;
        for (var index = 0; index < BucketCounts.Length; index++)
        {
            cumulative += BucketCounts[index];
            if (cumulative >= rank)
            {
                var upperTicks = UpperBoundsTicks[index] == long.MaxValue
                    ? MaxTicks
                    : UpperBoundsTicks[index];
                return TimeSpan.FromTicks(upperTicks).TotalMilliseconds;
            }
        }
        return TimeSpan.FromTicks(MaxTicks).TotalMilliseconds;
    }

    public bool HasCompatibleBounds(long[] bounds)
        => UpperBoundsTicks.AsSpan().SequenceEqual(bounds);
}
