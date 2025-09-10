using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Processing;

/// <summary>
/// 自适应批处理调度器 - 基于负载动态调整批处理参数的智能调度器
/// 替代固定间隔的批处理机制，提供更优的延迟和吞吐量平衡
/// </summary>
public sealed class AdaptiveBatchScheduler : IAsyncDisposable
{
    #region 技术规格常量
    
    /// <summary>
    /// 调度器规格
    /// </summary>
    public static class SchedulerSpecs
    {
        public const int MIN_BATCH_INTERVAL_MS = 1;      // 最小批处理间隔1ms
        public const int MAX_BATCH_INTERVAL_MS = 10;     // 最大批处理间隔10ms
        public const int DEFAULT_BATCH_INTERVAL_MS = 2;  // 默认批处理间隔2ms
        
        public const int MIN_BATCH_SIZE = 8;             // 最小批大小
        public const int MAX_BATCH_SIZE = 128;           // 最大批大小
        public const int DEFAULT_BATCH_SIZE = 32;        // 默认批大小
        
        public const int METRICS_WINDOW_SIZE = 100;      // 性能指标窗口大小
        public const double TARGET_UTILIZATION = 0.8;   // 目标利用率80%
        public const int ADAPTATION_CHECK_INTERVAL_MS = 100; // 自适应检查间隔100ms
    }
    
    #endregion
    
    #region 字段和属性
    
    private readonly ILogger<AdaptiveBatchScheduler> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    // 当前调度参数 - 使用volatile确保线程安全的读取
    private volatile int _currentBatchInterval = SchedulerSpecs.DEFAULT_BATCH_INTERVAL_MS;
    private volatile int _currentBatchSize = SchedulerSpecs.DEFAULT_BATCH_SIZE;
    
    // 性能监控组件
    private readonly MovingAverageMetrics _throughputMetrics;
    private readonly LatencyTracker _latencyTracker;
    private readonly LoadAnalyzer _loadAnalyzer;
    
    // 调度状态
    private volatile bool _isRunning;
    private Task? _adaptationTask;
    
    // 回调处理
    private readonly List<IBatchProcessor> _processors = new();
    private readonly object _processorsLock = new();
    
    #endregion
    
    #region 构造函数和初始化
    
    /// <summary>
    /// 构造自适应批处理调度器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public AdaptiveBatchScheduler(ILogger<AdaptiveBatchScheduler> logger)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        
        // 初始化性能监控组件
        _throughputMetrics = new MovingAverageMetrics(SchedulerSpecs.METRICS_WINDOW_SIZE);
        _latencyTracker = new LatencyTracker();
        _loadAnalyzer = new LoadAnalyzer();
        
