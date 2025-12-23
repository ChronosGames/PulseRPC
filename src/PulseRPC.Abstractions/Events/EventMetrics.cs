using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace PulseRPC.Client.Events;

/// <summary>
/// 事件指标收集器 - 线程安全的性能监控
/// </summary>
public sealed class EventMetrics
{
    private readonly ConcurrentDictionary<string, EventMethodMetrics> _methodMetrics = new();
    private long _totalEventsReceived;
    private long _totalEventsProcessed;
    private long _totalErrors;
    private long _totalBatchesProcessed;
    private long _totalBatchErrors;

    public void RecordEventReceived(string methodName)
    {
        Interlocked.Increment(ref _totalEventsReceived);
        var metrics = _methodMetrics.GetOrAdd(methodName, _ => new EventMethodMetrics());
        Interlocked.Increment(ref metrics.CallCount);
    }

    public void RecordEventProcessed(string methodName, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalEventsProcessed);
        var metrics = _methodMetrics.GetOrAdd(methodName, _ => new EventMethodMetrics());
        Interlocked.Increment(ref metrics.SuccessCount);

        // 更新延迟统计
        var durationMs = duration.TotalMilliseconds;
        lock (metrics)
        {
            metrics.TotalDuration += durationMs;
            metrics.MaxDuration = Math.Max(metrics.MaxDuration, durationMs);
            metrics.MinDuration = metrics.MinDuration == 0 ? durationMs : Math.Min(metrics.MinDuration, durationMs);
        }
    }

    public void RecordEventError(string methodName, Exception exception, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalErrors);
        var metrics = _methodMetrics.GetOrAdd(methodName, _ => new EventMethodMetrics());
        Interlocked.Increment(ref metrics.ErrorCount);

        lock (metrics)
        {
            metrics.LastError = exception;
            metrics.LastErrorTime = DateTime.UtcNow;
        }
    }

    public void RecordBatchProcessed(int batchSize, int successCount, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalBatchesProcessed);
        Interlocked.Add(ref _totalEventsProcessed, successCount);

        if (successCount < batchSize)
        {
            Interlocked.Add(ref _totalErrors, batchSize - successCount);
        }
    }

    public void RecordBatchProcessingError(Exception exception)
    {
        Interlocked.Increment(ref _totalBatchErrors);
    }

    public void RecordSubscriptionError(Exception exception)
    {
        Interlocked.Increment(ref _totalErrors);
    }

    public EventHandlerMetrics GetSnapshot()
    {
        var methodSnapshots = new Dictionary<string, MethodMetrics>();

        foreach (var kvp in _methodMetrics)
        {
            var method = kvp.Value;
            lock (method)
            {
                methodSnapshots[kvp.Key] = new MethodMetrics
                {
                    CallCount = method.CallCount,
                    SuccessCount = method.SuccessCount,
                    ErrorCount = method.ErrorCount,
                    AverageDuration = method.SuccessCount > 0 ? method.TotalDuration / method.SuccessCount : 0,
                    MaxDuration = method.MaxDuration,
                    MinDuration = method.MinDuration,
                    LastError = method.LastError,
                    LastErrorTime = method.LastErrorTime
                };
            }
        }

        return new EventHandlerMetrics
        {
            TotalEventsReceived = _totalEventsReceived,
            TotalEventsProcessed = _totalEventsProcessed,
            TotalErrors = _totalErrors,
            TotalBatchesProcessed = _totalBatchesProcessed,
            TotalBatchErrors = _totalBatchErrors,
            MethodMetrics = methodSnapshots
        };
    }

    private sealed class EventMethodMetrics
    {
        public long CallCount;
        public long SuccessCount;
        public long ErrorCount;
        public double TotalDuration;
        public double MaxDuration;
        public double MinDuration;
        public Exception? LastError;
        public DateTime LastErrorTime;
    }
}

/// <summary>
/// 事件处理器性能指标快照
/// </summary>
public sealed class EventHandlerMetrics
{
    public long TotalEventsReceived { get; set; }
    public long TotalEventsProcessed { get; set; }
    public long TotalErrors { get; set; }
    public long TotalBatchesProcessed { get; set; }
    public long TotalBatchErrors { get; set; }
    public Dictionary<string, MethodMetrics> MethodMetrics { get; set; } = new();

    public double SuccessRate => TotalEventsReceived > 0 ? (double)TotalEventsProcessed / TotalEventsReceived : 0;
    public double ErrorRate => TotalEventsReceived > 0 ? (double)TotalErrors / TotalEventsReceived : 0;
}

/// <summary>
/// 方法级性能指标
/// </summary>
public sealed class MethodMetrics
{
    public long CallCount { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
    public double AverageDuration { get; set; }
    public double MaxDuration { get; set; }
    public double MinDuration { get; set; }
    public Exception? LastError { get; set; }
    public DateTime LastErrorTime { get; set; }

    public double SuccessRate => CallCount > 0 ? (double)SuccessCount / CallCount : 0;
    public double ErrorRate => CallCount > 0 ? (double)ErrorCount / CallCount : 0;
}
