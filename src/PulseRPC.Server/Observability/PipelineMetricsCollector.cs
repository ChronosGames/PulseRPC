using System.Collections.Concurrent;
using System.Diagnostics;

namespace PulseRPC.Server.Observability;

/// <summary>
/// Collects pipeline metrics with minimal hot-path overhead (&lt;1% impact).
/// Tracks: throughput, error rate, latency percentiles, active connections, queue depths.
/// </summary>
public sealed class PipelineMetricsCollector
{
    // Counters (thread-safe via Interlocked)
    private long _totalRequests;
    private long _totalErrors;
    private long _totalTimeouts;
    private long _activeRequests;
    private long _activeConnections;

    // Latency tracking (lock-free histogram)
    private readonly ConcurrentDictionary<int, long> _latencyBuckets = new();

    // Queue depth tracking
    private long _l1QueueDepth;
    private long _l2QueueDepth;
    private long _l3QueueDepth;

    // Throughput calculation
    private long _lastRequestCount;
    private DateTime _lastResetTime = DateTime.UtcNow;
    private readonly Lock _throughputLock = new();

    /// <summary>
    /// Records a request start.
    /// </summary>
    public void RecordRequestStart()
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _activeRequests);
    }

    /// <summary>
    /// Records a request completion.
    /// </summary>
    public void RecordRequestComplete(double durationMs, bool isError = false, bool isTimeout = false)
    {
        Interlocked.Decrement(ref _activeRequests);

        if (isError)
            Interlocked.Increment(ref _totalErrors);

        if (isTimeout)
            Interlocked.Increment(ref _totalTimeouts);

        // Record latency in histogram buckets
        RecordLatency(durationMs);
    }

    /// <summary>
    /// Records a connection event.
    /// </summary>
    public void RecordConnectionChange(int delta)
    {
        Interlocked.Add(ref _activeConnections, delta);
    }

    /// <summary>
    /// Updates queue depth metrics.
    /// </summary>
    public void UpdateQueueDepths(int l1Depth, int l2Depth, int l3Depth)
    {
        Interlocked.Exchange(ref _l1QueueDepth, l1Depth);
        Interlocked.Exchange(ref _l2QueueDepth, l2Depth);
        Interlocked.Exchange(ref _l3QueueDepth, l3Depth);
    }

    /// <summary>
    /// Gets current metrics snapshot.
    /// </summary>
    public PipelineMetrics GetSnapshot()
    {
        var totalRequests = Interlocked.Read(ref _totalRequests);
        var totalErrors = Interlocked.Read(ref _totalErrors);
        var totalTimeouts = Interlocked.Read(ref _totalTimeouts);
        var activeRequests = Interlocked.Read(ref _activeRequests);
        var activeConnections = Interlocked.Read(ref _activeConnections);

        // Calculate throughput
        double requestsPerSecond;
        using (_throughputLock.EnterScope())
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastResetTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                var currentRequests = totalRequests;
                requestsPerSecond = (currentRequests - _lastRequestCount) / elapsed;
                _lastRequestCount = currentRequests;
                _lastResetTime = now;
            }
            else
            {
                requestsPerSecond = 0;
            }
        }

        return new PipelineMetrics
        {
            TotalRequests = totalRequests,
            TotalErrors = totalErrors,
            TotalTimeouts = totalTimeouts,
            ActiveRequests = activeRequests,
            ActiveConnections = activeConnections,
            RequestsPerSecond = requestsPerSecond,
            ErrorRate = totalRequests > 0 ? (double)totalErrors / totalRequests : 0,
            LatencyP50 = CalculatePercentile(0.50),
            LatencyP75 = CalculatePercentile(0.75),
            LatencyP95 = CalculatePercentile(0.95),
            LatencyP99 = CalculatePercentile(0.99),
            L1QueueDepth = Interlocked.Read(ref _l1QueueDepth),
            L2QueueDepth = Interlocked.Read(ref _l2QueueDepth),
            L3QueueDepth = Interlocked.Read(ref _l3QueueDepth),
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsageMB = GetMemoryUsage()
        };
    }

    /// <summary>
    /// Exports metrics in Prometheus format.
    /// </summary>
    public string ExportPrometheus()
    {
        var metrics = GetSnapshot();
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# HELP pulserpc_requests_total Total number of RPC requests");
        sb.AppendLine("# TYPE pulserpc_requests_total counter");
        sb.AppendLine($"pulserpc_requests_total {metrics.TotalRequests}");

        sb.AppendLine("# HELP pulserpc_errors_total Total number of errors");
        sb.AppendLine("# TYPE pulserpc_errors_total counter");
        sb.AppendLine($"pulserpc_errors_total {metrics.TotalErrors}");

        sb.AppendLine("# HELP pulserpc_requests_per_second Current requests per second");
        sb.AppendLine("# TYPE pulserpc_requests_per_second gauge");
        sb.AppendLine($"pulserpc_requests_per_second {metrics.RequestsPerSecond:F2}");

        sb.AppendLine("# HELP pulserpc_active_requests Currently processing requests");
        sb.AppendLine("# TYPE pulserpc_active_requests gauge");
        sb.AppendLine($"pulserpc_active_requests {metrics.ActiveRequests}");

        sb.AppendLine("# HELP pulserpc_active_connections Currently active connections");
        sb.AppendLine("# TYPE pulserpc_active_connections gauge");
        sb.AppendLine($"pulserpc_active_connections {metrics.ActiveConnections}");

        sb.AppendLine("# HELP pulserpc_latency_p95_ms 95th percentile latency in milliseconds");
        sb.AppendLine("# TYPE pulserpc_latency_p95_ms gauge");
        sb.AppendLine($"pulserpc_latency_p95_ms {metrics.LatencyP95:F2}");

        sb.AppendLine("# HELP pulserpc_latency_p99_ms 99th percentile latency in milliseconds");
        sb.AppendLine("# TYPE pulserpc_latency_p99_ms gauge");
        sb.AppendLine($"pulserpc_latency_p99_ms {metrics.LatencyP99:F2}");

        return sb.ToString();
    }

    private void RecordLatency(double durationMs)
    {
        // Use logarithmic buckets: 0-1ms, 1-2ms, 2-5ms, 5-10ms, 10-20ms, 20-50ms, 50-100ms, 100+ms
        int bucket = durationMs switch
        {
            < 1 => 0,
            < 2 => 1,
            < 5 => 2,
            < 10 => 5,
            < 20 => 10,
            < 50 => 20,
            < 100 => 50,
            _ => 100
        };

        _latencyBuckets.AddOrUpdate(bucket, 1, (_, count) => count + 1);
    }

    private double CalculatePercentile(double percentile)
    {
        // Simplified percentile calculation from histogram
        // For production, consider using HdrHistogram library
        var totalCount = _latencyBuckets.Values.Sum();
        if (totalCount == 0) return 0;

        var targetCount = (long)(totalCount * percentile);
        long runningCount = 0;

        foreach (var bucket in _latencyBuckets.OrderBy(kvp => kvp.Key))
        {
            runningCount += bucket.Value;
            if (runningCount >= targetCount)
            {
                return bucket.Key;
            }
        }

        return 100; // Max bucket
    }

    private double GetCpuUsage()
    {
        // Simplified CPU usage - for production, use PerformanceCounter or similar
        try
        {
            using var process = Process.GetCurrentProcess();
            var cpuTime = process.TotalProcessorTime;
            var wallTime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
            var processorCount = Environment.ProcessorCount;

            if (wallTime.TotalMilliseconds > 0)
            {
                return (cpuTime.TotalMilliseconds / wallTime.TotalMilliseconds / processorCount) * 100;
            }
        }
        catch
        {
            // Ignore errors in metrics collection
        }

        return 0;
    }

    private long GetMemoryUsage()
    {
        return GC.GetTotalMemory(false) / (1024 * 1024); // Convert to MB
    }

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _totalErrors, 0);
        Interlocked.Exchange(ref _totalTimeouts, 0);
        Interlocked.Exchange(ref _activeRequests, 0);
        Interlocked.Exchange(ref _activeConnections, 0);
        _latencyBuckets.Clear();
        _lastRequestCount = 0;
        _lastResetTime = DateTime.UtcNow;
    }
}

/// <summary>
/// Snapshot of pipeline metrics.
/// </summary>
public sealed class PipelineMetrics
{
    public long TotalRequests { get; init; }
    public long TotalErrors { get; init; }
    public long TotalTimeouts { get; init; }
    public long ActiveRequests { get; init; }
    public long ActiveConnections { get; init; }
    public double RequestsPerSecond { get; init; }
    public double ErrorRate { get; init; }
    public double LatencyP50 { get; init; }
    public double LatencyP75 { get; init; }
    public double LatencyP95 { get; init; }
    public double LatencyP99 { get; init; }
    public long L1QueueDepth { get; init; }
    public long L2QueueDepth { get; init; }
    public long L3QueueDepth { get; init; }
    public double CpuUsagePercent { get; init; }
    public long MemoryUsageMB { get; init; }
}