        _logger.LogInformation("自适应批处理调度器已创建，初始参数：间隔={BatchInterval}ms，批大小={BatchSize}",
            _currentBatchInterval, _currentBatchSize);
    }
    
    #endregion
    
    #region 核心调度API
    
    /// <summary>
    /// 当前批处理间隔(毫秒)
    /// </summary>
    public int CurrentBatchInterval => _currentBatchInterval;
    
    /// <summary>
    /// 当前批处理大小
    /// </summary>
    public int CurrentBatchSize => _currentBatchSize;
    
    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;
    
    /// <summary>
    /// 注册批处理器
    /// </summary>
    /// <param name="processor">批处理器实例</param>
    public void RegisterProcessor(IBatchProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        
        lock (_processorsLock)
        {
            _processors.Add(processor);
        }
        
        _logger.LogDebug("已注册批处理器：{ProcessorType}", processor.GetType().Name);
    }
    
    /// <summary>
    /// 取消注册批处理器
    /// </summary>
    /// <param name="processor">批处理器实例</param>
    public void UnregisterProcessor(IBatchProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        
        lock (_processorsLock)
        {
            _processors.Remove(processor);
        }
        
        _logger.LogDebug("已取消注册批处理器：{ProcessorType}", processor.GetType().Name);
    }
    
    /// <summary>
    /// 启动调度器
    /// </summary>
    public void Start()
    {
        if (_isRunning)
        {
            _logger.LogWarning("调度器已经在运行中");
            return;
        }
        
        _isRunning = true;
        
        // 启动自适应调整任务
        _adaptationTask = Task.Run(AdaptationLoop, _cancellationTokenSource.Token);
        
        _logger.LogInformation("自适应批处理调度器已启动");
    }
    
    /// <summary>
    /// 停止调度器
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;
        
        _isRunning = false;
        await _cancellationTokenSource.CancelAsync();
        
        if (_adaptationTask != null)
        {
            try
            {
                await _adaptationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期的取消异常，忽略
            }
        }
        
        _logger.LogInformation("自适应批处理调度器已停止");
    }
    
    #endregion
    
    #region 性能监控接口
    
    /// <summary>
    /// 记录批处理操作
    /// </summary>
    /// <param name="batchSize">批大小</param>
    /// <param name="processingTime">处理时间</param>
    /// <param name="queueDepth">队列深度</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordBatchOperation(int batchSize, TimeSpan processingTime, int queueDepth)
    {
        _throughputMetrics.Record(batchSize, processingTime);
        _latencyTracker.RecordLatency(processingTime);
        _loadAnalyzer.RecordLoad(queueDepth);
    }
    
    /// <summary>
    /// 获取当前性能指标
    /// </summary>
    public SchedulerMetrics GetMetrics()
    {
        return new SchedulerMetrics
        {
            CurrentBatchInterval = _currentBatchInterval,
            CurrentBatchSize = _currentBatchSize,
            AverageThroughput = _throughputMetrics.GetAverageThroughput(),
            AverageLatency = _latencyTracker.GetAverageLatency(),
            P95Latency = _latencyTracker.GetPercentileLatency(0.95),
            CurrentLoad = _loadAnalyzer.GetCurrentLoad(),
            TotalBatches = _throughputMetrics.TotalBatches,
            AdaptationCount = _loadAnalyzer.AdaptationCount
        };
    }
    
    #endregion
    
    #region 自适应调整逻辑
    
    /// <summary>
    /// 自适应调整主循环
    /// </summary>
    private async Task AdaptationLoop()
    {
        _logger.LogDebug("自适应调整循环已启动");
        
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await Task.Delay(SchedulerSpecs.ADAPTATION_CHECK_INTERVAL_MS, 
                    _cancellationTokenSource.Token).ConfigureAwait(false);
                
                if (_throughputMetrics.HasEnoughSamples())
                {
                    AdaptParameters();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不需要记录错误
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自适应调整循环发生异常");
        }
        
        _logger.LogDebug("自适应调整循环已退出");
    }
    
    /// <summary>
    /// 自适应调整参数
    /// </summary>
    private void AdaptParameters()
    {
        var metrics = GetMetrics();
        var adjustment = CalculateAdjustment(metrics);
        
        if (adjustment.ShouldAdjust)
        {
            ApplyAdjustment(adjustment);
            LogAdjustment(metrics, adjustment);
        }
    }
    
    /// <summary>
    /// 计算调整策略
    /// </summary>
    private AdjustmentDecision CalculateAdjustment(SchedulerMetrics metrics)
    {
        var decision = new AdjustmentDecision();
        
        // 基于延迟的调整
        if (metrics.P95Latency.TotalMilliseconds > _currentBatchInterval * 2)
        {
            // 延迟过高，减小批间隔
            decision.NewInterval = Math.Max(SchedulerSpecs.MIN_BATCH_INTERVAL_MS, 
                _currentBatchInterval - 1);
            decision.Reason = "延迟过高";
            decision.ShouldAdjust = true;
        }
        else if (metrics.P95Latency.TotalMilliseconds < _currentBatchInterval * 0.5 && 
                 metrics.CurrentLoad > SchedulerSpecs.TARGET_UTILIZATION)
        {
            // 延迟很低且负载高，可以增加批间隔提升吞吐量
            decision.NewInterval = Math.Min(SchedulerSpecs.MAX_BATCH_INTERVAL_MS, 
                _currentBatchInterval + 1);
            decision.Reason = "延迟低且负载高";
            decision.ShouldAdjust = true;
        }
        
        // 基于负载的批大小调整
        if (metrics.CurrentLoad > 0.9)
        {
            // 高负载，增大批大小
            decision.NewBatchSize = Math.Min(SchedulerSpecs.MAX_BATCH_SIZE, 
                (int)(_currentBatchSize * 1.2));
            decision.Reason += decision.ShouldAdjust ? "；高负载增大批量" : "高负载增大批量";
            decision.ShouldAdjust = true;
        }
        else if (metrics.CurrentLoad < 0.3)
        {
            // 低负载，减小批大小降低延迟
            decision.NewBatchSize = Math.Max(SchedulerSpecs.MIN_BATCH_SIZE, 
                (int)(_currentBatchSize * 0.8));
            decision.Reason += decision.ShouldAdjust ? "；低负载减小批量" : "低负载减小批量";
            decision.ShouldAdjust = true;
        }
        else
        {
            decision.NewBatchSize = _currentBatchSize;
        }
        
        if (!decision.ShouldAdjust)
        {
            decision.NewInterval = _currentBatchInterval;
            decision.NewBatchSize = _currentBatchSize;
        }
        
        return decision;
    }
    
    /// <summary>
    /// 应用调整
    /// </summary>
    private void ApplyAdjustment(AdjustmentDecision decision)
    {
        _currentBatchInterval = decision.NewInterval;
        _currentBatchSize = decision.NewBatchSize;
        
        _loadAnalyzer.IncrementAdaptationCount();
        
        // 通知所有注册的处理器参数已更新
        NotifyProcessors();
    }
    
    /// <summary>
    /// 通知处理器参数更新
    /// </summary>
    private void NotifyProcessors()
    {
        lock (_processorsLock)
        {
            foreach (var processor in _processors)
            {
                try
                {
                    processor.OnParametersUpdated(_currentBatchInterval, _currentBatchSize);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "通知处理器参数更新时发生异常：{ProcessorType}", 
                        processor.GetType().Name);
                }
            }
        }
    }
    
    /// <summary>
    /// 记录调整日志
    /// </summary>
    private void LogAdjustment(SchedulerMetrics metrics, AdjustmentDecision decision)
    {
        _logger.LogInformation(
            "参数自适应调整：间隔 {OldInterval}ms→{NewInterval}ms，批大小 {OldSize}→{NewSize}，" +
            "原因：{Reason}，当前指标：吞吐量={Throughput:F1}/s，P95延迟={Latency:F2}ms，负载={Load:P1}",
            _currentBatchInterval, decision.NewInterval,
            _currentBatchSize, decision.NewBatchSize,
            decision.Reason,
            metrics.AverageThroughput, metrics.P95Latency.TotalMilliseconds, metrics.CurrentLoad);
    }
    
    #endregion
    
    #region 辅助类型
    
    /// <summary>
    /// 调整决策
    /// </summary>
    private struct AdjustmentDecision
    {
        public bool ShouldAdjust { get; set; }
        public int NewInterval { get; set; }
        public int NewBatchSize { get; set; }
        public string Reason { get; set; }
    }
    
    #endregion
    
    #region IAsyncDisposable实现
    
    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cancellationTokenSource.Dispose();
        
        _logger.LogDebug("自适应批处理调度器已释放");
    }
    
    #endregion
}

