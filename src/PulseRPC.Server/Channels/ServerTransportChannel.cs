using System.Collections.Concurrent;
using PulseRPC.Authentication;
using PulseRPC.Channels;
using PulseRPC.Transport;
using Microsoft.Extensions.Logging;
using System.Net;
using PulseRPC.Messaging;
using PulseRPC.Server.Security;
using MessageProcessedEventArgs = PulseRPC.Server.Processing.Engine.MessageProcessedEventArgs;
using ConnectionState = PulseRPC.Transport.ConnectionState;

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
    /// 异步发送数据
    /// </summary>
    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// [Server→Client Reverse Ask] 向该连接的客户端发起「需应答」的反向请求，并等待客户端应答。
    /// </summary>
    /// <param name="protocolId">协议号（由源生成器生成）</param>
    /// <param name="payload">已序列化的请求载荷</param>
    /// <param name="timeout">等待应答的超时时间；小于等于 <see cref="TimeSpan.Zero"/> 时使用默认超时。</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>客户端返回的已序列化响应载荷。</returns>
    /// <remarks>
    /// 关联键为 <see cref="MessageHeader.MessageId"/>。客户端以 <see cref="MessageType.Response"/> 表示成功、
    /// <see cref="MessageType.Error"/> 表示失败进行应答。超时抛出 <see cref="TimeoutException"/>；
    /// 连接断开、客户端处理异常等抛出 <see cref="PulseReverseCallException"/>。
    /// </remarks>
    Task<ReadOnlyMemory<byte>> InvokeClientAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("当前 IServerChannel 实现不支持服务端→客户端反向调用（Reverse Ask）。");

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
public sealed class ServerTransportChannel : TransportChannelBase, IServerChannel
{
    private readonly IServerTransport _transport;
    private readonly ConcurrentDictionary<string, object> _properties;
    private readonly Lock _authLock = new Lock();
    private readonly ILogger<ServerTransportChannel>? _logger;

    private IAuthenticationContext? _authenticationContext;
    private DateTime _lastActiveTime;
    private bool _disposed;

    // === [P-4] 服务端→客户端反向 Ask（Reverse Ask）挂起状态 ===

    /// <summary>反向调用默认超时时间</summary>
    private static readonly TimeSpan DefaultReverseCallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 挂起中的反向调用（按连接归属，天然支持断线兜底）。
    /// 键为 <see cref="MessageHeader.MessageId"/>。
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ReverseCallState> _pendingReverseCalls = new();

    private sealed class ReverseCallState
    {
        public ReverseCallState(TaskCompletionSource<ReadOnlyMemory<byte>> tcs) => Tcs = tcs;

        public TaskCompletionSource<ReadOnlyMemory<byte>> Tcs { get; }
    }

    // === TransportChannelBase 抽象成员实现 ===

    /// <inheritdoc />
    public override string ConnectionId => _transport.Id;

    /// <inheritdoc />
    public override bool IsConnected => _transport.IsConnected;

    /// <inheritdoc />
    public override EndPoint? RemoteEndPoint => _transport.RemoteEndPoint!;

    /// <inheritdoc />
    public override EndPoint? LocalEndPoint => _transport.LocalEndPoint!;

    /// <inheritdoc />
    public override DateTime ConnectedAt => ConnectedTime;

    /// <inheritdoc />
    public override DateTime LastActivityAt => _lastActiveTime;

    /// <inheritdoc />
    public override Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult(false);

