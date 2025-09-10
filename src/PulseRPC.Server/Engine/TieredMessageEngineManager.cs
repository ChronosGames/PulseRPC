using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 分层消息引擎管理器 - 替代HighThroughputProcessorManager
/// 管理多个连接的TieredMessageProcessor实例，提供统一的接口
/// </summary>
public sealed class TieredMessageEngineManager : ITieredMessageEngineManager, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TieredMessageEngineManager> _logger;
    private readonly TieredEngineManagerOptions _options;
    
    // 连接处理器映射
    private readonly ConcurrentDictionary<string, TieredMessageProcessorAdapter> _connectionProcessors;
    private readonly ConcurrentDictionary<string, HighPerformanceMessageEngineV2> _engineInstances;
    
    // 管理状态
    private volatile bool _isDisposed;
    private readonly object _disposeLock = new();

    public TieredMessageEngineManager(
        IServiceProvider serviceProvider,
        IOptions<TieredEngineManagerOptions> options,
        ILogger<TieredMessageEngineManager> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
        
        _connectionProcessors = new ConcurrentDictionary<string, TieredMessageProcessorAdapter>();
        _engineInstances = new ConcurrentDictionary<string, HighPerformanceMessageEngineV2>();
        
        _logger.LogInformation("TieredMessageEngineManager已初始化，最大连接数：{MaxConnections}", 
            _options.MaxConnections);
    }

    /// <summary>
    /// 获取或创建连接的消息处理器适配器
    /// </summary>
    public async Task<TieredMessageProcessorAdapter> GetOrCreateProcessorAdapterAsync(
        string connectionId,
        IServerChannel serverChannel,
        IMessageHandlerRegistry handlerRegistry)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(TieredMessageEngineManager));
        
        return _connectionProcessors.GetOrAdd(connectionId, connId =>
        {
            _logger.LogDebug("创建新的TieredMessageProcessorAdapter：ConnectionId={ConnectionId}", connId);
            
            // 创建适配器专用的选项
            var processorOptions = Microsoft.Extensions.Options.Options.Create(
                CreateProcessorOptions(connId));
            
            var adapter = new TieredMessageProcessorAdapter(
                connId,
                serverChannel,
                handlerRegistry,
                processorOptions,
                _logger.CreateLogger<TieredMessageProcessorAdapter>());
                
            return adapter;
        });
    }

    /// <summary>
    /// 获取或创建高性能消息引擎V2实例
    /// </summary>
    public async Task<HighPerformanceMessageEngineV2> GetOrCreateEngineAsync(
        string connectionId,
        IServerChannel serverChannel,
        IMessageHandlerRegistry handlerRegistry)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(TieredMessageEngineManager));
        
        return _engineInstances.GetOrAdd(connectionId, connId =>
        {
            _logger.LogDebug("创建新的HighPerformanceMessageEngineV2：ConnectionId={ConnectionId}", connId);
            
            var engine = new HighPerformanceMessageEngineV2(
                connId,
                serverChannel,
                handlerRegistry,
                _logger.CreateLogger<HighPerformanceMessageEngineV2>());
                
            // 启动引擎
            _ = Task.Run(async () =>
            {
                try
                {
                    await engine.StartAsync();
                    _logger.LogInformation("HighPerformanceMessageEngineV2已启动：ConnectionId={ConnectionId}", connId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "启动HighPerformanceMessageEngineV2失败：ConnectionId={ConnectionId}", connId);
                }
            });
                
            return engine;
        });
    }

    /// <summary>
    /// 移除连接的处理器
    /// </summary>
    public async Task RemoveConnectionAsync(string connectionId)
    {
        if (_isDisposed) return;
        
        _logger.LogDebug("移除连接处理器：ConnectionId={ConnectionId}", connectionId);
        
        // 移除适配器
        if (_connectionProcessors.TryRemove(connectionId, out var adapter))
        {
            try
            {
                await adapter.DisposeAsync();
                _logger.LogDebug("TieredMessageProcessorAdapter已释放：ConnectionId={ConnectionId}", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放TieredMessageProcessorAdapter失败：ConnectionId={ConnectionId}", connectionId);
            }
        }
        
        // 移除引擎实例
        if (_engineInstances.TryRemove(connectionId, out var engine))
        {
            try
            {
                await engine.DisposeAsync();
                _logger.LogDebug("HighPerformanceMessageEngineV2已释放：ConnectionId={ConnectionId}", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放HighPerformanceMessageEngineV2失败：ConnectionId={ConnectionId}", connectionId);
            }
        }
    }

    /// <summary>
    /// 获取所有连接统计信息
    /// </summary>
    public async Task<ManagerStatistics> GetStatisticsAsync()
    {
        if (_isDisposed) return new ManagerStatistics { IsDisposed = true };
        
        var statistics = new ManagerStatistics
        {
            TotalConnections = _connectionProcessors.Count,
            TotalEngineInstances = _engineInstances.Count,
            IsDisposed = false,
            AdapterStatistics = new List<AdapterStatistics>(),
            EngineStatistics = new List<EngineInstanceStatistics>()
        };
        
        // 收集适配器统计
        foreach (var kvp in _connectionProcessors)
        {
            try
            {
                var adapterStats = kvp.Value.GetAdapterStatistics();
                statistics.AdapterStatistics.Add(adapterStats);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取适配器统计失败：ConnectionId={ConnectionId}", kvp.Key);
            }
        }
        
        // 收集引擎统计
        foreach (var kvp in _engineInstances)
        {
            try
            {
                var engineStats = await kvp.Value.GetStatisticsAsync();
                statistics.EngineStatistics.Add(new EngineInstanceStatistics
                {
                    ConnectionId = kvp.Key,
                    Statistics = engineStats
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取引擎统计失败：ConnectionId={ConnectionId}", kvp.Key);
            }
        }
        
        return statistics;
    }

    /// <summary>
    /// 创建处理器选项
    /// </summary>
    private HighThroughputProcessorOptions CreateProcessorOptions(string connectionId)
    {
        return new HighThroughputProcessorOptions
        {
            L1BufferSize = _options.DefaultL1BufferSize,
            L2QueueCapacity = _options.DefaultL2QueueCapacity,
            L3QueueCapacity = _options.DefaultL3QueueCapacity,
            MaxBatchSize = _options.DefaultMaxBatchSize,
            BatchIntervalMs = _options.DefaultBatchIntervalMs,
            EnableDetailedLogging = _options.EnableDetailedLogging,
            NormalMessageDropRate = _options.DefaultNormalMessageDropRate,
            CriticalMessageTimeoutUs = _options.DefaultCriticalMessageTimeoutUs,
            L2BackpressureWaitMs = _options.DefaultL2BackpressureWaitMs,
            PerformanceCheckFrequency = _options.DefaultPerformanceCheckFrequency,
            BatchSoftTimeoutMs = _options.DefaultBatchSoftTimeoutMs
        };
    }

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        
        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
        
        _logger.LogInformation("开始释放TieredMessageEngineManager，连接数：{ConnectionCount}", 
            _connectionProcessors.Count);
        
        // 释放所有适配器
        var adapterTasks = new List<Task>();
        foreach (var kvp in _connectionProcessors)
        {
            adapterTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await kvp.Value.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放适配器失败：ConnectionId={ConnectionId}", kvp.Key);
                }
            }));
        }
        
        // 释放所有引擎实例
        var engineTasks = new List<Task>();
        foreach (var kvp in _engineInstances)
        {
            engineTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await kvp.Value.DisposeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放引擎实例失败：ConnectionId={ConnectionId}", kvp.Key);
                }
            }));
        }
        
        // 等待所有释放完成
        try
        {
            await Task.WhenAll(adapterTasks.Concat(engineTasks)).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("TieredMessageEngineManager释放超时");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TieredMessageEngineManager释放过程中发生异常");
        }
        
        _connectionProcessors.Clear();
        _engineInstances.Clear();
        
        _logger.LogInformation("TieredMessageEngineManager已完全释放");
    }
}

