using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 优先级调度器性能指标
/// </summary>
public sealed class PrioritySchedulerMetrics
{
    // 调度指标
    private readonly Counter<long> _criticalTasksScheduled;
    private readonly Counter<long> _normalTasksScheduled;
    private readonly Counter<long> _bulkTasksScheduled;

    // 出队指标
    private readonly Counter<long> _criticalTasksDequeued;
    private readonly Counter<long> _normalTasksDequeued;
    private readonly Counter<long> _bulkTasksDequeued;

    // 完成指标
    private readonly Counter<long> _criticalTasksCompleted;
    private readonly Counter<long> _normalTasksCompleted;
    private readonly Counter<long> _bulkTasksCompleted;

    // 错误指标
    private readonly Counter<long> _criticalTasksErrored;
    private readonly Counter<long> _normalTasksErrored;
    private readonly Counter<long> _bulkTasksErrored;

    // SLA违规指标
    private readonly Counter<long> _criticalSLAViolations;
    private readonly Counter<long> _normalSLAViolations;
    private readonly Counter<long> _bulkSLAViolations;

    // 队列深度指标
    private readonly ObservableGauge<int> _criticalQueueDepth;
    private readonly ObservableGauge<int> _normalQueueDepth;
    private readonly ObservableGauge<int> _bulkQueueDepth;

    // 延迟指标
    private readonly Histogram<double> _criticalScheduleLatency;
    private readonly Histogram<double> _normalScheduleLatency;
    private readonly Histogram<double> _bulkScheduleLatency;

    // 处理时间指标
    private readonly Histogram<double> _criticalProcessingTime;
    private readonly Histogram<double> _normalProcessingTime;
    private readonly Histogram<double> _bulkProcessingTime;

    // 资源利用率指标
    private readonly ObservableGauge<double> _concurrencyUtilization;

    // 其他指标
    private readonly Counter<long> _rateLimitedTasks;
    private readonly Counter<long> _criticalQueueOverflows;

    // 内部状态
    private long _criticalQueueDepthValue;
    private long _normalQueueDepthValue;
    private long _bulkQueueDepthValue;
    private double _concurrencyUtilizationValue;

    // 吞吐量计算
    private readonly MovingWindowCounter _throughputCounter;

    static PrioritySchedulerMetrics()
    {
        var meter = new Meter("PulseRPC.Server.Scheduling");
        _meter = meter;
    }

    private static readonly Meter _meter;

