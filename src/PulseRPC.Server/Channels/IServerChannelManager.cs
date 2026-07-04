using PulseRPC.Authentication;
using PulseRPC.Shared;

namespace PulseRPC.Server.Transport;

/// <summary>
/// 通道事件参数
/// </summary>
public class ChannelEventArgs(IServerChannel channel) : EventArgs
{
    public IServerChannel Channel { get; } = channel ?? throw new ArgumentNullException(nameof(channel));
}

/// <summary>
/// 通道认证事件参数
/// </summary>
public class ChannelAuthenticatedEventArgs(IServerChannel channel, IAuthenticationContext authContext)
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
    public long TotalMessageEnginesCreated { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public bool MessageEngineEnabled { get; set; }
}


/// <summary>
/// 服务器通道管理器接口
/// </summary>
public interface IServerChannelManager : IDisposable
{
    /// <summary>
    /// 通道超时时间（毫秒）
    /// </summary>
    TimeSpan ChannelTimeout { get; set; }

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
    event EventHandler<ChannelEventArgs>? ChannelConnected;

    /// <summary>
    /// 通道断开事件
    /// </summary>
    event EventHandler<ChannelEventArgs>? ChannelDisconnected;

    /// <summary>
    /// 通道认证事件
    /// </summary>
    event EventHandler<ChannelAuthenticatedEventArgs>? ChannelAuthenticated;

    /// <summary>
    ///
    /// </summary>
    event EventHandler<PulseRPC.Server.Transport.MessageParsedEventArgs>? ChannelMessageParsed;

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
    /// 按 <paramref name="connectionId"/> 获取已注册的通道；若不存在，用 <paramref name="factory"/>
    /// 构造一个并原子性地注册（幂等：并发/重复调用只会保留一个实例）。
    /// </summary>
    /// <remarks>
    /// 供注册<strong>虚拟通道</strong>使用（如 Gateway 桥接的远程客户端连接，见 <c>GatewayVirtualChannel</c>）：
    /// 与 <see cref="AddChannel"/> 的区别是调用方直接提供完整的 <see cref="IServerChannel"/> 实例，
    /// 而非由本管理器基于 <see cref="IServerTransport"/> 构造，因此不要求底层真的存在一个网络传输连接。
    /// </remarks>
    /// <param name="connectionId">连接 Id（虚拟通道的 <see cref="IServerChannel.Id"/> 应与此一致）。</param>
    /// <param name="factory">首次注册时用于构造通道实例的工厂方法。</param>
    /// <returns>已存在或新注册的通道实例。</returns>
    IServerChannel GetOrRegisterVirtualChannel(string connectionId, Func<string, IServerChannel> factory);

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
    /// 广播消息到所有已认证的通道
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发送的通道数量</returns>
    Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取通道管理器统计信息
    /// </summary>
    /// <returns></returns>
    ChannelManagerStats GetChannelManagerStats();
}
