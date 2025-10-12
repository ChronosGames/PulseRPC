using System.Collections.Concurrent;
using PulseRPC.Authentication;
using PulseRPC.Transport;
using Microsoft.Extensions.Logging;
using System.Net;
using PulseRPC.Server.Authentication;
using PulseRPC.Messaging;
using MessageProcessedEventArgs = PulseRPC.Server.Dispatch.MessageProcessedEventArgs;

namespace PulseRPC.Server.Transport;

/// <summary>
/// 消息解析完成事件参数
/// </summary>
public sealed class MessageParsedEventArgs : EventArgs
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// 解析的消息包
    /// </summary>
    public MessagePacketHolder MessagePacket { get; }

    /// <summary>
    /// 接收时间
    /// </summary>
    public DateTime ReceivedTime { get; }

    /// <summary>
    /// 处理器ID
    /// </summary>
    public int ProcessorId { get; }

    public MessageParsedEventArgs(string connectionId, MessagePacketHolder messagePacket, DateTime receivedTime, int processorId)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        MessagePacket = messagePacket ?? throw new ArgumentNullException(nameof(messagePacket));
        ReceivedTime = receivedTime;
        ProcessorId = processorId;
    }
}

// 旧的 MessageProcessedEventArgs 已移除，统一使用 PulseRPC.Server.Dispatch.MessageProcessedEventArgs
// 该事件参数已通过 using 别名导入

public interface IServerChannel : IDisposable
{
    string Id { get; }
    string ConnectionId => Id;

    DateTime ConnectedAt { get; }

    DateTime LastActiveTime { get; }

    /// <summary>
    /// 本地端点
    /// </summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>
    /// 远程端点
    /// </summary>
    EndPoint RemoteEndPoint { get; }

    /// <summary>
    /// 传输类型
    /// </summary>
    TransportType Type { get; }

    bool IsAuthenticated { get; }

    IAuthenticationContext? AuthenticationContext { get; set; }

    void SetAuthentication(IAuthenticationContext authContext);

    void ClearAuthentication();

    /// <summary>
    /// 发送数据
    /// </summary>
    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 状态变更事件
    /// </summary>
    event EventHandler<TransportStateEventArgs>? StateChanged;

    /// <summary>
    /// 消息解析完成事件
    /// </summary>
    event EventHandler<MessageParsedEventArgs>? MessageParsed;

    /// <summary>
    /// 消息处理完成事件
    /// </summary>
    event EventHandler<MessageProcessedEventArgs>? MessageProcessed;
}

/// <summary>
/// 服务器传输通道实现，包装 IServerListener 并提供认证和会话管理
/// 现在继承三层抽象架构中的ITransportChannel接口
/// </summary>
public class ServerTransportChannel : IServerChannel
{
    private readonly IServerTransport _transport;
    private readonly ConcurrentDictionary<string, object> _properties;
    private readonly Lock _authLock = new Lock();
    private readonly ILogger<ServerTransportChannel>? _logger;

    private IAuthenticationContext? _authenticationContext;
    private DateTime _lastActiveTime;
    private bool _disposed;

    public string Id => ((ITransport)_transport).Id;
    public TransportType Type => _transport.Type;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="transport">底层传输连接</param>
    /// <param name="logger">日志记录器</param>
    public ServerTransportChannel(
        IServerTransport transport,
        ILogger<ServerTransportChannel>? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _properties = new ConcurrentDictionary<string, object>();
        ConnectedTime = DateTime.UtcNow;
        _lastActiveTime = ConnectedTime;
        _logger = logger;

        // 转发传输层事件
        _transport.StateChanged += OnTransportStateChanged;
        _transport.DataReceived += OnTransportDataReceived;
    }

    /// <inheritdoc />
    public string ConnectionId => ((ITransport)_transport).Id;

    /// <inheritdoc />
    public IServerTransport Transport => _transport;

    #region ITransportConnection Implementation
    /// <inheritdoc />
    public ConnectionState State => _transport.State;

    /// <inheritdoc />
    public EndPoint RemoteEndPoint => _transport.RemoteEndPoint;

    /// <inheritdoc />
    public EndPoint LocalEndPoint => _transport.LocalEndPoint;

    /// <inheritdoc />
    public DateTime ConnectedAt => ConnectedTime;

    /// <inheritdoc />
    public DateTime LastActivityAt => _lastActiveTime;

    /// <inheritdoc />
    public TransportType TransportType => _transport.Type;

    /// <inheritdoc />
    public bool IsConnected => _transport.IsConnected;

    /// <inheritdoc />
    public event EventHandler<TransportStateEventArgs>? StateChanged;
    #endregion

