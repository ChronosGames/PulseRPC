using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Engine;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Processing;

/// <summary>
/// 高吞吐量处理器管理器 - 管理所有连接的高性能消息处理器
/// </summary>
public class HighThroughputProcessorManager : IHighThroughputProcessorManager
{
    private readonly ConcurrentDictionary<string, HighPerformanceMessageEngine> _processors;
    private readonly IMessageDispatcher _messageDispatcher;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<MessageEngineConfiguration> _options;
    private readonly ILogger<HighThroughputProcessorManager> _logger;
    private readonly ILoggerFactory _loggerFactory;

    // 统计信息
    private long _totalProcessorsCreated;
    private long _totalProcessorsRemoved;
    private readonly DateTime _startTime;
    private volatile bool _disposed;

    public HighThroughputProcessorManager(
        IMessageDispatcher messageDispatcher,
        IServiceProvider serviceProvider,
        IOptions<MessageEngineConfiguration> options,
        ILogger<HighThroughputProcessorManager> logger,
        ILoggerFactory loggerFactory)
    {
        _processors = new ConcurrentDictionary<string, HighPerformanceMessageEngine>();
        _messageDispatcher = messageDispatcher;
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _startTime = DateTime.UtcNow;

        _logger.LogInformation("高吞吐量处理器管理器已初始化");
    }

    /// <summary>
    /// 为连接创建高吞吐量处理器
    /// </summary>
    public async Task<HighPerformanceMessageEngine> CreateProcessorAsync(string connectionId, IServerChannel serverChannel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HighThroughputProcessorManager));

        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("连接ID不能为空", nameof(connectionId));


        // 检查是否已存在
        if (_processors.ContainsKey(connectionId))
        {
            throw new InvalidOperationException($"连接 {connectionId} 的处理器已存在");
        }

        try
        {
            // 创建专用日志器
            var processorLogger = _loggerFactory.CreateLogger<HighPerformanceMessageEngine>();

            // 创建处理器
            var processor = new HighPerformanceMessageEngine(
                connectionId,
                _messageDispatcher,
                _serviceProvider,
                _options.Value,
                processorLogger);

            // 添加到管理集合
            if (_processors.TryAdd(connectionId, processor))
            {
                Interlocked.Increment(ref _totalProcessorsCreated);
                _logger.LogDebug("已创建高吞吐量处理器: ConnectionId={ConnectionId}, 总数={TotalCount}",
                    connectionId, _processors.Count);

                return processor;
            }
            else
            {
                // 添加失败，释放处理器
                await processor.DisposeAsync();
                throw new InvalidOperationException($"无法添加处理器到管理集合: {connectionId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建高吞吐量处理器失败: ConnectionId={ConnectionId}", connectionId);
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

        if (!_processors.TryRemove(connectionId, out var processor))
        {
            return false;
        }

        try
        {
            await processor.DisposeAsync();
            Interlocked.Increment(ref _totalProcessorsRemoved);

            _logger.LogInformation("已移除高吞吐量处理器: ConnectionId={ConnectionId}, 剩余={RemainingCount}",
                connectionId, _processors.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除处理器时发生异常: ConnectionId={ConnectionId}", connectionId);
            return false;
        }
    }

    /// <summary>
    /// 获取连接的处理器
    /// </summary>
    public HighPerformanceMessageEngine? GetProcessor(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return null;

        _processors.TryGetValue(connectionId, out var processor);
        return processor;
    }

    /// <summary>
    /// 获取所有处理器统计信息
    /// </summary>
    public Dictionary<string, EngineStatistics> GetAllStats()
    {
        var stats = new Dictionary<string, EngineStatistics>();

        foreach (var kvp in _processors)
        {
            try
            {
                stats[kvp.Key] = kvp.Value.GetStatistics();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取处理器统计信息失败: ConnectionId={ConnectionId}", kvp.Key);
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
        var totalProcessed = allStats.Values.Sum(s => s.TotalMessagesProcessed);
        var totalDropped = allStats.Values.Sum(s => s.TotalMessagesDropped);

        return new ManagerStats
        {
            ActiveProcessors = _processors.Count,
            TotalProcessorsCreated = Interlocked.Read(ref _totalProcessorsCreated),
            TotalProcessorsRemoved = Interlocked.Read(ref _totalProcessorsRemoved),
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
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("正在释放高吞吐量处理器管理器，当前处理器数量: {Count}", _processors.Count);

        // 释放所有处理器
        foreach (var processor in _processors.Values)
        {
            try
            {
                processor.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放处理器时发生异常");
            }
        }

        _processors.Clear();

        var finalStats = GetManagerStats();
        _logger.LogInformation("高吞吐量处理器管理器已释放，最终统计: 创建={Created}, 移除={Removed}, 处理消息={Processed}, 丢弃消息={Dropped}",
            finalStats.TotalProcessorsCreated,
            finalStats.TotalProcessorsRemoved,
            finalStats.TotalMessagesProcessed,
            finalStats.TotalMessagesDropped);
    }
}
