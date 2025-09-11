using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;

namespace PulseRPC.Server.Threading;

/// <summary>
/// 工作窃取处理器性能指标
/// </summary>
public sealed class WorkStealingProcessorMetrics
{
    private readonly int _workerCount;
    
    // 全局指标
    private readonly Counter<long> _tasksScheduled;
    private readonly Counter<long> _tasksCompleted;
    private readonly Counter<long> _tasksErrored;
    private readonly Counter<long> _tasksRejected;
    private readonly Counter<long> _tasksStolen;
    private readonly Counter<long> _stolenTasksCompleted;
    
    // 每个工作线程的指标
    private readonly Counter<long>[] _workerTasksEnqueued;
    private readonly Counter<long>[] _workerTasksDequeued;
    private readonly Counter<long>[] _workerTasksCompleted;
    private readonly Counter<long>[] _workerTasksErrored;
    private readonly Counter<long>[] _workerTasksStolen;
    private readonly Counter<long>[] _workerTasksStolenFrom;
    private readonly Histogram<double>[] _workerProcessingTime;
    
    // 吞吐量计算
    private readonly MovingWindowCounter _throughputCounter;
    
    // 内部计数器（用于快速访问）
    private long _tasksScheduledCount;
    private long _tasksCompletedCount;
    private long _tasksErroredCount;
    private long _tasksRejectedCount;
    private long _tasksStolenCount;
    private long _stolenTasksCompletedCount;
    
    // 内部工作线程计数器数组
    private long[] _workerTasksEnqueuedCount;
    private long[] _workerTasksDequeuedCount;
    private long[] _workerTasksCompletedCount;
    private long[] _workerTasksErroredCount;
    private long[] _workerTasksStolenCount;
    private long[] _workerTasksStolenFromCount;
    
    static WorkStealingProcessorMetrics()
    {
        var meter = new Meter("PulseRPC.Server.Threading");
        _meter = meter;
    }
    
    private static readonly Meter _meter;
    
    public WorkStealingProcessorMetrics(int workerCount)
    {
        _workerCount = workerCount;
        
        // 初始化全局指标
        _tasksScheduled = _meter.CreateCounter<long>("work_stealing_tasks_scheduled", 
            description: "调度的任务总数");
        _tasksCompleted = _meter.CreateCounter<long>("work_stealing_tasks_completed", 
            description: "完成的任务总数");
        _tasksErrored = _meter.CreateCounter<long>("work_stealing_tasks_errored", 
            description: "出错的任务总数");
        _tasksRejected = _meter.CreateCounter<long>("work_stealing_tasks_rejected", 
            description: "被拒绝的任务总数");
        _tasksStolen = _meter.CreateCounter<long>("work_stealing_tasks_stolen", 
            description: "被窃取的任务总数");
        _stolenTasksCompleted = _meter.CreateCounter<long>("work_stealing_stolen_tasks_completed", 
            description: "完成的被窃取任务总数");
        
        // 初始化每个工作线程的指标
        _workerTasksEnqueued = new Counter<long>[workerCount];
        _workerTasksDequeued = new Counter<long>[workerCount];
        _workerTasksCompleted = new Counter<long>[workerCount];
        _workerTasksErrored = new Counter<long>[workerCount];
        _workerTasksStolen = new Counter<long>[workerCount];
        _workerTasksStolenFrom = new Counter<long>[workerCount];
        _workerProcessingTime = new Histogram<double>[workerCount];
        
        // 初始化内部计数器数组
        _workerTasksEnqueuedCount = new long[workerCount];
        _workerTasksDequeuedCount = new long[workerCount];
        _workerTasksCompletedCount = new long[workerCount];
        _workerTasksErroredCount = new long[workerCount];
        _workerTasksStolenCount = new long[workerCount];
        _workerTasksStolenFromCount = new long[workerCount];
        
        for (int i = 0; i < workerCount; i++)
        {
            var workerId = i;
            var workerTag = new KeyValuePair<string, object?>("worker_id", workerId);
            
            _workerTasksEnqueued[i] = _meter.CreateCounter<long>($"work_stealing_worker_tasks_enqueued", 
                description: "工作线程入队任务数");
            _workerTasksDequeued[i] = _meter.CreateCounter<long>($"work_stealing_worker_tasks_dequeued", 
                description: "工作线程出队任务数");
            _workerTasksCompleted[i] = _meter.CreateCounter<long>($"work_stealing_worker_tasks_completed", 
                description: "工作线程完成任务数");
            _workerTasksErrored[i] = _meter.CreateCounter<long>($"work_stealing_worker_tasks_errored", 
                description: "工作线程错误任务数");
            _workerTasksStolen[i] = _meter.CreateCounter<long>($"work_stealing_worker_tasks_stolen", 
                description: "工作线程窃取的任务数");
            _workerTasksStolenFrom[i] = _meter.CreateCounter<long>($"work_stealing_worker_tasks_stolen_from", 
                description: "工作线程被窃取的任务数");
            _workerProcessingTime[i] = _meter.CreateHistogram<double>($"work_stealing_worker_processing_time", 
                unit: "ms", description: "工作线程任务处理时间");
        }
        
        // 初始化吞吐量计算
        _throughputCounter = new MovingWindowCounter(TimeSpan.FromSeconds(10));
    }
    
