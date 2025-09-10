using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Engine;

/// <summary>
/// 分层处理器管理器适配器 - 实现IHighThroughputProcessorManager接口
/// 提供向后兼容性，将现有的处理器管理接口桥接到新的TieredMessageEngineManager
/// </summary>
public sealed class TieredProcessorManagerAdapter : IHighThroughputProcessorManager
{
    private readonly ITieredMessageEngineManager _engineManager;
    private readonly IMessageHandlerRegistry _handlerRegistry;
    private readonly ILogger<TieredProcessorManagerAdapter> _logger;
    private readonly DateTime _startTime;
    
    // 兼容性处理器包装器缓存
    private readonly ConcurrentDictionary<string, CompatibilityProcessorWrapper> _processorWrappers;
    
    // 统计计数器
    private long _totalProcessorsCreated;
    private long _totalProcessorsRemoved;
    private volatile bool _disposed;

    public TieredProcessorManagerAdapter(
        ITieredMessageEngineManager engineManager,
        IMessageHandlerRegistry handlerRegistry,
        ILogger<TieredProcessorManagerAdapter> logger)
    {
        _engineManager = engineManager;
        _handlerRegistry = handlerRegistry;
        _logger = logger;
        _startTime = DateTime.UtcNow;
        
        _processorWrappers = new ConcurrentDictionary<string, CompatibilityProcessorWrapper>();
        
        _logger.LogInformation("TieredProcessorManagerAdapter已初始化");
    }

    /// <summary>
    /// 为连接创建高吞吐量处理器
    /// </summary>
    public async Task<ServerHighThroughputMessageProcessor> CreateProcessorAsync(
        string connectionId, 
        IServerChannel serverChannel)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(TieredProcessorManagerAdapter));
        
        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("连接ID不能为空", nameof(connectionId));
        
        if (_processorWrappers.ContainsKey(connectionId))
        {
            throw new InvalidOperationException($"连接 {connectionId} 的处理器已存在");
        }
        
        try
        {
            // 获取或创建TieredMessageProcessorAdapter
            var adapter = await _engineManager.GetOrCreateProcessorAdapterAsync(
                connectionId, 
                serverChannel, 
                _handlerRegistry);
            
            // 创建兼容性包装器
            var wrapper = new CompatibilityProcessorWrapper(
                connectionId, 
                adapter, 
                _logger.CreateLogger<CompatibilityProcessorWrapper>());
            
            if (_processorWrappers.TryAdd(connectionId, wrapper))
            {
                System.Threading.Interlocked.Increment(ref _totalProcessorsCreated);
                
                _logger.LogDebug("已创建兼容性处理器包装器: ConnectionId={ConnectionId}, 总数={TotalCount}",
                    connectionId, _processorWrappers.Count);
                
                return wrapper;
            }
            else
            {
                await wrapper.DisposeAsync();
                throw new InvalidOperationException($"无法添加处理器包装器到管理集合: {connectionId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建兼容性处理器包装器失败: ConnectionId={ConnectionId}", connectionId);
            throw;
        }
    }

    /// <summary>
    /// 移除连接的处理器
    /// </summary>
    public async Task<bool> RemoveProcessorAsync(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return false;
        
        if (!_processorWrappers.TryRemove(connectionId, out var wrapper))
        {
            return false;
        }
        
        try
        {
            await wrapper.DisposeAsync();
            await _engineManager.RemoveConnectionAsync(connectionId);
            
            System.Threading.Interlocked.Increment(ref _totalProcessorsRemoved);
            
            _logger.LogInformation("已移除兼容性处理器包装器: ConnectionId={ConnectionId}, 剩余={RemainingCount}",
                connectionId, _processorWrappers.Count);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除处理器包装器时发生异常: ConnectionId={ConnectionId}", connectionId);
            return false;
        }
    }

    /// <summary>
    /// 获取连接的处理器
    /// </summary>
    public ServerHighThroughputMessageProcessor? GetProcessor(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return null;
        
        _processorWrappers.TryGetValue(connectionId, out var wrapper);
        return wrapper;
    }

    /// <summary>
    /// 获取所有处理器统计信息
    /// </summary>
    public Dictionary<string, ProcessorStats> GetAllStats()
    {
        var stats = new Dictionary<string, ProcessorStats>();
        
        foreach (var kvp in _processorWrappers)
        {
            try
            {
                stats[kvp.Key] = kvp.Value.GetStats();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取处理器包装器统计信息失败: ConnectionId={ConnectionId}", kvp.Key);
            }
        }
        
        return stats;
    }

    /// <summary>
    /// 获取管理器状态
    /// </summary>
    public ManagerStats GetManagerStats()
    {
        var allStats = GetAllStats();
        var totalProcessed = allStats.Values.Sum(s => s.TotalProcessed);
        var totalDropped = allStats.Values.Sum(s => s.TotalDropped);
        
        return new ManagerStats
        {
            ActiveProcessors = _processorWrappers.Count,
            TotalProcessorsCreated = System.Threading.Interlocked.Read(ref _totalProcessorsCreated),
            TotalProcessorsRemoved = System.Threading.Interlocked.Read(ref _totalProcessorsRemoved),
            TotalMessagesProcessed = totalProcessed,
            TotalMessagesDropped = totalDropped,
            StartTime = _startTime
        };
    }

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _logger.LogInformation("正在释放TieredProcessorManagerAdapter，当前处理器包装器数量: {Count}", 
            _processorWrappers.Count);
        
        // 并行释放所有包装器
        var disposeTasks = _processorWrappers.Values.Select(async wrapper =>
        {
            try
            {
                await wrapper.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放处理器包装器时发生异常");
            }
        }).ToArray();
        
        try
        {
            Task.WhenAll(disposeTasks).Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待处理器包装器释放时发生异常");
        }
        
        _processorWrappers.Clear();
        
        var finalStats = GetManagerStats();
        _logger.LogInformation("TieredProcessorManagerAdapter已释放，最终统计: 创建={Created}, 移除={Removed}, 处理消息={Processed}, 丢弃消息={Dropped}",
            finalStats.TotalProcessorsCreated,
            finalStats.TotalProcessorsRemoved,
            finalStats.TotalMessagesProcessed,
            finalStats.TotalMessagesDropped);
    }
}

