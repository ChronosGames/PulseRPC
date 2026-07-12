using System.Collections.Concurrent;
using System.Diagnostics;
using PulseRPC.Diagnostics;

namespace PulseRPC.Server.Extensions;

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
    private readonly LatencyHistogram _latencyHistogram = new();

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

        if (durationMs < 0 || double.IsNaN(durationMs) || double.IsInfinity(durationMs))
            throw new ArgumentOutOfRangeException(nameof(durationMs));
        _latencyHistogram.Record(TimeSpan.FromMilliseconds(durationMs));
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

        var latency = _latencyHistogram.GetSnapshot();
        return new PipelineMetrics
        {
            TotalRequests = totalRequests,
            TotalErrors = totalErrors,
            TotalTimeouts = totalTimeouts,
            ActiveRequests = activeRequests,
            ActiveConnections = activeConnections,
            RequestsPerSecond = requestsPerSecond,
            ErrorRate = totalRequests > 0 ? (double)totalErrors / totalRequests : 0,
            LatencyCount = latency.Count,
            LatencyAverage = latency.AverageMilliseconds,
            LatencyMin = TimeSpan.FromTicks(latency.MinTicks).TotalMilliseconds,
            LatencyMax = TimeSpan.FromTicks(latency.MaxTicks).TotalMilliseconds,
            LatencyP50 = latency.GetPercentileMilliseconds(0.50),
            LatencyP75 = latency.GetPercentileMilliseconds(0.75),
            LatencyP95 = latency.GetPercentileMilliseconds(0.95),
            LatencyP99 = latency.GetPercentileMilliseconds(0.99),
            L1QueueDepth = Interlocked.Read(ref _l1QueueDepth),
            L2QueueDepth = Interlocked.Read(ref _l2QueueDepth),
            L3QueueDepth = Interlocked.Read(ref _l3QueueDepth),
            Queues = RuntimeQueueMetrics.GetSnapshots(),
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

        var histogram = _latencyHistogram.GetSnapshot();
        sb.AppendLine("# HELP pulserpc_latency_ms RPC latency histogram");
        sb.AppendLine("# TYPE pulserpc_latency_ms histogram");
        long cumulative = 0;
        for (var index = 0; index < histogram.BucketCounts.Length; index++)
        {
            if (histogram.UpperBoundsTicks[index] == long.MaxValue)
                break;
            cumulative += histogram.BucketCounts[index];
            var upperMs = TimeSpan.FromTicks(histogram.UpperBoundsTicks[index]).TotalMilliseconds;
            sb.AppendLine($"pulserpc_latency_ms_bucket{{le=\"{upperMs:R}\"}} {cumulative}");
        }
        sb.AppendLine($"pulserpc_latency_ms_bucket{{le=\"+Inf\"}} {histogram.Count}");
        sb.AppendLine($"pulserpc_latency_ms_sum {TimeSpan.FromTicks(histogram.TotalTicks).TotalMilliseconds:R}");
        sb.AppendLine($"pulserpc_latency_ms_count {histogram.Count}");

        foreach (var queue in metrics.Queues)
        {
            var name = queue.QueueName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var instance = queue.InstanceId.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var labels = $"queue=\"{name}\",instance=\"{instance}\"";
            sb.AppendLine($"pulserpc_queue_capacity{{{labels}}} {queue.Capacity}");
            sb.AppendLine($"pulserpc_queue_depth{{{labels}}} {queue.Depth}");
            sb.AppendLine($"pulserpc_queue_saturation{{{labels}}} {queue.Saturation:R}");
            sb.AppendLine($"pulserpc_queue_high_watermark{{{labels}}} {queue.HighWatermark}");
            sb.AppendLine($"pulserpc_queue_saturation_events_total{{{labels}}} {queue.SaturationEvents}");
            sb.AppendLine($"pulserpc_queue_enqueue_wait_total{{{labels}}} {queue.EnqueueWaitCount}");
            sb.AppendLine($"pulserpc_queue_rejected_total{{{labels}}} {queue.RejectedEnqueues}");
        }

        return sb.ToString();
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
        _latencyHistogram.Reset();
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
    public long LatencyCount { get; init; }
    public double LatencyAverage { get; init; }
    public double LatencyMin { get; init; }
    public double LatencyMax { get; init; }
    public double LatencyP50 { get; init; }
    public double LatencyP75 { get; init; }
    public double LatencyP95 { get; init; }
    public double LatencyP99 { get; init; }
    public long L1QueueDepth { get; init; }
    public long L2QueueDepth { get; init; }
    public long L3QueueDepth { get; init; }
    public IReadOnlyList<RuntimeQueueMetricsSnapshot> Queues { get; init; } = Array.Empty<RuntimeQueueMetricsSnapshot>();
    public double CpuUsagePercent { get; init; }
    public long MemoryUsageMB { get; init; }
}