    // 全局指标属性
    public Counter<long> TasksScheduled => _tasksScheduled;
    public Counter<long> TasksCompleted => _tasksCompleted;
    public Counter<long> TasksErrored => _tasksErrored;
    public Counter<long> TasksRejected => _tasksRejected;
    public Counter<long> TasksStolen => _tasksStolen;
    public Counter<long> StolenTasksCompleted => _stolenTasksCompleted;
    
    // 工作线程指标属性
    public Counter<long>[] WorkerTasksEnqueued => _workerTasksEnqueued;
    public Counter<long>[] WorkerTasksDequeued => _workerTasksDequeued;
    public Counter<long>[] WorkerTasksCompleted => _workerTasksCompleted;
    public Counter<long>[] WorkerTasksErrored => _workerTasksErrored;
    public Counter<long>[] WorkerTasksStolen => _workerTasksStolen;
    public Counter<long>[] WorkerTasksStolenFrom => _workerTasksStolenFrom;
    public Histogram<double>[] WorkerProcessingTime => _workerProcessingTime;
    
    // 快速访问属性（用于内部计数器）
    public long TasksScheduledCount => _tasksScheduledCount;
    public long TasksCompletedCount => _tasksCompletedCount;
    public long TasksErroredCount => _tasksErroredCount;
    public long TasksRejectedCount => _tasksRejectedCount;
    public long TasksStolenCount => _tasksStolenCount;
    public long StolenTasksCompletedCount => _stolenTasksCompletedCount;
    
    // 扩展方法，用于增加计数器并更新内部计数
    public void IncrementTasksScheduled()
    {
        _tasksScheduled.Add(1);
        Interlocked.Increment(ref _tasksScheduledCount);
    }
    
    public void IncrementTasksCompleted()
    {
        _tasksCompleted.Add(1);
        Interlocked.Increment(ref _tasksCompletedCount);
        _throughputCounter.Increment();
    }
    
    public void IncrementTasksErrored()
    {
        _tasksErrored.Add(1);
        Interlocked.Increment(ref _tasksErroredCount);
    }
    
    public void IncrementTasksRejected()
    {
        _tasksRejected.Add(1);
        Interlocked.Increment(ref _tasksRejectedCount);
    }
    
    public void IncrementTasksStolen()
    {
        _tasksStolen.Add(1);
        Interlocked.Increment(ref _tasksStolenCount);
    }
    
    public void IncrementStolenTasksCompleted()
    {
        _stolenTasksCompleted.Add(1);
        Interlocked.Increment(ref _stolenTasksCompletedCount);
    }
    