#region 支持组件

/// <summary>
/// 批处理器接口
/// </summary>
public interface IBatchProcessor
{
    /// <summary>
    /// 参数更新通知
    /// </summary>
    /// <param name="batchInterval">新的批间隔</param>
    /// <param name="batchSize">新的批大小</param>
    void OnParametersUpdated(int batchInterval, int batchSize);
}

/// <summary>
/// 调度器性能指标
/// </summary>
public struct SchedulerMetrics
{
    public int CurrentBatchInterval { get; set; }
    public int CurrentBatchSize { get; set; }
    public double AverageThroughput { get; set; }
    public TimeSpan AverageLatency { get; set; }
    public TimeSpan P95Latency { get; set; }
    public double CurrentLoad { get; set; }
    public long TotalBatches { get; set; }
    public long AdaptationCount { get; set; }
}

/// <summary>
/// 移动平均性能指标
/// </summary>
internal sealed class MovingAverageMetrics
{
    private readonly Queue<(int batchSize, long processingTicks)> _samples;
    private readonly int _windowSize;
    private readonly object _lock = new();
    private long _totalBatches;
    
    public MovingAverageMetrics(int windowSize)
    {
        _windowSize = windowSize;
        _samples = new Queue<(int, long)>(windowSize);
    }
    
    public void Record(int batchSize, TimeSpan processingTime)
    {
        lock (_lock)
        {
            if (_samples.Count >= _windowSize)
            {
                _samples.Dequeue();
            }
            
            _samples.Enqueue((batchSize, processingTime.Ticks));
            _totalBatches++;
        }
    }
    
