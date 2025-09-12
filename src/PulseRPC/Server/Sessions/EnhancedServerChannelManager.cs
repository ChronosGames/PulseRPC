using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Sessions;

/// <summary>
/// 增强的服务端通道管理器 - 集成三层抽象架构
/// 桥接传统的IServerChannelManager和新的IServerSessionManager
/// </summary>
public class EnhancedServerChannelManager : IEnhancedServerChannelManager
{
    private readonly ServerChannelManager _channelManager;
    private readonly ServerSessionManager _sessionManager;
    private readonly ILogger<EnhancedServerChannelManager> _logger;
    private volatile bool _disposed;

    /// <summary>
    /// 会话管理器
    /// </summary>
    public IServerSessionManager SessionManager => _sessionManager;

    /// <summary>
    /// 通道超时时间（毫秒）
    /// </summary>
    public int ChannelTimeoutMs
    {
        get => _channelManager.ChannelTimeoutMs;
        set
        {
            _channelManager.ChannelTimeoutMs = value;
            _sessionManager.SessionTimeoutMs = value;
        }
    }

    /// <summary>
    /// 当前连接数
    /// </summary>
    public int ConnectionCount => _channelManager.ConnectionCount;

    /// <summary>
    /// 所有连接ID
    /// </summary>
    public IEnumerable<string> ConnectionIds => _channelManager.ConnectionIds;

    /// <summary>
    /// 通道连接事件
    /// </summary>
    public event EventHandler<ChannelEventArgs>? ChannelConnected
    {
        add => _channelManager.ChannelConnected += value;
        remove => _channelManager.ChannelConnected -= value;
    }

    /// <summary>
    /// 通道断开事件
    /// </summary>
    public event EventHandler<ChannelEventArgs>? ChannelDisconnected
    {
        add => _channelManager.ChannelDisconnected += value;
        remove => _channelManager.ChannelDisconnected -= value;
    }

    /// <summary>
    /// 通道认证事件
    /// </summary>
    public event EventHandler<ChannelAuthenticatedEventArgs>? ChannelAuthenticated
    {
        add => _channelManager.ChannelAuthenticated += value;
        remove => _channelManager.ChannelAuthenticated -= value;
    }

    public EnhancedServerChannelManager(
        ServerChannelManager channelManager,
        ServerSessionManager sessionManager,
        ILogger<EnhancedServerChannelManager> logger)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 监听通道事件，自动创建和管理对应的客户端会话
        _channelManager.ChannelConnected += OnChannelConnected;
        _channelManager.ChannelDisconnected += OnChannelDisconnected;
        _channelManager.ChannelAuthenticated += OnChannelAuthenticated;

