using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Serialization;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Dispatch;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 分层消息引擎管理器 - 统一管理所有连接的高性能消息引擎
/// 管理多个连接的HighPerformanceMessageEngine实例，提供统一的接口
/// </summary>
public sealed class TieredMessageEngineManager : ITieredMessageEngineManager, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TieredMessageEngineManager> _logger;
    private readonly TieredEngineManagerOptions _options;

    // 连接处理器映射
    private readonly ConcurrentDictionary<string, HighPerformanceMessageEngine> _engineInstances;

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

        _engineInstances = new ConcurrentDictionary<string, HighPerformanceMessageEngine>();

        _logger.LogInformation("TieredMessageEngineManager已初始化，最大连接数：{MaxConnections}",
            _options.MaxConnections);
    }

    /// <summary>
    /// 获取或创建高性能消息引擎实例
    /// </summary>
    public async Task<HighPerformanceMessageEngine> GetOrCreateEngineAsync(
        string connectionId,
        IServerChannel serverChannel,
        IMessageDispatcher messageDispatcher)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(TieredMessageEngineManager));

        // 如果引擎已存在，直接返回
        if (_engineInstances.TryGetValue(connectionId, out var existingEngine))
        {
            return existingEngine;
        }

        // 创建新引擎并确保完全初始化
        _logger.LogDebug("创建新的HighPerformanceMessageEngine：ConnectionId={ConnectionId}", connectionId);

        var config = new MessageEngineConfiguration();

        var engine = new HighPerformanceMessageEngine(
            connectionId,
            messageDispatcher,
            _serviceProvider,
            config,
            new NullLogger<HighPerformanceMessageEngine>(),
            null // scheduler
            );

        try
        {
            // 第一阶段：启动引擎 - 确保完全初始化
            await engine.StartAsync();
            _logger.LogInformation("HighPerformanceMessageEngine已启动：ConnectionId={ConnectionId}", connectionId);

            // 第二阶段：添加到管理集合
            var addedEngine = _engineInstances.GetOrAdd(connectionId, engine);

            // 如果添加失败（已存在），释放当前引擎并返回已存在的
            if (addedEngine != engine)
            {
                await engine.DisposeAsync();
                return addedEngine;
            }

            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动HighPerformanceMessageEngine失败：ConnectionId={ConnectionId}", connectionId);
            await engine.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// 移除连接的处理器
    /// </summary>
    public async Task RemoveConnectionAsync(string connectionId)
    {
        if (_isDisposed) return;

        _logger.LogDebug("移除连接处理器：ConnectionId={ConnectionId}", connectionId);


        // 移除引擎实例
        if (_engineInstances.TryRemove(connectionId, out var engine))
        {
            try
            {
                await engine.DisposeAsync();
                _logger.LogDebug("HighPerformanceMessageEngine已释放：ConnectionId={ConnectionId}", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放HighPerformanceMessageEngine失败：ConnectionId={ConnectionId}", connectionId);
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
            TotalConnections = _engineInstances.Count,
            TotalEngineInstances = _engineInstances.Count,
            IsDisposed = false,
            AdapterStatistics = new List<AdapterStatistics>(),
            EngineStatistics = new List<EngineInstanceStatistics>()
        };


        // 收集引擎统计
        foreach (var kvp in _engineInstances)
        {
            try
            {
                var engineStats = kvp.Value.GetStatistics();
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
            _engineInstances.Count);


        // 释放所有引擎实例
        foreach (var kvp in _engineInstances)
        {
            try
            {
                await kvp.Value.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放引擎实例失败：ConnectionId={ConnectionId}", kvp.Key);
            }
        }

        _engineInstances.Clear();

        _logger.LogInformation("TieredMessageEngineManager已完全释放");
    }
}

/// <summary>
/// 分层消息引擎管理器接口
/// </summary>
public interface ITieredMessageEngineManager
{
    Task<HighPerformanceMessageEngine> GetOrCreateEngineAsync(
        string connectionId,
        IServerChannel serverChannel,
        IMessageDispatcher messageDispatcher);

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

