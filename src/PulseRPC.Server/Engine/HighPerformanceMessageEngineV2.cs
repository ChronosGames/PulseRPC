using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Memory;
using PulseRPC.Server.Memory;
using PulseRPC.Server.Processing;
using PulseRPC.Transport;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 高性能消息引擎 V2 - 基于TieredMessageProcessor的统一消息处理架构
/// 
/// 特性：
/// - 每连接独立的TieredMessageProcessor实例
/// - 统一的消息路由和负载均衡
/// - 自适应性能调优
/// - 丰富的监控和诊断
/// </summary>
public sealed class HighPerformanceMessageEngineV2 : IAsyncDisposable
{
    private readonly ILogger<HighPerformanceMessageEngineV2> _logger;
    private readonly HighPerformanceEngineOptions _options;
    private readonly IMessageHandlerRegistry _handlerRegistry;
    
    // 连接级处理器管理
    private readonly ConcurrentDictionary<string, TieredMessageProcessor> _connectionProcessors;
    private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics;
    
    // 全局负载均衡
    private readonly LoadBalancer _loadBalancer;
    private readonly PerformanceMonitor _performanceMonitor;
    
    // 生命周期管理
    private readonly CancellationTokenSource _cancellationTokenSource;
    private volatile bool _isDisposed;
    
    public HighPerformanceMessageEngineV2(
        IOptions<HighPerformanceEngineOptions> options,
        IMessageHandlerRegistry handlerRegistry,
        ILogger<HighPerformanceMessageEngineV2> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _connectionProcessors = new ConcurrentDictionary<string, TieredMessageProcessor>();
        _connectionMetrics = new ConcurrentDictionary<string, ConnectionMetrics>();
        _loadBalancer = new LoadBalancer(_options.LoadBalancingStrategy, _logger);
        _performanceMonitor = new PerformanceMonitor(_options.MonitoringInterval, _logger);
        _cancellationTokenSource = new CancellationTokenSource();
        
        StartPerformanceMonitoring();
        
        _logger.LogInformation("HighPerformanceMessageEngineV2启动成功，负载均衡策略: {Strategy}",
            _options.LoadBalancingStrategy);
    }
    