    public PrioritySchedulerMetrics()
    {
        // 调度指标
        _criticalTasksScheduled = _meter.CreateCounter<long>("priority_scheduler_critical_tasks_scheduled",
            description: "关键任务调度数量");
        _normalTasksScheduled = _meter.CreateCounter<long>("priority_scheduler_normal_tasks_scheduled",
            description: "普通任务调度数量");
        _bulkTasksScheduled = _meter.CreateCounter<long>("priority_scheduler_bulk_tasks_scheduled",
            description: "批量任务调度数量");

        // 出队指标
        _criticalTasksDequeued = _meter.CreateCounter<long>("priority_scheduler_critical_tasks_dequeued",
            description: "关键任务出队数量");
        _normalTasksDequeued = _meter.CreateCounter<long>("priority_scheduler_normal_tasks_dequeued",
            description: "普通任务出队数量");
        _bulkTasksDequeued = _meter.CreateCounter<long>("priority_scheduler_bulk_tasks_dequeued",
            description: "批量任务出队数量");

        // 完成指标
        _criticalTasksCompleted = _meter.CreateCounter<long>("priority_scheduler_critical_tasks_completed",
            description: "关键任务完成数量");
        _normalTasksCompleted = _meter.CreateCounter<long>("priority_scheduler_normal_tasks_completed",
            description: "普通任务完成数量");
        _bulkTasksCompleted = _meter.CreateCounter<long>("priority_scheduler_bulk_tasks_completed",
            description: "批量任务完成数量");

        // 错误指标
        _criticalTasksErrored = _meter.CreateCounter<long>("priority_scheduler_critical_tasks_errored",
            description: "关键任务错误数量");
        _normalTasksErrored = _meter.CreateCounter<long>("priority_scheduler_normal_tasks_errored",
            description: "普通任务错误数量");
        _bulkTasksErrored = _meter.CreateCounter<long>("priority_scheduler_bulk_tasks_errored",
            description: "批量任务错误数量");

        // SLA违规指标
        _criticalSLAViolations = _meter.CreateCounter<long>("priority_scheduler_critical_sla_violations",
            description: "关键任务SLA违规数量");
        _normalSLAViolations = _meter.CreateCounter<long>("priority_scheduler_normal_sla_violations",
            description: "普通任务SLA违规数量");
        _bulkSLAViolations = _meter.CreateCounter<long>("priority_scheduler_bulk_sla_violations",
            description: "批量任务SLA违规数量");

        // 队列深度指标
        _criticalQueueDepth = _meter.CreateObservableGauge<int>("priority_scheduler_critical_queue_depth",
            () => (int)_criticalQueueDepthValue, description: "关键消息队列深度");
        _normalQueueDepth = _meter.CreateObservableGauge<int>("priority_scheduler_normal_queue_depth",
            () => (int)_normalQueueDepthValue, description: "普通消息队列深度");
        _bulkQueueDepth = _meter.CreateObservableGauge<int>("priority_scheduler_bulk_queue_depth",
            () => (int)_bulkQueueDepthValue, description: "批量消息队列深度");

        // 延迟指标
        _criticalScheduleLatency = _meter.CreateHistogram<double>("priority_scheduler_critical_schedule_latency",
            unit: "ms", description: "关键任务调度延迟");
        _normalScheduleLatency = _meter.CreateHistogram<double>("priority_scheduler_normal_schedule_latency",
            unit: "ms", description: "普通任务调度延迟");
        _bulkScheduleLatency = _meter.CreateHistogram<double>("priority_scheduler_bulk_schedule_latency",
            unit: "ms", description: "批量任务调度延迟");

        // 处理时间指标
        _criticalProcessingTime = _meter.CreateHistogram<double>("priority_scheduler_critical_processing_time",
            unit: "ms", description: "关键任务处理时间");
        _normalProcessingTime = _meter.CreateHistogram<double>("priority_scheduler_normal_processing_time",
            unit: "ms", description: "普通任务处理时间");
        _bulkProcessingTime = _meter.CreateHistogram<double>("priority_scheduler_bulk_processing_time",
            unit: "ms", description: "批量任务处理时间");

        // 资源利用率指标
        _concurrencyUtilization = _meter.CreateObservableGauge<double>("priority_scheduler_concurrency_utilization",
            () => _concurrencyUtilizationValue, description: "并发利用率");

        // 其他指标
        _rateLimitedTasks = _meter.CreateCounter<long>("priority_scheduler_rate_limited_tasks",
            description: "被速率限制的任务数量");
        _criticalQueueOverflows = _meter.CreateCounter<long>("priority_scheduler_critical_queue_overflows",
            description: "关键队列溢出次数");

        // 吞吐量计算
        _throughputCounter = new MovingWindowCounter(TimeSpan.FromSeconds(10));
    }

    // 调度指标属性
    public Counter<long> CriticalTasksScheduled => _criticalTasksScheduled;
    public Counter<long> NormalTasksScheduled => _normalTasksScheduled;
    public Counter<long> BulkTasksScheduled => _bulkTasksScheduled;

    // 出队指标属性
    public Counter<long> CriticalTasksDequeued => _criticalTasksDequeued;
    public Counter<long> NormalTasksDequeued => _normalTasksDequeued;
    public Counter<long> BulkTasksDequeued => _bulkTasksDequeued;

    // 完成指标属性
    public Counter<long> CriticalTasksCompleted => _criticalTasksCompleted;
    public Counter<long> NormalTasksCompleted => _normalTasksCompleted;
    public Counter<long> BulkTasksCompleted => _bulkTasksCompleted;

    // 错误指标属性
    public Counter<long> CriticalTasksErrored => _criticalTasksErrored;
    public Counter<long> NormalTasksErrored => _normalTasksErrored;
    public Counter<long> BulkTasksErrored => _bulkTasksErrored;