        LastActiveTime = DateTime.UtcNow;
        return _transport.SendAsync(data, cancellationToken);
    }

    // === IServerChannel 显式实现（处理非空要求）===

    EndPoint IServerChannel.RemoteEndPoint => RemoteEndPoint!;
    EndPoint IServerChannel.LocalEndPoint => LocalEndPoint!;

    // === IServerChannel 实现（向后兼容）===

    public string Id => ConnectionId;
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
    public IServerTransport Transport => _transport;

    #region ITransportConnection Implementation
    /// <inheritdoc />
    public ConnectionState State => _transport.State;

    /// <inheritdoc />
    public TransportType TransportType => _transport.Type;

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

            // [P-4] 断线兜底：使所有挂起的反向调用失败
            FailAllPendingReverseCalls("连接已断开，反向调用（Reverse Ask）被中止。");
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
    /// [P-4] 向该连接的客户端发起「需应答」的反向请求，并等待客户端应答。
    /// </summary>
    public async Task<ReadOnlyMemory<byte>> InvokeClientAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new PulseReverseCallException("连接已关闭，无法发起反向调用（Reverse Ask）。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var effectiveTimeout = timeout <= TimeSpan.Zero ? DefaultReverseCallTimeout : timeout;
        var messageId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new ReverseCallState(tcs);

        if (!_pendingReverseCalls.TryAdd(messageId, state))
        {
            throw new PulseReverseCallException("反向调用消息ID发生冲突，无法发起反向调用。");
        }

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // 超时 / 取消兜底
        using var registration = linkedCts.Token.Register(() =>
        {
            if (_pendingReverseCalls.TryRemove(messageId, out var removed))
            {
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    removed.Tcs.TrySetException(new TimeoutException(
                        $"反向调用超时（{effectiveTimeout.TotalMilliseconds:F0}ms）：ProtocolId=0x{protocolId:X4}, MessageId={messageId}"));
                }
                else
                {
                    removed.Tcs.TrySetCanceled(cancellationToken);
                }
            }
        });

        try
        {
            // 构建 ReverseRequest 帧：[4字节头长度][MemoryPack 头][载荷]
            var header = new MessageHeader(MessageType.ReverseRequest, string.Empty, string.Empty)
            {
                MessageId = messageId,
                ProtocolId = protocolId,
                Flags = MessageFlags.RequireResponse,
                Timestamp = DateTimeOffset.UtcNow.Ticks
            };

            var packet = new MessagePacket(header, payload.Span);
            using var buffer = System.Buffers.MemoryPool<byte>.Shared.Rent(packet.EstimateSize());
            var bytesWritten = packet.WriteTo(buffer.Memory.Span);

            var sent = await SendAsync(buffer.Memory[..bytesWritten], cancellationToken).ConfigureAwait(false);
            if (!sent && _pendingReverseCalls.TryRemove(messageId, out var removed))
            {
                removed.Tcs.TrySetException(new PulseReverseCallException("反向调用发送失败：连接不可用。"));
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not TimeoutException and not OperationCanceledException and not PulseReverseCallException)
        {
            _pendingReverseCalls.TryRemove(messageId, out _);
            throw new PulseReverseCallException("反向调用失败。", ex);
        }
        finally
        {
            _pendingReverseCalls.TryRemove(messageId, out _);
        }
    }

    /// <summary>
    /// 尝试用客户端应答（Response/Error）完成挂起的反向调用。
    /// </summary>
    /// <returns>命中挂起调用返回 true，否则返回 false。</returns>
    private bool TryCompleteReverseCall(MessageHeader header, ReadOnlySpan<byte> payload)
    {
        if (!_pendingReverseCalls.TryRemove(header.MessageId, out var state))
        {
            return false;
        }

        if (header.Type == MessageType.Error)
        {
            var message = "客户端反向调用处理失败。";
            string? errorCode = null;
            try
            {
                if (!payload.IsEmpty)
                {
                    var error = MemoryPack.MemoryPackSerializer.Deserialize<ErrorResponse>(payload);
                    if (error != null)
                    {
                        errorCode = string.IsNullOrEmpty(error.ErrorCode) ? null : error.ErrorCode;
                        if (!string.IsNullOrEmpty(error.ErrorMessage))
                        {
                            message = error.ErrorMessage;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[反向调用] {ConnectionId} 解析错误响应失败，MessageId={MessageId}", ConnectionId, header.MessageId);
            }

            state.Tcs.TrySetException(new PulseReverseCallException(message, errorCode));
        }
        else
        {
            // 成功：复制载荷（payload 位于复用的接收缓冲区之上）
            state.Tcs.TrySetResult(payload.ToArray());
        }

        return true;
    }

    /// <summary>
    /// 断线兜底：使所有挂起的反向调用失败。
    /// </summary>
    private void FailAllPendingReverseCalls(string reason)
    {
        if (_pendingReverseCalls.IsEmpty)
        {
            return;
        }

        foreach (var kvp in _pendingReverseCalls)
        {
            if (_pendingReverseCalls.TryRemove(kvp.Key, out var state))
            {
                state.Tcs.TrySetException(new PulseReverseCallException(reason));
            }
        }
    }

    /// <summary>
    /// 处理接收到的数据，解析消息包并触发 MessageParsed 事件
    /// </summary>
    private void ProcessReceivedData(ReadOnlyMemory<byte> data)
    {
        try
        {
            // 尝试解析消息包
            if (MessagePacket.TryReadFrom(data.Span, out var messagePacket))
            {
                _logger?.LogTrace("[消息解析] {ConnectionId} 成功解析消息包: 服务={ServiceName}, 方法={MethodName}, 类型={Type}, ID={MessageId}",
                    ConnectionId, messagePacket.Header.ServiceName, messagePacket.Header.MethodName,
                    messagePacket.Header.Type, messagePacket.Header.MessageId);

                // 特殊处理：系统消息（Ping）直接在这里处理，不进入消息处理管道
                if (messagePacket.Header.Type == MessageType.Ping)
                {
                    _logger?.LogTrace("[系统消息] {ConnectionId} 收到Ping消息，直接回复Pong", ConnectionId);
                    _ = HandlePingMessageAsync(messagePacket.Header.MessageId);
                    return; // 不触发 MessageParsed 事件
                }

                // [P-4] 特殊处理：客户端对「反向请求」的应答（Response/Error）。
                // 正常客户端不会向服务端发送 Response/Error，因此在此拦截并完成挂起的反向调用，不进入消息处理管道。
                if (messagePacket.Header.Type == MessageType.Response || messagePacket.Header.Type == MessageType.Error)
                {
                    if (TryCompleteReverseCall(messagePacket.Header, messagePacket.Payload))
                    {
                        return; // 已由反向调用消费
                    }

                    _logger?.LogWarning("[反向调用] {ConnectionId} 收到未匹配的应答（可能已超时或断线），Type={Type}, MessageId={MessageId}",
                        ConnectionId, messagePacket.Header.Type, messagePacket.Header.MessageId);
                    return;
                }

                // 创建消息包持有者（避免 ref struct 的生命周期问题）
                var messagePacketHolder = new MessagePacketHolder(messagePacket);

                // 触发消息解析完成事件（仅处理业务消息）
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

    /// <summary>
    /// 处理 Ping 消息并回复 Pong
    /// </summary>
    private async Task HandlePingMessageAsync(Guid messageId)
    {
        try
        {
            // 创建 Pong 响应消息头
            var pongHeader = new MessageHeader(MessageType.Pong, string.Empty, string.Empty)
            {
                MessageId = messageId,
                Flags = MessageFlags.None
            };

            // 创建空的 Pong 消息包
            var pongPacket = new MessagePacket(pongHeader, ReadOnlySpan<byte>.Empty);

            // 序列化并发送
            using var buffer = System.Buffers.MemoryPool<byte>.Shared.Rent(pongPacket.EstimateSize());
            var bytesWritten = pongPacket.WriteTo(buffer.Memory.Span);

            await SendAsync(buffer.Memory[..bytesWritten], CancellationToken.None);

            _logger?.LogTrace("[系统消息] {ConnectionId} Pong响应已发送，消息ID={MessageId}", ConnectionId, messageId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[系统消息] {ConnectionId} 处理Ping消息失败，消息ID={MessageId}", ConnectionId, messageId);
        }
    }


    /// <inheritdoc />
    public new void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 取消订阅事件
        _transport.StateChanged -= OnTransportStateChanged;
        _transport.DataReceived -= OnTransportDataReceived;

        // [P-4] 断线兜底：使所有挂起的反向调用失败
        FailAllPendingReverseCalls("连接已释放，反向调用（Reverse Ask）被中止。");

        // 清理认证信息
        ClearAuthentication();

        // 清理属性
        _properties.Clear();

        // 释放传输资源
        _transport.Dispose();

        // 调用基类释放
        base.Dispose();
    }

    /// <summary>
    /// 释放资源（防御性：当通过 <see cref="IDisposable"/> 接口引用释放时，
    /// 基类 <c>Dispose()</c> 会调用此虚方法，确保反向调用挂起状态在任一释放路径下都被清理）。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // [P-4] 断线兜底：使所有挂起的反向调用失败（幂等，可与 Dispose() 重复调用）
            FailAllPendingReverseCalls("连接已释放，反向调用（Reverse Ask）被中止。");
        }

        base.Dispose(disposing);
    }
}
