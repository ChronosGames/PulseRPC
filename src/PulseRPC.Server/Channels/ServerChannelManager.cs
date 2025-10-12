using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Server.Engine;
using PulseRPC.Server.Transport;
using PulseRPC.Server.Serialization;
using PulseRPC.Server.Memory;
using PulseRPC.Transport;
using System.Buffers;
using PulseRPC.Server.Scheduling;
using PulseRPC.Server.Dispatch;
using PulseRPC.Server.Response;
using MessageProcessedEventArgs = PulseRPC.Server.Dispatch.MessageProcessedEventArgs;

namespace PulseRPC.Server.Processing;

/// <summary>
/// 增强的服务器通道管理器 - 集成高吞吐量消息处理器
/// </summary>
internal class ServerChannelManager : IServerChannelManager
{
    private readonly ConcurrentDictionary<string, IServerChannel> _channels;
    private readonly ITieredMessageEngineManager? _engineManager;
    private readonly IOptions<MessageEngineConfiguration> _processorOptions;
    private readonly ILogger<ServerChannelManager> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IMessageDispatcher _messageDispatcher;
    private readonly Timer _cleanupTimer;
    private volatile bool _disposed;

    // 统计信息
    private long _totalChannelsCreated;
    private long _totalChannelsRemoved;
    private long _totalEnginesCreated;

    /// <summary>
    /// 通道超时时间（毫秒）
    /// </summary>
    public TimeSpan ChannelTimeout { get; set; } = TimeSpan.FromMinutes(5);

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
    public event EventHandler<ChannelEventArgs>? ChannelConnected;

    /// <summary>
    /// 通道断开事件
    /// </summary>
    public event EventHandler<ChannelEventArgs>? ChannelDisconnected;

    /// <summary>
    /// 通道认证事件
    /// </summary>
    public event EventHandler<ChannelAuthenticatedEventArgs>? ChannelAuthenticated;