    /// <summary>
    /// 为连接创建或获取处理器
    /// </summary>
    public async Task<TieredMessageProcessor> GetOrCreateProcessorAsync(string connectionId)
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(HighPerformanceMessageEngineV2));
            
        return _connectionProcessors.GetOrAdd(connectionId, connId =>
        {
            var processorOptions = CreateProcessorOptions(connId);
            var processor = new TieredMessageProcessor(
                connId,
                processorOptions,
                CreateMessageHandler(connId),
                _logger);
                
            // 注册连接指标
            _connectionMetrics.TryAdd(connId, new ConnectionMetrics(connId));
            
            _logger.LogDebug("为连接创建TieredMessageProcessor: ConnectionId={ConnectionId}", connId);
            return processor;
        });
    }
    
    /// <summary>
    /// 处理消息
    /// </summary>
    public async Task<bool> ProcessMessageAsync(string connectionId, ReadOnlyMemory<byte> messageData, 
        MessagePriority priority = MessagePriority.Normal)
    {
        if (_isDisposed)
            return false;
            
        try
        {
            // 获取连接处理器
            var processor = await GetOrCreateProcessorAsync(connectionId);
            
            // 负载均衡检查
            if (!_loadBalancer.ShouldAcceptMessage(connectionId, priority))
            {
                _logger.LogDebug("负载均衡拒绝消息: ConnectionId={ConnectionId}, Priority={Priority}",
                    connectionId, priority);
                return false;
            }
            
            // 入队消息
            var success = processor.TryEnqueueMessage(messageData, priority);
            
            // 更新连接指标
            if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
            {
                if (success)
                    metrics.MessagesAccepted.Increment();
                else
                    metrics.MessagesRejected.Increment();
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息失败: ConnectionId={ConnectionId}", connectionId);
            return false;
        }
    }
    
    /// <summary>
    /// 移除连接处理器
    /// </summary>
    public async Task RemoveProcessorAsync(string connectionId)
    {
        if (_connectionProcessors.TryRemove(connectionId, out var processor))
        {
            await processor.DisposeAsync();
            _connectionMetrics.TryRemove(connectionId, out _);
            _loadBalancer.RemoveConnection(connectionId);
            
            _logger.LogDebug("移除连接处理器: ConnectionId={ConnectionId}", connectionId);
        }
    }
    
    /// <summary>
    /// 创建处理器选项
    /// </summary>
    private TieredMessageProcessorOptions CreateProcessorOptions(string connectionId)
    {
        return new TieredMessageProcessorOptions
        {
            L1BufferSize = _options.L1BufferSize,
            MaxBatchSize = _options.MaxBatchSize,
            BatchChannelCapacity = _options.BatchChannelCapacity,
            EnableAdaptiveBatching = _options.EnableAdaptiveBatching,
            EnableDetailedLogging = _options.EnableDetailedLogging,
            NormalMessageDropThreshold = _options.BackpressureThreshold,
            CriticalMessageTimeoutUs = _options.CriticalMessageTimeoutUs
        };
    }
    
    /// <summary>
    /// 创建消息处理器
    /// </summary>
    private Func<MessageSlot, CancellationToken, ValueTask<ProcessingResult>> CreateMessageHandler(string connectionId)
    {
        return async (slot, cancellationToken) =>
        {
            var startTime = Stopwatch.GetTimestamp();
            
            try
            {
                // 这里集成实际的消息处理逻辑
                // 可以调用现有的IMessageHandlerRegistry
                
                // 模拟处理
                await Task.Delay(1, cancellationToken);
                
                var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime);
                
                // 更新连接指标
                if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
                {
                    metrics.MessagesProcessed.Increment();
                    metrics.RecordProcessingTime(processingTime);
                }
                
                return new ProcessingResult
                {
                    Success = true,
                    ProcessingTime = processingTime
                };
            }
            catch (Exception ex)
            {
                var processingTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime);
                
                if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
                {
                    metrics.MessagesErrored.Increment();
                }
                
                _logger.LogWarning(ex, "消息处理失败: ConnectionId={ConnectionId}, MessageId={MessageId}",
                    connectionId, slot.MessageId);
                
                return new ProcessingResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTime = processingTime
                };
            }
        };
    }
    
    /// <summary>
    /// 启动性能监控
    /// </summary>
    private void StartPerformanceMonitoring()
    {
        _ = Task.Run(async () =>
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await PerformMonitoringCycle();
                    await Task.Delay(_options.MonitoringInterval, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "性能监控周期失败");
                }
            }
        }, _cancellationTokenSource.Token);
    }
    
    /// <summary>
    /// 执行监控周期
    /// </summary>
    private async Task PerformMonitoringCycle()
    {
        var globalMetrics = new GlobalEngineMetrics();
        
        foreach (var kvp in _connectionProcessors)
        {
            var connectionId = kvp.Key;
            var processor = kvp.Value;
            
            try
            {
                var status = processor.GetStatus();
                var metrics = processor.GetMetrics();
                
                // 累积全局指标
                globalMetrics.TotalConnections++;
                globalMetrics.TotalMessagesProcessed += status.TotalMessagesProcessed;
                globalMetrics.TotalMessagesDropped += status.TotalMessagesDropped;
                globalMetrics.TotalThroughput += status.CurrentThroughput;
                
                // 检查是否需要负载均衡调整
                _loadBalancer.UpdateConnectionLoad(connectionId, status.CurrentThroughput, 
                    status.L1BufferUtilization);
                    
                // 性能告警检查
                await CheckPerformanceAlerts(connectionId, status, metrics);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "监控连接失败: ConnectionId={ConnectionId}", connectionId);
            }
        }
        
        // 记录全局指标
        _performanceMonitor.RecordGlobalMetrics(globalMetrics);
        
        if (_options.EnableDetailedLogging)
        {
            _logger.LogDebug("性能监控周期完成: 连接数={Connections}, 总吞吐量={Throughput:F2} msgs/sec",
                globalMetrics.TotalConnections, globalMetrics.TotalThroughput);
        }
    }
    
    /// <summary>
    /// 检查性能告警
    /// </summary>
    private async Task CheckPerformanceAlerts(string connectionId, ProcessorStatus status, 
        TieredProcessorMetrics metrics)
    {
        // L1缓冲区利用率告警
        if (status.L1BufferUtilization > _options.HighUtilizationThreshold)
        {
            _logger.LogWarning("L1缓冲区利用率过高: ConnectionId={ConnectionId}, Utilization={Utilization:P2}",
                connectionId, status.L1BufferUtilization);
        }
        
        // 吞吐量下降告警
        if (status.CurrentThroughput < _options.LowThroughputThreshold)
        {
            _logger.LogWarning("连接吞吐量过低: ConnectionId={ConnectionId}, Throughput={Throughput:F2} msgs/sec",
                connectionId, status.CurrentThroughput);
        }
        
        // 消息丢弃率告警
        var summary = metrics.GetSummary();
        if (summary.L1BackpressureRate > _options.HighBackpressureThreshold)
        {
            _logger.LogWarning("背压事件频繁: ConnectionId={ConnectionId}, BackpressureRate={Rate:P2}",
                connectionId, summary.L1BackpressureRate);
        }
    }
    
    /// <summary>
    /// 获取引擎状态
    /// </summary>
    public EngineStatus GetEngineStatus()
    {
        var connections = new List<ConnectionStatus>();
        
        foreach (var kvp in _connectionProcessors)
        {
            var connectionId = kvp.Key;
            var processor = kvp.Value;
            
            try
            {
                var status = processor.GetStatus();
                var metrics = _connectionMetrics.GetValueOrDefault(connectionId);
                
                connections.Add(new ConnectionStatus
                {
                    ConnectionId = connectionId,
                    ProcessorStatus = status,
                    ConnectionMetrics = metrics?.GetSummary()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取连接状态失败: ConnectionId={ConnectionId}", connectionId);
            }
        }
        
        return new EngineStatus
        {
            IsRunning = !_isDisposed,
            TotalConnections = connections.Count,
            Connections = connections,
            LoadBalancerStatus = _loadBalancer.GetStatus(),
            GlobalMetrics = _performanceMonitor.GetGlobalMetrics()
        };
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
            
        _isDisposed = true;
        
        _logger.LogInformation("正在关闭HighPerformanceMessageEngineV2");
        
        // 停止监控
        await _cancellationTokenSource.CancelAsync();
        
        // 关闭所有连接处理器
        var disposeTasks = new List<Task>();
        foreach (var processor in _connectionProcessors.Values)
        {
            disposeTasks.Add(processor.DisposeAsync().AsTask());
        }
        
        await Task.WhenAll(disposeTasks);
        _connectionProcessors.Clear();
        _connectionMetrics.Clear();
        
        // 清理资源
        _cancellationTokenSource.Dispose();
        
        _logger.LogInformation("HighPerformanceMessageEngineV2已关闭");
    }
}

/// <summary>
/// 高性能引擎配置选项
/// </summary>
public class HighPerformanceEngineOptions
{
    // TieredMessageProcessor配置
    public int L1BufferSize { get; set; } = 8192;
    public int MaxBatchSize { get; set; } = 64;
    public int BatchChannelCapacity { get; set; } = 256;
    public bool EnableAdaptiveBatching { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
    
    // 负载均衡配置
    public LoadBalancingStrategy LoadBalancingStrategy { get; set; } = LoadBalancingStrategy.RoundRobin;
    public double BackpressureThreshold { get; set; } = 0.8;
    public int CriticalMessageTimeoutUs { get; set; } = 1000;
    
    // 监控配置
    public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromSeconds(5);
    public double HighUtilizationThreshold { get; set; } = 0.85;
    public double LowThroughputThreshold { get; set; } = 100; // msgs/sec
    public double HighBackpressureThreshold { get; set; } = 0.1; // 10%
}

/// <summary>
/// 连接指标
/// </summary>
public class ConnectionMetrics
{
    public string ConnectionId { get; }
    public PerformanceCounter MessagesAccepted { get; } = new();
    public PerformanceCounter MessagesRejected { get; } = new();
    public PerformanceCounter MessagesProcessed { get; } = new();
    public PerformanceCounter MessagesErrored { get; } = new();
    
    private readonly RingBuffer<TimeSpan> _processingTimes = new(1000);
    private readonly object _lock = new();
    
    public ConnectionMetrics(string connectionId)
    {
        ConnectionId = connectionId;
    }
    
    public void RecordProcessingTime(TimeSpan time)
    {
        lock (_lock)
        {
            _processingTimes.Add(time);
        }
    }
    
    public ConnectionMetricsSummary GetSummary()
    {
        lock (_lock)
        {
            var avgTime = TimeSpan.Zero;
            if (_processingTimes.Count > 0)
            {
                var total = TimeSpan.Zero;
                for (int i = 0; i < _processingTimes.Count; i++)
                {
                    total += _processingTimes[i];
                }
                avgTime = TimeSpan.FromTicks(total.Ticks / _processingTimes.Count);
            }
            
            return new ConnectionMetricsSummary
            {
                ConnectionId = ConnectionId,
                MessagesAccepted = MessagesAccepted.Value,
                MessagesRejected = MessagesRejected.Value,
                MessagesProcessed = MessagesProcessed.Value,
                MessagesErrored = MessagesErrored.Value,
                AverageProcessingTime = avgTime
            };
        }
    }
}

/// <summary>
/// 负载均衡策略
/// </summary>
public enum LoadBalancingStrategy
{
    RoundRobin,
    LeastConnections,
    WeightedRoundRobin,
    AdaptiveLoad
}

// 支持类型定义
public class LoadBalancer
{
    private readonly LoadBalancingStrategy _strategy;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ConnectionLoadInfo> _connectionLoads = new();
    
    public LoadBalancer(LoadBalancingStrategy strategy, ILogger logger)
    {
        _strategy = strategy;
        _logger = logger;
    }
    
    public bool ShouldAcceptMessage(string connectionId, MessagePriority priority)
    {
        // 简化实现 - 关键消息总是接受
        if (priority == MessagePriority.Critical)
            return true;
            
        // 基于策略的负载控制
        return true; // 暂时简化
    }
    
    public void UpdateConnectionLoad(string connectionId, double throughput, double utilization)
    {
        _connectionLoads.AddOrUpdate(connectionId,
            new ConnectionLoadInfo { Throughput = throughput, Utilization = utilization },
            (_, existing) => 
            {
                existing.Throughput = throughput;
                existing.Utilization = utilization;
                return existing;
            });
    }
    
    public void RemoveConnection(string connectionId)
    {
        _connectionLoads.TryRemove(connectionId, out _);
    }
    
    public object GetStatus() => new { Strategy = _strategy, Connections = _connectionLoads.Count };
}

public class ConnectionLoadInfo
{
    public double Throughput { get; set; }
    public double Utilization { get; set; }
}

public class PerformanceMonitor
{
    private readonly TimeSpan _interval;
    private readonly ILogger _logger;
    private GlobalEngineMetrics _globalMetrics = new();
    
    public PerformanceMonitor(TimeSpan interval, ILogger logger)
    {
        _interval = interval;
        _logger = logger;
    }
    
    public void RecordGlobalMetrics(GlobalEngineMetrics metrics)
    {
        _globalMetrics = metrics;
    }
    
    public GlobalEngineMetrics GetGlobalMetrics() => _globalMetrics;
}

public class GlobalEngineMetrics
{
    public int TotalConnections { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public double TotalThroughput { get; set; }
}

public class ConnectionStatus
{
    public required string ConnectionId { get; set; }
    public ProcessorStatus? ProcessorStatus { get; set; }
    public ConnectionMetricsSummary? ConnectionMetrics { get; set; }
}

public class ConnectionMetricsSummary
{
    public required string ConnectionId { get; set; }
    public long MessagesAccepted { get; set; }
    public long MessagesRejected { get; set; }
    public long MessagesProcessed { get; set; }
    public long MessagesErrored { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
}

public class EngineStatus
{
    public bool IsRunning { get; set; }
    public int TotalConnections { get; set; }
    public required List<ConnectionStatus> Connections { get; set; }
    public object? LoadBalancerStatus { get; set; }
    public GlobalEngineMetrics? GlobalMetrics { get; set; }
}