using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Processing;

/// <summary>
/// 增强的服务器通道管理器 - 集成高吞吐量消息处理器
/// </summary>
public class EnhancedServerChannelManager : IServerChannelManager
{
    private readonly ConcurrentDictionary<string, IServerChannel> _channels;
    private readonly IHighThroughputProcessorManager? _processorManager;
    private readonly IOptions<HighThroughputProcessorOptions> _processorOptions;
    private readonly ILogger<EnhancedServerChannelManager> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Timer _cleanupTimer;
    private volatile bool _disposed;

    // 统计信息
    private long _totalChannelsCreated;
    private long _totalChannelsRemoved;
    private long _totalHighThroughputProcessorsCreated;

    /// <summary>
    /// 通道超时时间（毫秒）
    /// </summary>
    public int ChannelTimeoutMs { get; set; } = 300000; // 5分钟

    /// <summary>
    /// 当前连接数
    /// </summary>
    public int ConnectionCount => _channels.Count;

    /// <summary>
    /// 所有连接ID
    /// </summary>
    public IEnumerable<string> ConnectionIds => _channels.Keys.ToList();

    /// <summary>
    /// 通道连接事件
    /// </summary>
    public event System.EventHandler<ChannelEventArgs>? ChannelConnected;

    /// <summary>
    /// 通道断开事件
    /// </summary>
    public event System.EventHandler<ChannelEventArgs>? ChannelDisconnected;

    /// <summary>
    /// 通道认证事件
    /// </summary>
    public event System.EventHandler<ChannelAuthenticatedEventArgs>? ChannelAuthenticated;

    public EnhancedServerChannelManager(
        ILogger<EnhancedServerChannelManager> logger,
        IOptions<HighThroughputProcessorOptions> processorOptions,
        ILoggerFactory? loggerFactory = null,
        IHighThroughputProcessorManager? processorManager = null)
    {
        _channels = new ConcurrentDictionary<string, IServerChannel>();
        _processorManager = processorManager;
        _processorOptions = processorOptions;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory;

        // 启动清理定时器，每60秒清理一次过期连接
        _cleanupTimer = new Timer(CleanupExpiredChannels, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _logger.LogInformation("增强服务器通道管理器已启动，高吞吐量处理器: {ProcessorEnabled}", _processorOptions.Value.Enabled ? "启用" : "禁用");
    }

    /// <summary>
    /// 添加新的传输通道
    /// </summary>
    /// <param name="transport">服务器连接</param>
    /// <returns>创建的传输通道</returns>
    public IServerChannel AddChannel(IServerTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ObjectDisposedException.ThrowIf(_disposed, nameof(EnhancedServerChannelManager));

        // 创建通道专用的日志器
        var channelLogger = _loggerFactory?.CreateLogger<ServerTransportChannel>();
        var channel = new ServerTransportChannel(transport, channelLogger);

        // 注册事件处理
        channel.StateChanged += OnChannelStateChanged;

        // 添加到管理字典
        if (_channels.TryAdd(transport.ConnectionId, channel))
        {
            Interlocked.Increment(ref _totalChannelsCreated);

            _logger.LogInformation("已添加传输通道: {ConnectionId}, 总数: {TotalCount}", transport.ConnectionId, _channels.Count);

            // 如果启用了高吞吐量处理器，为此通道创建处理器
            _ = Task.Run(async () => await TryCreateHighThroughputProcessorAsync(channel));

            ChannelConnected?.Invoke(this, new ChannelEventArgs(channel));
            return channel;
        }
        else
        {
            // 如果添加失败，说明连接ID冲突，释放新创建的通道
            channel.Dispose();
            throw new InvalidOperationException($"连接ID冲突: {transport.ConnectionId}");
        }
    }

    /// <summary>
    /// 尝试为通道创建高吞吐量处理器
    /// </summary>
    private async Task TryCreateHighThroughputProcessorAsync(IServerChannel channel)
    {
        if (!_processorOptions.Value.Enabled || _processorManager == null)
            return;

        try
        {
            var processor = await _processorManager.CreateProcessorAsync(channel.ConnectionId, channel);
            Interlocked.Increment(ref _totalHighThroughputProcessorsCreated);

            _logger.LogDebug("已为通道创建高吞吐量处理器: {ConnectionId}", channel.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "为通道创建高吞吐量处理器失败: {ConnectionId}", channel.ConnectionId);
        }
    }

    /// <summary>
    /// 获取指定的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>传输通道，如果不存在则返回null</returns>
    public IServerChannel? GetChannel(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return null;

        _channels.TryGetValue(connectionId, out var channel);
        return channel;
    }

    /// <summary>
    /// 移除指定的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveChannel(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return false;

        if (!_channels.TryRemove(connectionId, out var channel))
            return false;

        Interlocked.Increment(ref _totalChannelsRemoved);

        _logger.LogInformation("已移除传输通道: {ConnectionId}, 剩余: {RemainingCount}", connectionId, _channels.Count);

        // 移除对应的高吞吐量处理器
        _ = Task.Run(async () => await TryRemoveHighThroughputProcessorAsync(connectionId));

        // 取消订阅事件
        channel.StateChanged -= OnChannelStateChanged;

        // 触发断开事件
        ChannelDisconnected?.Invoke(this, new ChannelEventArgs(channel));

        // 释放通道资源
        channel.Dispose();

        return true;
    }