    // SLA违规指标属性
    public Counter<long> CriticalSLAViolations => _criticalSLAViolations;
    public Counter<long> NormalSLAViolations => _normalSLAViolations;
    public Counter<long> BulkSLAViolations => _bulkSLAViolations;

    // 延迟指标属性
    public Histogram<double> CriticalScheduleLatency => _criticalScheduleLatency;
    public Histogram<double> NormalScheduleLatency => _normalScheduleLatency;
    public Histogram<double> BulkScheduleLatency => _bulkScheduleLatency;

    // 处理时间指标属性
    public Histogram<double> CriticalProcessingTime => _criticalProcessingTime;
    public Histogram<double> NormalProcessingTime => _normalProcessingTime;
    public Histogram<double> BulkProcessingTime => _bulkProcessingTime;

    // 其他指标属性
    public Counter<long> RateLimitedTasks => _rateLimitedTasks;
    public Counter<long> CriticalQueueOverflows => _criticalQueueOverflows;

    // 队列深度属性（兼容性）
    public int CriticalQueueDepth => (int)_criticalQueueDepthValue;
    public int NormalQueueDepth => (int)_normalQueueDepthValue;
    public int BulkQueueDepth => (int)_bulkQueueDepthValue;

    // 并发利用率属性（兼容性）
    public double ConcurrencyUtilization => _concurrencyUtilizationValue;

    // 设置队列深度
    public void SetCriticalQueueDepth(int depth) =>
        Interlocked.Exchange(ref _criticalQueueDepthValue, depth);

    public void SetNormalQueueDepth(int depth) =>
        Interlocked.Exchange(ref _normalQueueDepthValue, depth);

    public void SetBulkQueueDepth(int depth) =>
        Interlocked.Exchange(ref _bulkQueueDepthValue, depth);

    // 设置并发利用率
    public void SetConcurrencyUtilization(double utilization) =>
        Interlocked.Exchange(ref _concurrencyUtilizationValue, utilization);

    // 获取总任务数
    public long GetTotalTasksScheduled()
    {
        // 注意：Counter<T>不直接提供当前值的访问，这里使用内部计数器
        return _criticalTasksScheduledCount + _normalTasksScheduledCount + _bulkTasksScheduledCount;
    }

    public long GetTotalTasksCompleted()
    {
        return _criticalTasksCompletedCount + _normalTasksCompletedCount + _bulkTasksCompletedCount;
    }

    // 获取当前吞吐量
    public double GetCurrentThroughput()
    {
        return _throughputCounter.GetRate();
    }

    // 记录任务完成（用于吞吐量计算）
    public void RecordTaskCompletion()
    {
        _throughputCounter.Increment();
    }

    // 内部计数器（用于统计）
    private long _criticalTasksScheduledCount;
    private long _normalTasksScheduledCount;
    private long _bulkTasksScheduledCount;
    private long _criticalTasksCompletedCount;
    private long _normalTasksCompletedCount;
    private long _bulkTasksCompletedCount;

    // 扩展方法，用于增加计数器并更新内部计数
    public void IncrementCriticalTasksScheduled()
    {
        _criticalTasksScheduled.Add(1);
        Interlocked.Increment(ref _criticalTasksScheduledCount);
    }

    public void IncrementNormalTasksScheduled()
    {
        _normalTasksScheduled.Add(1);
        Interlocked.Increment(ref _normalTasksScheduledCount);
    }

    public void IncrementBulkTasksScheduled()
    {
        _bulkTasksScheduled.Add(1);
        Interlocked.Increment(ref _bulkTasksScheduledCount);
    }

    public void IncrementCriticalTasksCompleted()
    {
        _criticalTasksCompleted.Add(1);
        Interlocked.Increment(ref _criticalTasksCompletedCount);
        RecordTaskCompletion();
    }

    public void IncrementNormalTasksCompleted()
    {
        _normalTasksCompleted.Add(1);
        Interlocked.Increment(ref _normalTasksCompletedCount);
        RecordTaskCompletion();
    }

    public void IncrementBulkTasksCompleted()
    {
        _bulkTasksCompleted.Add(1);
        Interlocked.Increment(ref _bulkTasksCompletedCount);
        RecordTaskCompletion();
    }
}
