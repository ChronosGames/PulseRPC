using Microsoft.Extensions.Logging;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Transport;
using PulseRPC.Messaging;
using PulseRPC.Serialization;

namespace PulseRPC.Transport;

/// <summary>
/// 通道管理器实现
/// </summary>
public class ChannelManager : IChannelManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, IClientChannel> _channels = new();
    private string? _defaultChannelName;
    private readonly object _syncLock = new object();

    public ChannelManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public IClientChannel GetChannel(string channelName)
    {
        lock (_syncLock)
        {
            if (_channels.TryGetValue(channelName, out var channel))
            {
                return channel;
            }

            throw new ArgumentException($"Channel not found: {channelName}");
        }
    }

    public IClientChannel GetDefaultChannel()
    {
        lock (_syncLock)
        {
            if (string.IsNullOrEmpty(_defaultChannelName))
            {
                throw new InvalidOperationException("No default channel registered");
            }

            return GetChannel(_defaultChannelName);
        }
    }

    public void RegisterChannel(string name, IClientChannel channel, bool isDefault = false)
    {
        lock (_syncLock)
        {
            if (!_channels.TryAdd(name, channel))
            {
                throw new ArgumentException($"Channel already registered: {name}");
            }

            if (isDefault || string.IsNullOrEmpty(_defaultChannelName))
            {
                _defaultChannelName = name;
            }
        }
    }

    public void UnregisterChannel(string channelName)
    {
        lock (_syncLock)
        {
            if (_channels.Remove(channelName) && channelName == _defaultChannelName)
            {
                _defaultChannelName = _channels.Keys.FirstOrDefault();
            }
        }
    }

    public bool HasChannel(string channelName)
    {
        lock (_syncLock)
        {
            return _channels.ContainsKey(channelName);
        }
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
            _defaultChannelName = null;
        }
    }

    public void RegisterChannel(string name, TransportType type, TransportOptions options, bool isDefault = false)
    {
        IClientTransport transport = type switch
        {
            TransportType.Tcp => new TcpClientTransport(options, _loggerFactory.CreateLogger<TcpClientTransport>()),
            TransportType.Kcp => new KcpClientTransport(options, _loggerFactory.CreateLogger<KcpClientTransport>()),
            _ => throw new NotSupportedException($"不支持的传输类型: {type}")
        };

        var channel = new TransportChannel(
            name,
            transport,
            PulseRPCSerializerProvider.Instance,
            logger: _loggerFactory.CreateLogger<TransportChannel>());

        RegisterChannel(name, channel, isDefault);
    }

    /// <summary>
    /// 获取服务代理
    /// </summary>
    public T GetService<T>() where T : class, IPulseService
    {
        // 简化实现 - 通过反射创建代理
        // 注意：这里应该使用Source Generator生成的代理类
        throw new NotImplementedException("GetService<T> 应该通过Source Generator扩展方法调用");
    }

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    public ISubscriptionToken RegisterEventListener<T>(T listener) where T : class, IPulseEventHandler
    {
        var defaultChannel = GetDefaultChannel();
        // 这里应该实现具体的事件监听器注册逻辑
        // 简化实现
        throw new NotImplementedException("RegisterEventListener<T> 需要具体实现");
    }
}