    // 工作线程特定的增量方法
    public void IncrementWorkerTasksEnqueued(int workerId)
    {
        if (workerId >= 0 && workerId < _workerCount)
        {
            _workerTasksEnqueued[workerId].Add(1);
            Interlocked.Increment(ref _workerTasksEnqueuedCount[workerId]);
        }
    }
    
    public void IncrementWorkerTasksDequeued(int workerId)
    {
        if (workerId >= 0 && workerId < _workerCount)
        {
            _workerTasksDequeued[workerId].Add(1);
            Interlocked.Increment(ref _workerTasksDequeuedCount[workerId]);
        }
    }
    
    public void IncrementWorkerTasksCompleted(int workerId)
    {
        if (workerId >= 0 && workerId < _workerCount)
        {
            _workerTasksCompleted[workerId].Add(1);
            Interlocked.Increment(ref _workerTasksCompletedCount[workerId]);
        }
    }
    
    public void IncrementWorkerTasksErrored(int workerId)
    {
        if (workerId >= 0 && workerId < _workerCount)
        {
            _workerTasksErrored[workerId].Add(1);
            Interlocked.Increment(ref _workerTasksErroredCount[workerId]);
        }
    }
    
    public void IncrementWorkerTasksStolen(int workerId)
    {
        if (workerId >= 0 && workerId < _workerCount)
        {
            _workerTasksStolen[workerId].Add(1);
            Interlocked.Increment(ref _workerTasksStolenCount[workerId]);
        }
    }
    
    public void IncrementWorkerTasksStolenFrom(int workerId)
    {
        if (workerId >= 0 && workerId < _workerCount)
        {
            _workerTasksStolenFrom[workerId].Add(1);
            Interlocked.Increment(ref _workerTasksStolenFromCount[workerId]);
        }
    }
    
    // 获取当前吞吐量
    public double GetCurrentThroughput()
    {
        return _throughputCounter.GetRate();
    }
    
    // 获取工作负载均衡统计
    public WorkloadBalanceStatistics GetWorkloadBalanceStatistics()
    {
        var workerLoads = new long[_workerCount];
        for (int i = 0; i < _workerCount; i++)
        {
            workerLoads[i] = _workerTasksCompletedCount[i];
        }
        
        // 计算统计信息
        long min = long.MaxValue;
        long max = long.MinValue;
        long sum = 0;
        
        for (int i = 0; i < workerLoads.Length; i++)
        {
            var load = workerLoads[i];
            min = Math.Min(min, load);
            max = Math.Max(max, load);
            sum += load;
        }
        
        double average = workerLoads.Length > 0 ? (double)sum / workerLoads.Length : 0;
        double loadImbalance = max > 0 ? (double)(max - min) / max : 0;
        
        // 计算标准差
        double variance = 0;
        if (workerLoads.Length > 1)
        {
            for (int i = 0; i < workerLoads.Length; i++)
            {
                var diff = workerLoads[i] - average;
                variance += diff * diff;
            }
            variance /= workerLoads.Length;
        }
        double standardDeviation = Math.Sqrt(variance);
        
        return new WorkloadBalanceStatistics
        {
            WorkerCount = _workerCount,
            WorkerLoads = workerLoads,
            MinLoad = min,
            MaxLoad = max,
            AverageLoad = average,
            LoadImbalance = loadImbalance,
            StandardDeviation = standardDeviation
        };
    }
    
    // 获取窃取效率统计
    public StealingEfficiencyStatistics GetStealingEfficiencyStatistics()
    {
        var totalTasksCompleted = _tasksCompletedCount;
        var totalTasksStolen = _tasksStolenCount;
        var stolenTasksCompleted = _stolenTasksCompletedCount;
        
        double stealingRate = totalTasksCompleted > 0 ? (double)totalTasksStolen / totalTasksCompleted : 0;
        double stealingEfficiency = totalTasksStolen > 0 ? (double)stolenTasksCompleted / totalTasksStolen : 0;
        
        var workerStealingStats = new WorkerStealingStats[_workerCount];
        for (int i = 0; i < _workerCount; i++)
        {
            var stolen = _workerTasksStolenCount[i];
            var stolenFrom = _workerTasksStolenFromCount[i];
            
            workerStealingStats[i] = new WorkerStealingStats
            {
                WorkerId = i,
                TasksStolen = stolen,
                TasksStolenFrom = stolenFrom,
                NetStealingBalance = stolen - stolenFrom
            };
        }
        
        return new StealingEfficiencyStatistics
        {
            TotalTasksStolen = totalTasksStolen,
            StolenTasksCompleted = stolenTasksCompleted,
            StealingRate = stealingRate,
            StealingEfficiency = stealingEfficiency,
            WorkerStealingStats = workerStealingStats
        };
    }
}