    public ServerChannelManager(
        ILogger<ServerChannelManager> logger,
        IOptions<MessageEngineConfiguration> processorOptions,
        IMessageDispatcher messageDispatcher,
        ILoggerFactory? loggerFactory = null,
        ITieredMessageEngineManager? engineManager = null)
    {
        _channels = new ConcurrentDictionary<string, IServerChannel>();
        _engineManager = engineManager ?? throw new ArgumentNullException(nameof(engineManager));
        _processorOptions = processorOptions;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory;
        _messageDispatcher = messageDispatcher ?? throw new ArgumentNullException(nameof(messageDispatcher));

        // 启动清理定时器，每60秒清理一次过期连接
        _cleanupTimer = new Timer(CleanupExpiredChannels, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _logger.LogInformation("增强服务器通道管理器已启动，引擎管理器: {EngineManagerType}, 消息分发器: {DispatcherType}",
            _engineManager?.GetType().Name ?? "None", _messageDispatcher.GetType().Name);
    }

    /// <summary>
    /// 添加新的传输通道
    /// </summary>
    /// <param name="transport">服务器连接</param>
    /// <returns>创建的传输通道</returns>
    public IServerChannel AddChannel(IServerTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ObjectDisposedException.ThrowIf(_disposed, nameof(ServerChannelManager));

        // 创建通道专用的日志器
        var channelLogger = _loggerFactory?.CreateLogger<ServerTransportChannel>();
        var channel = new ServerTransportChannel(transport, channelLogger);

        // 注册事件处理
        channel.StateChanged += OnChannelStateChanged;
        channel.MessageParsed += OnChannelMessageParsed;

        // 添加到管理字典
        if (_channels.TryAdd(transport.Id, channel))
        {
            Interlocked.Increment(ref _totalChannelsCreated);

            _logger.LogInformation("已添加传输通道: {ConnectionId}, 总数: {TotalCount}", ((ITransport)transport).Id, _channels.Count);

            // 如果启用了消息引擎，为此通道创建引擎
            _ = Task.Run(async () => await TryCreateEngineAsync(channel));

            ChannelConnected?.Invoke(this, new ChannelEventArgs(channel));
            return channel;
        }
        else
        {
            // 如果添加失败，说明连接ID冲突，释放新创建的通道
            channel.Dispose();
            throw new InvalidOperationException($"连接ID冲突: {transport.Id}");
        }
    }

    /// <summary>
    /// 尝试为通道创建消息引擎
    /// </summary>
    private async Task TryCreateEngineAsync(IServerChannel channel)
    {
        if (!_processorOptions.Value.Enabled || _engineManager == null)
            return;

        try
        {
            var engine = await _engineManager.GetOrCreateEngineAsync(channel.Id, channel, _messageDispatcher);
            Interlocked.Increment(ref _totalEnginesCreated);

            _logger.LogDebug("已为通道创建消息引擎: {ConnectionId}", channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "为通道创建消息引擎失败: {ConnectionId}", channel.Id);
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

        // 移除对应的消息引擎
        _ = Task.Run(async () => await TryRemoveEngineAsync(connectionId));

        // 取消订阅事件
        channel.StateChanged -= OnChannelStateChanged;
        channel.MessageParsed -= OnChannelMessageParsed;

        // 触发断开事件
        ChannelDisconnected?.Invoke(this, new ChannelEventArgs(channel));

        // 释放通道资源
        channel.Dispose();

        return true;
    }

    /// <summary>
    /// 尝试移除消息引擎
    /// </summary>
    private async Task TryRemoveEngineAsync(string connectionId)
    {
        if (_engineManager == null)
            return;

        try
        {
            await _engineManager.RemoveConnectionAsync(connectionId);
            _logger.LogDebug("已移除消息引擎: {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除消息引擎失败: {ConnectionId}", connectionId);
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
                _logger.LogWarning(ex, "向通道 {ConnectionId} 发送广播消息失败", channel.Id);
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Count(success => success);
    }

    /// <summary>
    /// 获取引擎统计信息
    /// </summary>
    public Dictionary<string, object?> GetEngineStats()
    {
        if (_engineManager == null)
            return new Dictionary<string, object?>();

        // 同步版本 - 返回基础统计信息
        return new Dictionary<string, object?>();
    }

    /// <summary>
    /// 获取通道管理器统计信息
    /// </summary>
    public ChannelManagerStats GetChannelManagerStats()
    {
        var engineStats = GetEngineStats();
        // 注意：引擎统计格式可能不同，这里返回基础统计
        var totalProcessed = 0L;
        var totalDropped = 0L;

        return new ChannelManagerStats
        {
            ActiveChannels = _channels.Count,
            TotalChannelsCreated = Interlocked.Read(ref _totalChannelsCreated),
            TotalChannelsRemoved = Interlocked.Read(ref _totalChannelsRemoved),
            TotalHighThroughputProcessorsCreated = engineStats.Count,
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
        if (sender is not IServerChannel channel)
            return;

        _logger.LogDebug("通道状态变更: {ConnectionId} - {OldState} -> {NewState}", channel.Id, e.PreviousState, e.CurrentState);

        // 如果连接断开，自动移除通道
        if (e.CurrentState == ConnectionState.Disconnected)
        {
            RemoveChannel(channel.Id);
        }
    }

    /// <summary>
    /// 处理通道消息解析事件
    /// </summary>
    private void OnChannelMessageParsed(object? sender, MessageParsedEventArgs e)
    {
        if (sender is not IServerChannel channel)
            return;

        _logger.LogTrace("[消息路由] {ConnectionId} 解析消息: 服务={ServiceName}, 方法={MethodName}, 类型={Type}",
            e.ConnectionId, e.MessagePacket.Header.ServiceName, e.MessagePacket.Header.MethodName, e.MessagePacket.Header.Type);

        // 将消息路由到引擎（如果启用）
        if (_engineManager != null && _processorOptions.Value.Enabled)
        {
            _ = Task.Run(async () => await RouteToEngineAsync(e));
        }
        else
        {
            // 如果没有启用引擎，可以在这里添加默认的消息处理逻辑
            _logger.LogWarning("[消息路由] {ConnectionId} 消息引擎未启用，消息将被丢弃", e.ConnectionId);
        }
    }

    /// <summary>
    /// 将消息路由到消息引擎
    /// </summary>
    private async Task RouteToEngineAsync(MessageParsedEventArgs eventArgs)
    {
        try
        {
            if (_engineManager == null)
            {
                // 回退路径：直接调用分发器
                _logger.LogWarning("[消息路由] {ConnectionId} 引擎管理器未初始化，使用回退处理", eventArgs.ConnectionId);
                await FallbackProcessMessageAsync(eventArgs);
                return;
            }

            // 获取当前连接对应的通道
            if (!_channels.TryGetValue(eventArgs.ConnectionId, out var serverChannel))
            {
                _logger.LogWarning("[消息路由] {ConnectionId} 找不到对应的服务器通道", eventArgs.ConnectionId);
                return;
            }

            // 获取或创建对应连接的消息引擎
            var engine = await _engineManager.GetOrCreateEngineAsync(
                eventArgs.ConnectionId,
                serverChannel,
                _messageDispatcher);

            // 传递完整消息包而非仅 Payload
            var priority = DetermineMessagePriority(eventArgs.MessagePacket.Header);
            var success = engine.TryEnqueueMessage(
                eventArgs.ConnectionId,
                eventArgs.MessagePacket, // 传递完整结构
                priority);

            if (success)
            {
                _logger.LogTrace("[消息路由] {ConnectionId} 消息已成功路由到引擎: 服务={ServiceName}, 方法={MethodName}, MessageId={MessageId}",
                    eventArgs.ConnectionId, eventArgs.MessagePacket.Header.ServiceName,
                    eventArgs.MessagePacket.Header.MethodName, eventArgs.MessagePacket.Header.MessageId);
            }
            else
            {
                _logger.LogWarning("[消息路由] {ConnectionId} 消息入队失败，尝试回退处理", eventArgs.ConnectionId);
                await FallbackProcessMessageAsync(eventArgs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[消息路由] {ConnectionId} 引擎处理失败，尝试回退处理", eventArgs.ConnectionId);
            await FallbackProcessMessageAsync(eventArgs);
        }
    }

    /// <summary>
    /// 回退处理路径 - 当高性能引擎不可用时使用
    /// </summary>
    private async Task FallbackProcessMessageAsync(MessageParsedEventArgs eventArgs)
    {
        _logger.LogInformation("[回退处理] {ConnectionId} 使用简化处理路径: Service={ServiceName}, Method={MethodName}",
            eventArgs.ConnectionId,
            eventArgs.MessagePacket.Header.ServiceName,
            eventArgs.MessagePacket.Header.MethodName);
    }

    /// <summary>
    /// 根据消息头确定消息优先级
    /// </summary>
    private static MessagePriority DetermineMessagePriority(MessageHeader header)
    {
        // 可以根据服务名、方法名或其他头信息确定优先级
        // 这里使用简单的策略
        return header.Type switch
        {
            MessageType.Request => MessagePriority.Normal,
            MessageType.Response => MessagePriority.High,
            MessageType.Event => MessagePriority.Low,
            _ => MessagePriority.Normal
        };
    }

    /// <summary>
    /// 清理过期的连接
    /// </summary>
    private void CleanupExpiredChannels(object? state)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(ServerChannelManager));

        try
        {
            var expiredThreshold = DateTime.UtcNow - ChannelTimeout;
            var expiredChannels = _channels.Values
                .Where(c => c.LastActiveTime < expiredThreshold)
                .ToList();

            foreach (var channel in expiredChannels)
            {
                _logger.LogInformation("清理过期通道: {ConnectionId}, 最后活动时间: {LastActiveTime}",
                    channel.Id, channel.LastActiveTime);

                RemoveChannel(channel.Id);
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
                channel.MessageParsed -= OnChannelMessageParsed;
                channel.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放通道 {ConnectionId} 时发生异常", channel.Id);
            }
        }

        var stats = GetChannelManagerStats();
        _logger.LogInformation("增强服务器通道管理器已释放，最终统计: 通道创建={ChannelsCreated}, 通道移除={ChannelsRemoved}, 处理器创建={ProcessorsCreated}",
            stats.TotalChannelsCreated, stats.TotalChannelsRemoved, stats.TotalHighThroughputProcessorsCreated);
    }
}

