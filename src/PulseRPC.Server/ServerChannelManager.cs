using PulseRPC.Messaging;

namespace PulseRPC.Server;

/// <summary>
/// 服务器通道接口
/// </summary>
public interface IServerChannel : IDisposable
{
    /// <summary>
    /// 通道名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 发送消息到特定客户端
    /// </summary>
    Task SendMessageAsync(string clientId, MessageHeader header, object body);

    /// <summary>
    /// 广播事件到所有客户端
    /// </summary>
    Task BroadcastEventAsync<T>(string eventName, T eventData);

    /// <summary>
    /// 广播事件到指定客户端
    /// </summary>
    Task BroadcastEventAsync<T>(string eventName, T eventData, IEnumerable<string> clientIds);

    /// <summary>
    /// 消息接收事件
    /// </summary>
    event System.EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// 客户端连接事件
    /// </summary>
    event System.EventHandler<ClientConnectedEventArgs>? ClientConnected;

    /// <summary>
    /// 客户端断开连接事件
    /// </summary>
    event System.EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
}

/// <summary>
/// 消息接收事件参数
/// </summary>
public class MessageReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 客户端ID
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// 接收到的消息
    /// </summary>
    public NetworkMessage Message { get; }

    public MessageReceivedEventArgs(string clientId, NetworkMessage message)
    {
        ClientId = clientId;
        Message = message;
    }
}

/// <summary>
/// 客户端连接事件参数
/// </summary>
public class ClientConnectedEventArgs : EventArgs
{
    /// <summary>
    /// 客户端ID
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// 客户端远程地址
    /// </summary>
    public string RemoteAddress { get; }

    public ClientConnectedEventArgs(string clientId, string remoteAddress)
    {
        ClientId = clientId;
        RemoteAddress = remoteAddress;
    }
}

/// <summary>
/// 客户端断开连接事件参数
/// </summary>
public class ClientDisconnectedEventArgs(string clientId, string reason) : EventArgs
{
    /// <summary>
    /// 客户端ID
    /// </summary>
    public string ClientId { get; } = clientId;

    /// <summary>
    /// 断开原因
    /// </summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// 服务器通道管理器接口
/// </summary>
public interface IServerChannelManager : IDisposable
{
    /// <summary>
    /// 获取通道
    /// </summary>
    IServerChannel GetChannel(string channelName);

    /// <summary>
    /// 获取默认通道
    /// </summary>
    IServerChannel GetDefaultChannel();

    /// <summary>
    /// 注册通道
    /// </summary>
    void RegisterChannel(string channelName, IServerChannel channel, bool isDefault = false);

    /// <summary>
    /// 广播事件
    /// </summary>
    Task BroadcastEventAsync<T>(string channelName, string eventName, T eventData);
}

/// <summary>
/// 服务器通道管理器实现
/// </summary>
internal class ServerChannelManager : IServerChannelManager
{
    private readonly Dictionary<string, IServerChannel> _channels = new();
    private string _defaultChannelName = string.Empty;
    private readonly object _syncLock = new object();

    public IServerChannel GetChannel(string channelName)
    {
        lock (_syncLock)
        {
            if (_channels.TryGetValue(channelName, out var channel))
            {
                return channel;
            }

            if (string.IsNullOrEmpty(_defaultChannelName))
            {
                throw new InvalidOperationException("No default channel registered");
            }

            return _channels[_defaultChannelName];
        }
    }

    public IServerChannel GetDefaultChannel()
    {
        lock (_syncLock)
        {
            if (string.IsNullOrEmpty(_defaultChannelName))
            {
                throw new InvalidOperationException("No default channel registered");
            }

            return _channels[_defaultChannelName];
        }
    }

    public void RegisterChannel(string channelName, IServerChannel channel, bool isDefault = false)
    {
        lock (_syncLock)
        {
            if (!_channels.TryAdd(channelName, channel))
            {
                throw new ArgumentException($"Channel already registered: {channelName}");
            }

            if (isDefault || string.IsNullOrEmpty(_defaultChannelName))
            {
                _defaultChannelName = channelName;
            }
        }
    }

    public async Task BroadcastEventAsync<T>(string channelName, string eventName, T eventData)
    {
        IServerChannel? channel;

        lock (_syncLock)
        {
            if (!_channels.TryGetValue(channelName, out channel))
            {
                // 如果找不到指定通道，使用默认通道
                if (string.IsNullOrEmpty(_defaultChannelName))
                {
                    throw new InvalidOperationException("No default channel registered");
                }

                channel = _channels[_defaultChannelName];
            }
        }

        // 广播事件
        await channel.BroadcastEventAsync(eventName, eventData);
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            foreach (var channel in _channels.Values)
            {
                channel.Dispose();
            }

            _channels.Clear();
        }
    }
}