/// <summary>
/// 工作负载均衡统计
/// </summary>
public class WorkloadBalanceStatistics
{
    public int WorkerCount { get; set; }
    public required long[] WorkerLoads { get; set; }
    public long MinLoad { get; set; }
    public long MaxLoad { get; set; }
    public double AverageLoad { get; set; }
    public double LoadImbalance { get; set; }
    public double StandardDeviation { get; set; }
}

/// <summary>
/// 窃取效率统计
/// </summary>
public class StealingEfficiencyStatistics
{
    public long TotalTasksStolen { get; set; }
    public long StolenTasksCompleted { get; set; }
    public double StealingRate { get; set; }
    public double StealingEfficiency { get; set; }
    public required WorkerStealingStats[] WorkerStealingStats { get; set; }
}

/// <summary>
/// 工作线程窃取统计
/// </summary>
public class WorkerStealingStats
{
    public int WorkerId { get; set; }
    public long TasksStolen { get; set; }
    public long TasksStolenFrom { get; set; }
    public long NetStealingBalance { get; set; }
}


// 重用前面定义的MovingWindowCounter类
internal class MovingWindowCounter
{
    private readonly TimeSpan _windowSize;
    private readonly object _lock = new object();
    private readonly Queue<(DateTime timestamp, long count)> _samples = new();
    private long _totalCount = 0;
    
    public MovingWindowCounter(TimeSpan windowSize)
    {
        _windowSize = windowSize;
    }
    
    public void Increment(long count = 1)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _samples.Enqueue((now, count));
            _totalCount += count;
            
            // 清理过期样本
            var cutoff = now - _windowSize;
            while (_samples.Count > 0 && _samples.Peek().timestamp < cutoff)
            {
                var expired = _samples.Dequeue();
                _totalCount -= expired.count;
            }
        }
    }
    
    public double GetRate()
    {
        lock (_lock)
        {
            if (_samples.Count == 0)
                return 0.0;
            
            // 清理过期样本
            var now = DateTime.UtcNow;
            var cutoff = now - _windowSize;
            while (_samples.Count > 0 && _samples.Peek().timestamp < cutoff)
            {
                var expired = _samples.Dequeue();
                _totalCount -= expired.count;
            }
            
            if (_samples.Count == 0)
                return 0.0;
            
            var oldestSample = _samples.Peek();
            var actualWindow = now - oldestSample.timestamp;
            
            if (actualWindow.TotalSeconds <= 0)
                return 0.0;
            
            return _totalCount / actualWindow.TotalSeconds;
        }
    }
    
    // 工作线程计数器值的快速访问方法（临时实现）
    public long GetWorkerTasksEnqueuedCount(int workerId)
    {
        // TODO: Fix field access issue and implement proper counter tracking
        return 0;
    }
    
    public long GetWorkerTasksDequeuedCount(int workerId)
    {
        // TODO: Fix field access issue and implement proper counter tracking
        return 0;
    }
        
    public long GetWorkerTasksCompletedCount(int workerId)
    {
        // TODO: Fix field access issue and implement proper counter tracking  
        return 0;
    }
        
    public long GetWorkerTasksStolenCount(int workerId)
    {
        // TODO: Fix field access issue and implement proper counter tracking
        return 0;
    }
        
    public long GetWorkerTasksStolenFromCount(int workerId)
    {
        // TODO: Fix field access issue and implement proper counter tracking
        return 0;
    }
}

