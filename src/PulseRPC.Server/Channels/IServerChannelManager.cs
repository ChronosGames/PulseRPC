using PulseRPC.Authentication;
using PulseRPC.Transport;

namespace PulseRPC.Server.Transport;

/// <summary>
/// 通道事件参数
/// </summary>
public class ChannelEventArgs(ITransportChannel channel) : EventArgs
{
    public ITransportChannel Channel { get; } = channel ?? throw new ArgumentNullException(nameof(channel));
}

/// <summary>
/// 通道认证事件参数
/// </summary>
public class ChannelAuthenticatedEventArgs(ITransportChannel channel, IAuthenticationContext authContext)
    : ChannelEventArgs(channel)
{
    public IAuthenticationContext AuthenticationContext { get; } = authContext ?? throw new ArgumentNullException(nameof(authContext));
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


/// <summary>
/// 服务器通道管理器接口
/// </summary>
public interface IServerChannelManager : IDisposable
{
    /// <summary>
    /// 通道超时时间（毫秒）
    /// </summary>
    int ChannelTimeoutMs { get; set; }

    /// <summary>
    /// 当前连接数
    /// </summary>
    int ConnectionCount { get; }

    /// <summary>
    /// 所有连接ID
    /// </summary>
    IEnumerable<string> ConnectionIds { get; }

    /// <summary>
    /// 通道连接事件
    /// </summary>
    event System.EventHandler<ChannelEventArgs>? ChannelConnected;

    /// <summary>
    /// 通道断开事件
    /// </summary>
    event System.EventHandler<ChannelEventArgs>? ChannelDisconnected;

    /// <summary>
    /// 通道认证事件
    /// </summary>
    event System.EventHandler<ChannelAuthenticatedEventArgs>? ChannelAuthenticated;

    /// <summary>
    /// 添加新的传输通道
    /// </summary>
    /// <param name="transport">服务器连接</param>
    /// <returns>创建的传输通道</returns>
    IServerChannel AddChannel(IServerTransport transport);

    /// <summary>
    /// 获取指定的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>传输通道，如果不存在则返回null</returns>
    IServerChannel? GetChannel(string connectionId);

    /// <summary>
    /// 移除指定的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>是否成功移除</returns>
    bool RemoveChannel(string connectionId);

    /// <summary>
    /// 获取所有传输通道
    /// </summary>
    /// <returns>所有传输通道的集合</returns>
    IEnumerable<IServerChannel> GetAllChannels();

    /// <summary>
    /// 获取所有已认证的传输通道
    /// </summary>
    /// <returns>已认证的传输通道集合</returns>
    IEnumerable<IServerChannel> GetAuthenticatedChannels();

    /// <summary>
    /// 根据认证用户名获取传输通道
    /// </summary>
    /// <param name="username">用户名</param>
    /// <returns>用户的传输通道集合</returns>
    IEnumerable<IServerChannel> GetChannelsByUser(string username);

    /// <summary>
    /// 广播消息到所有已认证的通道
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发送的通道数量</returns>
    Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