        _logger.LogInformation("增强服务端通道管理器已启动");
    }

    #region IServerChannelManager Implementation

    /// <summary>
    /// 添加新的传输通道
    /// </summary>
    /// <param name="transport">服务器连接</param>
    /// <returns>创建的传输通道</returns>
    public IServerChannel AddChannel(IServerTransport transport)
    {
        var channel = _channelManager.AddChannel(transport);

        // 通道创建事件会自动触发OnChannelConnected，在那里创建对应的客户端会话

        return channel;
    }

    /// <summary>
    /// 获取指定的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>传输通道，如果不存在则返回null</returns>
    public IServerChannel? GetChannel(string connectionId)
    {
        return _channelManager.GetChannel(connectionId);
    }

    /// <summary>
    /// 移除指定的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveChannel(string connectionId)
    {
        // 先移除对应的客户端会话
        var session = _sessionManager.GetSessionByConnectionId(connectionId);
        if (session != null)
        {
            _sessionManager.RemoveSession(session.Descriptor.Id);
        }

        return _channelManager.RemoveChannel(connectionId);
    }

    /// <summary>
    /// 获取所有传输通道
    /// </summary>
    /// <returns>所有传输通道的集合</returns>
    public IEnumerable<IServerChannel> GetAllChannels()
    {
        return _channelManager.GetAllChannels();
    }

    /// <summary>
    /// 获取所有已认证的传输通道
    /// </summary>
    /// <returns>已认证的传输通道集合</returns>
    public IEnumerable<IServerChannel> GetAuthenticatedChannels()
    {
        return _channelManager.GetAuthenticatedChannels();
    }

    /// <summary>
    /// 根据认证用户名获取传输通道
    /// </summary>
    /// <param name="username">用户名</param>
    /// <returns>用户的传输通道集合</returns>
    public IEnumerable<IServerChannel> GetChannelsByUser(string username)
    {
        return _channelManager.GetChannelsByUser(username);
    }

    /// <summary>
    /// 广播消息到所有已认证的通道
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发送的通道数量</returns>
    public Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return _channelManager.BroadcastAsync(data, cancellationToken);
    }

    /// <summary>
    /// 获取通道管理器统计信息
    /// </summary>
    /// <returns></returns>
    public ChannelManagerStats GetChannelManagerStats()
    {
        return _channelManager.GetChannelManagerStats();
    }

    #endregion

    #region IEnhancedServerChannelManager Implementation

    /// <summary>
    /// 从通道创建客户端会话（业务层）
    /// </summary>
    /// <param name="serverChannel">服务端通道</param>
    /// <param name="descriptor">会话描述符</param>
    /// <returns>客户端会话</returns>
    public IClientSession CreateClientSession(IServerChannel serverChannel, ClientSessionDescriptor descriptor)
    {
        return _sessionManager.CreateSession(serverChannel, descriptor);
    }

    #endregion

    #region Private Event Handlers

    /// <summary>
    /// 处理通道连接事件 - 自动创建客户端会话
    /// </summary>
    private void OnChannelConnected(object? sender, ChannelEventArgs e)
    {
        try
        {
            // 自动为新连接的通道创建客户端会话
            var serverChannel = e.Channel as IServerChannel;
            if (serverChannel != null)
            {
                var sessionDescriptor = new ClientSessionDescriptor
                {
                    Id = $"session-{serverChannel.ConnectionId}",
                    Name = $"AutoSession-{DateTime.UtcNow:HHmmss}",
                    ClientId = serverChannel.ConnectionId,
                    Transport = serverChannel.TransportType,
                    TimeoutMs = ChannelTimeoutMs,
                    AutoCleanup = true
                };

                var session = _sessionManager.CreateSession(serverChannel, sessionDescriptor);

                _logger.LogDebug("已自动创建客户端会话: SessionId={SessionId}, ConnectionId={ConnectionId}",
                    sessionDescriptor.Id, serverChannel.ConnectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动创建客户端会话失败: ConnectionId={ConnectionId}",
                e.Channel.ConnectionId);
        }
    }

    /// <summary>
    /// 处理通道断开事件 - 自动移除客户端会话
    /// </summary>
    private void OnChannelDisconnected(object? sender, ChannelEventArgs e)
    {
        try
        {
            // 自动移除对应的客户端会话
            var session = _sessionManager.GetSessionByConnectionId(e.Channel.ConnectionId);
            if (session != null)
            {
                _sessionManager.RemoveSession(session.Descriptor.Id);

                _logger.LogDebug("已自动移除客户端会话: SessionId={SessionId}, ConnectionId={ConnectionId}",
                    session.Descriptor.Id, e.Channel.ConnectionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动移除客户端会话失败: ConnectionId={ConnectionId}",
                e.Channel.ConnectionId);
        }
    }

    /// <summary>
    /// 处理通道认证事件
    /// </summary>
    private void OnChannelAuthenticated(object? sender, ChannelAuthenticatedEventArgs e)
    {
        _logger.LogDebug("通道认证完成: ConnectionId={ConnectionId}, User={Username}",
            e.Channel.ConnectionId, e.AuthenticationContext.Name);
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 取消事件订阅
        _channelManager.ChannelConnected -= OnChannelConnected;
        _channelManager.ChannelDisconnected -= OnChannelDisconnected;
        _channelManager.ChannelAuthenticated -= OnChannelAuthenticated;

        // 释放管理器资源
        _sessionManager.Dispose();
        _channelManager.Dispose();

        _logger.LogInformation("增强服务端通道管理器已释放");
    }

    #endregion
}