/// <summary>
/// 分层消息引擎管理器接口
/// </summary>
public interface ITieredMessageEngineManager
{
    Task<TieredMessageProcessorAdapter> GetOrCreateProcessorAdapterAsync(
        string connectionId,
        IServerChannel serverChannel,
        IMessageHandlerRegistry handlerRegistry);
        
    Task<HighPerformanceMessageEngineV2> GetOrCreateEngineAsync(
        string connectionId,
        IServerChannel serverChannel,
        IMessageHandlerRegistry handlerRegistry);
        
    Task RemoveConnectionAsync(string connectionId);
    Task<ManagerStatistics> GetStatisticsAsync();
}

/// <summary>
/// 引擎管理器配置选项
/// </summary>
public class TieredEngineManagerOptions
{
    public int MaxConnections { get; set; } = 10000;
    
    // 默认处理器选项
    public int DefaultL1BufferSize { get; set; } = 4096;
    public int DefaultL2QueueCapacity { get; set; } = 256;
    public int DefaultL3QueueCapacity { get; set; } = 128;
    public int DefaultMaxBatchSize { get; set; } = 64;
    public int DefaultBatchIntervalMs { get; set; } = 5;
    public bool EnableDetailedLogging { get; set; } = false;
    public double DefaultNormalMessageDropRate { get; set; } = 0.8;
    public int DefaultCriticalMessageTimeoutUs { get; set; } = 100;
    public int DefaultL2BackpressureWaitMs { get; set; } = 1;
    public int DefaultPerformanceCheckFrequency { get; set; } = 10;
    public int DefaultBatchSoftTimeoutMs { get; set; } = 50;
}

/// <summary>
/// 管理器统计信息
/// </summary>
public class ManagerStatistics
{
    public int TotalConnections { get; set; }
    public int TotalEngineInstances { get; set; }
    public bool IsDisposed { get; set; }
    public List<AdapterStatistics> AdapterStatistics { get; set; } = new();
    public List<EngineInstanceStatistics> EngineStatistics { get; set; } = new();
}

/// <summary>
/// 引擎实例统计信息
/// </summary>
public class EngineInstanceStatistics
{
    public string ConnectionId { get; set; } = "";
    public object? Statistics { get; set; } // 引擎V2的统计信息类型
}

/// <summary>
/// DI扩展方法
/// </summary>
public static class TieredMessageEngineServiceExtensions
{
    /// <summary>
    /// 注册分层消息引擎相关服务
    /// </summary>
    public static IServiceCollection AddTieredMessageEngine(
        this IServiceCollection services,
        Action<TieredEngineManagerOptions>? configureOptions = null)
    {
        // 注册选项
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<TieredEngineManagerOptions>(options => { });
        }
        
        // 注册管理器
        services.AddSingleton<ITieredMessageEngineManager, TieredMessageEngineManager>();
        
        return services;
    }
}