    public double GetAverageThroughput()
    {
        lock (_lock)
        {
            if (_samples.Count == 0) return 0;
            
            long totalElements = 0;
            long totalTicks = 0;
            
            foreach (var (batchSize, processingTicks) in _samples)
            {
                totalElements += batchSize;
                totalTicks += processingTicks;
            }
            
            if (totalTicks == 0) return 0;
            
            return totalElements / TimeSpan.FromTicks(totalTicks).TotalSeconds;
        }
    }
    
    public bool HasEnoughSamples() => _samples.Count >= Math.Min(10, _windowSize / 2);
    
    public long TotalBatches => _totalBatches;
}

/// <summary>
/// 延迟跟踪器
/// </summary>
internal sealed class LatencyTracker
{
    private readonly Queue<long> _latencies = new();
    private readonly int _maxSamples = 100;
    private readonly object _lock = new();
    
    public void RecordLatency(TimeSpan latency)
    {
        lock (_lock)
        {
            if (_latencies.Count >= _maxSamples)
            {
                _latencies.Dequeue();
            }
            _latencies.Enqueue(latency.Ticks);
        }
    }
    
    public TimeSpan GetAverageLatency()
    {
        lock (_lock)
        {
            if (_latencies.Count == 0) return TimeSpan.Zero;
            
            var avgTicks = _latencies.Sum() / _latencies.Count;
            return TimeSpan.FromTicks(avgTicks);
        }
    }
    
    public TimeSpan GetPercentileLatency(double percentile)
    {
        lock (_lock)
        {
            if (_latencies.Count == 0) return TimeSpan.Zero;
            
            var sortedLatencies = _latencies.OrderBy(x => x).ToArray();
            int index = (int)(sortedLatencies.Length * percentile);
            if (index >= sortedLatencies.Length) index = sortedLatencies.Length - 1;
            
            return TimeSpan.FromTicks(sortedLatencies[index]);
        }
    }
}

/// <summary>
/// 负载分析器
/// </summary>
internal sealed class LoadAnalyzer
{
    private readonly Queue<double> _loadSamples = new();
    private readonly int _maxSamples = 50;
    private readonly object _lock = new();
    private long _adaptationCount;
    
    public void RecordLoad(int queueDepth)
    {
        // 简化的负载计算：基于队列深度
        double load = Math.Min(1.0, queueDepth / 100.0);
        
        lock (_lock)
        {
            if (_loadSamples.Count >= _maxSamples)
            {
                _loadSamples.Dequeue();
            }
            _loadSamples.Enqueue(load);
        }
    }
    
    public double GetCurrentLoad()
    {
        lock (_lock)
        {
            if (_loadSamples.Count == 0) return 0;
            return _loadSamples.Average();
        }
    }
    
    public void IncrementAdaptationCount()
    {
        Interlocked.Increment(ref _adaptationCount);
    }
    
    public long AdaptationCount => _adaptationCount;
}

#endregion