    #region ISessionChannel Implementation
    /// <inheritdoc />
    public T? GetProperty<T>(string key)
    {
        if (_properties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }

    /// <inheritdoc />
    public void SetProperty<T>(string key, T value)
    {
        if (value != null)
        {
            _properties[key] = value;
        }
        else
        {
            _properties.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public bool RemoveProperty(string key)
    {
        return _properties.TryRemove(key, out _);
    }

    /// <inheritdoc />
    public bool HasProperty(string key)
    {
        return _properties.ContainsKey(key);
    }

    /// <inheritdoc />
    public event EventHandler<AuthenticationChangedEventArgs>? AuthenticationChanged;
    #endregion

    /// <inheritdoc />
    public IAuthenticationContext? AuthenticationContext
    {
        get
        {
            lock (_authLock)
            {
                return _authenticationContext;
            }
        }
        set
        {
            IAuthenticationContext? previous;
            lock (_authLock)
            {
                previous = _authenticationContext;
                _authenticationContext = value;
            }

            // 在锁外触发事件，避免死锁
            AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs(
                ConnectionId, previous, value));
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public IDictionary<string, object> Properties => _properties;

    /// <inheritdoc />
    public string RemoteAddress => _transport.RemoteEndPoint?.ToString() ?? "Unknown";

    /// <inheritdoc />
    public DateTime ConnectedTime { get; }

    /// <inheritdoc />
    public DateTime LastActiveTime
    {
        get => _lastActiveTime;
        set => _lastActiveTime = value;
    }

    /// <inheritdoc />
    public void SetAuthentication(IAuthenticationContext authContext)
    {
        if (authContext == null) throw new ArgumentNullException(nameof(authContext));

        lock (_authLock)
        {
            _authenticationContext = authContext;
            LastActiveTime = DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public void ClearAuthentication()
    {
        lock (_authLock)
        {
            _authenticationContext?.Clear();
            _authenticationContext = null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;

        LastActiveTime = DateTime.UtcNow;
        return await _transport.SendAsync(data, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        _transport.Dispose();
    }

    /// <inheritdoc />
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    /// <summary>
    /// 消息解析完成事件
    /// </summary>
    public event EventHandler<MessageParsedEventArgs>? MessageParsed;

    /// <summary>
    /// 消息处理完成事件
    /// </summary>
    public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

    /// <summary>
    /// 处理传输层状态变更事件
    /// </summary>
    private void OnTransportStateChanged(object? sender, TransportStateEventArgs e)
    {
        // 直接转发ConnectionStateChangedEventArgs事件
        StateChanged?.Invoke(this, e);

        // 连接断开时清理认证信息
        if (e.CurrentState == ConnectionState.Disconnected)
        {
            ClearAuthentication();
        }
    }

    /// <summary>
    /// 处理传输层数据接收事件
    /// </summary>
    private void OnTransportDataReceived(object? sender, TransportDataEventArgs e)
    {
        LastActiveTime = DateTime.UtcNow;

        if (sender is IServerTransport connection)
        {
            _logger?.LogDebug("[通道数据处理] {ConnectionId} 接收到传输数据: Size={Size} bytes", connection.Id, e.Data.Length);

            // 处理接收到的数据，解析消息包
            ProcessReceivedData(e.Data);
        }
        else
        {
            _logger?.LogWarning("[通道数据处理] 发送者不是IServerTransport类型: {SenderType}", sender?.GetType().Name ?? "null");
        }

        // 继续转发原始数据事件，保持向后兼容性
        DataReceived?.Invoke(this, e);
    }

    /// <summary>
    /// 处理接收到的数据，解析消息包并触发事件给 ServerChannelManager 路由
    /// </summary>
    private void ProcessReceivedData(ReadOnlyMemory<byte> data)
    {
        try
        {
            // 统一使用消息包解析方式，触发 MessageParsed 事件让 ServerChannelManager 处理路由
            ParseAndTriggerMessageEvent(data);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[消息处理] {ConnectionId} 处理接收数据时发生异常: Size={Size} bytes",
                ConnectionId, data.Length);
        }
    }

    /// <summary>
    /// 解析消息包并触发 MessageParsed 事件
    /// </summary>
    private void ParseAndTriggerMessageEvent(ReadOnlyMemory<byte> data)
    {
        try
        {
            // 尝试解析消息包
            if (MessagePacket.TryReadFrom(data.Span, out var messagePacket))
            {
                _logger?.LogTrace("[消息解析] {ConnectionId} 成功解析消息包: 服务={ServiceName}, 方法={MethodName}, 类型={Type}, ID={MessageId}",
                    ConnectionId, messagePacket.Header.ServiceName, messagePacket.Header.MethodName,
                    messagePacket.Header.Type, messagePacket.Header.MessageId);

                // 创建消息包持有者（避免 ref struct 的生命周期问题）
                var messagePacketHolder = new MessagePacketHolder(messagePacket);

                // 触发消息解析完成事件
                var parsedEventArgs = new MessageParsedEventArgs(
                    ConnectionId,
                    messagePacketHolder,
                    DateTime.UtcNow,
                    0 // ProcessorId 暂时设为0，可以后续优化
                );

                MessageParsed?.Invoke(this, parsedEventArgs);

                _logger?.LogTrace("[消息解析] {ConnectionId} 消息解析事件已触发，订阅者数量: {SubscriberCount}",
                    ConnectionId, MessageParsed?.GetInvocationList()?.Length ?? 0);
            }
            else
            {
                _logger?.LogWarning("[消息解析] {ConnectionId} 消息包解析失败: Size={Size} bytes, Data=[{DataHex}]",
                    ConnectionId, data.Length, Convert.ToHexString(data.Span[..Math.Min(data.Length, 128)]));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[消息解析] {ConnectionId} 处理接收数据时发生异常: Size={Size} bytes",
                ConnectionId, data.Length);
        }
    }


    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 取消订阅事件
        _transport.StateChanged -= OnTransportStateChanged;
        _transport.DataReceived -= OnTransportDataReceived;

        // 清理认证信息
        ClearAuthentication();

        // 清理属性
        _properties.Clear();

        // 释放传输资源
        _transport.Dispose();
    }
}