/// <summary>
/// 兼容性处理器包装器 - 将TieredMessageProcessorAdapter包装为ServerHighThroughputMessageProcessor
/// </summary>
internal sealed class CompatibilityProcessorWrapper : ServerHighThroughputMessageProcessor
{
    private readonly string _connectionId;
    private readonly TieredMessageProcessorAdapter _adapter;
    private readonly ILogger<CompatibilityProcessorWrapper> _logger;

    public CompatibilityProcessorWrapper(
        string connectionId,
        TieredMessageProcessorAdapter adapter,
        ILogger<CompatibilityProcessorWrapper> logger)
        : base(CreateDummyChannel(), CreateDummyRegistry(), CreateDummyOptions(), CreateDummyLogger())
    {
        _connectionId = connectionId;
        _adapter = adapter;
        _logger = logger;
        
        _logger.LogDebug("兼容性处理器包装器已创建: ConnectionId={ConnectionId}", _connectionId);
    }

    /// <summary>
    /// 尝试将消息入队 - 委托给内部适配器
    /// </summary>
    public new bool TryEnqueueMessage(ServerMessage message)
    {
        try
        {
            return _adapter.TryEnqueueMessage(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "包装器消息入队失败: ConnectionId={ConnectionId}", _connectionId);
            return false;
        }
    }

    /// <summary>
    /// 获取统计信息 - 委托给内部适配器
    /// </summary>
    public new ProcessorStats GetStats()
    {
        try
        {
            return _adapter.GetStats();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取包装器统计信息失败: ConnectionId={ConnectionId}", _connectionId);
            return new ProcessorStats();
        }
    }

    /// <summary>
    /// 释放资源 - 委托给内部适配器
    /// </summary>
    public new async ValueTask DisposeAsync()
    {
        try
        {
            await _adapter.DisposeAsync();
            _logger.LogDebug("兼容性处理器包装器已释放: ConnectionId={ConnectionId}", _connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放包装器时发生异常: ConnectionId={ConnectionId}", _connectionId);
        }
        finally
        {
            // 不调用基类的DisposeAsync，因为我们不想释放虚拟的依赖项
            GC.SuppressFinalize(this);
        }
    }

    #region 创建虚拟依赖项的静态方法
    
    /// <summary>
    /// 创建虚拟的服务端通道
    /// </summary>
    private static IServerChannel CreateDummyChannel()
    {
        return new DummyServerChannel();
    }

    /// <summary>
    /// 创建虚拟的消息处理注册表
    /// </summary>
    private static IMessageHandlerRegistry CreateDummyRegistry()
    {
        return new DummyMessageHandlerRegistry();
    }

    /// <summary>
    /// 创建虚拟的处理器选项
    /// </summary>
    private static IOptions<HighThroughputProcessorOptions> CreateDummyOptions()
    {
        return Microsoft.Extensions.Options.Options.Create(new HighThroughputProcessorOptions
        {
            Enabled = true,
            L1BufferSize = 4096,
            L2QueueCapacity = 256,
            L3QueueCapacity = 128,
            MaxBatchSize = 64,
            BatchIntervalMs = 5
        });
    }

    /// <summary>
    /// 创建虚拟的日志记录器
    /// </summary>
    private static ILogger<ServerHighThroughputMessageProcessor> CreateDummyLogger()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger<ServerHighThroughputMessageProcessor>.Instance;
    }
    
    #endregion
}

#region 虚拟实现类

/// <summary>
/// 虚拟服务端通道实现
/// </summary>
internal sealed class DummyServerChannel : IServerChannel
{
    public Task SendAsync(ReadOnlyMemory<byte> data) => Task.CompletedTask;
    public void Dispose() { }
}

/// <summary>
/// 虚拟消息处理注册表实现
/// </summary>
internal sealed class DummyMessageHandlerRegistry : IMessageHandlerRegistry
{
    public Task<object?> HandleAsync(ServerMessage message) => Task.FromResult<object?>(null);
}

#endregion