    /// <summary>
    /// 尝试移除高吞吐量处理器
    /// </summary>
    private async Task TryRemoveHighThroughputProcessorAsync(string connectionId)
    {
        if (_processorManager == null)
            return;

        try
        {
            var removed = await _processorManager.RemoveProcessorAsync(connectionId);
            if (removed)
            {
                _logger.LogDebug("已移除高吞吐量处理器: {ConnectionId}", connectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除高吞吐量处理器失败: {ConnectionId}", connectionId);
        }
    }

    /// <summary>
    /// 获取所有传输通道
    /// </summary>
    /// <returns>所有传输通道的集合</returns>
    public IEnumerable<IServerChannel> GetAllChannels()
    {
        return _channels.Values.ToList();
    }

    /// <summary>
    /// 获取所有已认证的传输通道
    /// </summary>
    /// <returns>已认证的传输通道集合</returns>
    public IEnumerable<IServerChannel> GetAuthenticatedChannels()
    {
        return _channels.Values.Where(c => c.IsAuthenticated).ToList();
    }

    /// <summary>
    /// 根据认证用户名获取传输通道
    /// </summary>
    /// <param name="username">用户名</param>
    /// <returns>用户的传输通道集合</returns>
    public IEnumerable<IServerChannel> GetChannelsByUser(string username)
    {
        if (string.IsNullOrEmpty(username))
            return Enumerable.Empty<IServerChannel>();

        return _channels.Values
            .Where(c => c.IsAuthenticated &&
                        string.Equals(c.AuthenticationContext?.Name, username, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// 广播消息到所有已认证的通道
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发送的通道数量</returns>
    public async Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var authenticatedChannels = GetAuthenticatedChannels().ToList();
        var tasks = authenticatedChannels.Select(async channel =>
        {
            try
            {
                return await channel.SendAsync(data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向通道 {ConnectionId} 发送广播消息失败", channel.ConnectionId);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Count(success => success);
    }

    /// <summary>
    /// 获取高吞吐量处理器统计信息
    /// </summary>
    public Dictionary<string, ProcessorStats> GetProcessorStats()
    {
        if (_processorManager == null)
            return new Dictionary<string, ProcessorStats>();

        return _processorManager.GetAllStats();
    }

    /// <summary>
    /// 获取通道管理器统计信息
    /// </summary>
    public ChannelManagerStats GetChannelManagerStats()
    {
        var processorStats = GetProcessorStats();
        var totalProcessed = processorStats.Values.Sum(s => s.TotalProcessed);
        var totalDropped = processorStats.Values.Sum(s => s.TotalDropped);

        return new ChannelManagerStats
        {
            ActiveChannels = _channels.Count,
            TotalChannelsCreated = Interlocked.Read(ref _totalChannelsCreated),
            TotalChannelsRemoved = Interlocked.Read(ref _totalChannelsRemoved),
            TotalHighThroughputProcessorsCreated = Interlocked.Read(ref _totalHighThroughputProcessorsCreated),
            TotalMessagesProcessed = totalProcessed,
            TotalMessagesDropped = totalDropped,
            HighThroughputProcessorEnabled = _processorOptions.Value.Enabled
        };
    }

    /// <summary>
    /// 处理通道状态变更事件
    /// </summary>
    private void OnChannelStateChanged(object? sender, TransportStateEventArgs e)
    {
        if (sender is not ITransportChannel channel)
            return;

        _logger.LogDebug("通道状态变更: {ConnectionId} - {OldState} -> {NewState}",
            channel.ConnectionId, e.PreviousState, e.CurrentState);

        // 如果连接断开，自动移除通道
        if (e.CurrentState == ConnectionState.Disconnected)
        {
            RemoveChannel(channel.ConnectionId);
        }
    }

    /// <summary>
    /// 清理过期的连接
    /// </summary>
    private void CleanupExpiredChannels(object? state)
    {
        if (_disposed)
            return;

        try
        {
            var expiredThreshold = DateTime.UtcNow.AddMilliseconds(-ChannelTimeoutMs);
            var expiredChannels = _channels.Values
                .Where(c => c.LastActiveTime < expiredThreshold)
                .ToList();

            foreach (var channel in expiredChannels)
            {
                _logger.LogInformation("清理过期通道: {ConnectionId}, 最后活动时间: {LastActiveTime}",
                    channel.ConnectionId, channel.LastActiveTime);

                RemoveChannel(channel.ConnectionId);
            }

            if (expiredChannels.Count > 0)
            {
                _logger.LogInformation("已清理 {Count} 个过期通道", expiredChannels.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期通道时发生异常");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 停止清理定时器
        _cleanupTimer.Dispose();

        // 关闭所有通道
        var allChannels = _channels.Values.ToList();
        _channels.Clear();

        foreach (var channel in allChannels)
        {
            try
            {
                channel.StateChanged -= OnChannelStateChanged;
                channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放通道 {ConnectionId} 时发生异常", channel.ConnectionId);
            }
        }

        var stats = GetChannelManagerStats();
        _logger.LogInformation("增强服务器通道管理器已释放，最终统计: 通道创建={ChannelsCreated}, 通道移除={ChannelsRemoved}, 处理器创建={ProcessorsCreated}",
            stats.TotalChannelsCreated, stats.TotalChannelsRemoved, stats.TotalHighThroughputProcessorsCreated);
    }
}

/// <summary>
/// 通道管理器统计信息
/// </summary>
public class ChannelManagerStats
{
    public int ActiveChannels { get; set; }
    public long TotalChannelsCreated { get; set; }
    public long TotalChannelsRemoved { get; set; }
    public long TotalHighThroughputProcessorsCreated { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public bool HighThroughputProcessorEnabled { get; set; }
}
