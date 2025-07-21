using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 连接状态改变事件参数
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState OldState { get; }
    public ConnectionState NewState { get; }
    public Exception? Exception { get; }

    public ConnectionStateChangedEventArgs(ConnectionState oldState, ConnectionState newState,
        Exception? exception = null)
    {
        OldState = oldState;
        NewState = newState;
        Exception = exception;
    }
}

/// <summary>
/// 基于传输层的消息通道
/// </summary>
public class TransportChannel : IClientChannel
{
    private readonly string _name;
    private readonly IClientTransport _transport;
    private readonly ISerializerProvider _serializerProvider;
    private readonly TransportChannelOptions _options;
    private readonly Dictionary<Guid, TaskCompletionSource<NetworkMessage>> _pendingRequests = new();
    private readonly Dictionary<string, List<EventSubscription>> _eventSubscriptions = new();
    private readonly object _syncRoot = new object();
    private readonly ILogger<TransportChannel> _logger;
    private readonly Channel<NetworkMessage> _messageQueue;
    private readonly Task[] _messageProcessingTasks;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private DateTime _lastHeartbeatTime;
    private Task? _heartbeatTask;
    private bool _disposed;

    public string Name => _name;
    public bool IsConnected => _transport.IsConnected;
    public ConnectionState ConnectionState => _transport.State;

    // 事件回调
    private Action<string, byte[]>? _eventCallback;

    public event System.EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public TransportChannel(
        string name,
        IClientTransport transport,
        ISerializerProvider serializerProvider,
        TransportChannelOptions? options = null,
        ILogger<TransportChannel>? logger = null)
    {
        _name = name;
        _transport = transport;
        _serializerProvider = serializerProvider;
        _options = options ?? new TransportChannelOptions();
        _logger = logger ?? NullLogger<TransportChannel>.Instance;
        _messageQueue = Channel.CreateBounded<NetworkMessage>(new BoundedChannelOptions(_options.MessageQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        // 启动消息处理任务
        _messageProcessingTasks = new Task[_options.MessageProcessingConcurrency];
        for (var i = 0; i < _options.MessageProcessingConcurrency; i++)
        {
            _messageProcessingTasks[i] = Task.Run(ProcessMessageQueueAsync);
        }

        // 注册传输事件
        _transport.DataReceived += OnTransportDataReceived;
        _transport.StateChanged += OnTransportStateChanged;

        // 启动心跳任务
        if (_options.HeartbeatInterval > TimeSpan.Zero)
        {
            _heartbeatTask = Task.Run(SendHeartbeatAsync);
        }
    }

    /// <summary>
    /// 注册事件回调
    /// </summary>
    public void RegisterEventCallback(Action<string, byte[]> callback)
    {
        _eventCallback = callback;
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return _transport.ConnectAsync(host, port, cancellationToken);
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _transport.DisconnectAsync(cancellationToken);
    }

    /// <summary>
    /// 处理消息队列
    /// </summary>
    private async Task ProcessMessageQueueAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 从队列中读取消息
                    var message = await _messageQueue.Reader.ReadAsync(_cts.Token);

                    // 使用超时处理消息
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    timeoutCts.CancelAfter(_options.MessageProcessingTimeout);

                    try
                    {
                        await Task.Run(() => ProcessMessage(message), timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        _logger.LogWarning("[TransportChannel] 消息处理超时: Type={MessageType}, MessageId={MessageId}",
                            message.Header.Type, message.Header.MessageId);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TransportChannel] 处理消息队列时发生异常");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 消息处理任务异常退出");
        }
    }

    /// <summary>
    /// 发送心跳
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.HeartbeatInterval, _cts.Token);

                    if (!IsConnected)
                    {
                        continue;
                    }

                    // 检查是否需要发送心跳
                    var now = DateTime.UtcNow;
                    if (now - _lastHeartbeatTime < _options.HeartbeatInterval)
                    {
                        continue;
                    }

                    await SendHeartbeatAsync(_cts.Token);
                    _lastHeartbeatTime = now;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TransportChannel] 发送心跳时发生异常");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 心跳任务异常退出");
        }
    }

    /// <summary>
    /// 发送心跳消息
    /// </summary>
    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            var header = new MessageHeader { Type = MessageType.Ping, MessageId = Guid.NewGuid() };

            var serializedMessage = SerializeAndPackMessage<object>(header, null);
            await _transport.SendAsync(serializedMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 发送心跳消息失败");
        }
    }

    /// <summary>
    /// 发送请求并等待响应
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        string serviceName, string methodName, TRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[TransportChannel] 开始发送请求: Service={ServiceName}, Method={MethodName}",
            serviceName, methodName);

        // 创建消息头
        var messageId = Guid.NewGuid();
        var header = new MessageHeader
        {
            Type = MessageType.Request,
            MessageId = messageId,
            ServiceName = serviceName,
            MethodName = methodName
        };

        // 创建待处理请求
        var tcs = new TaskCompletionSource<NetworkMessage>();
        lock (_syncRoot)
        {
            _pendingRequests[messageId] = tcs;
        }

        try
        {
            // 序列化并发送请求
            _logger.LogDebug("[TransportChannel] 序列化请求数据: MessageId={MessageId}", messageId);
            var serializedMessage = SerializeAndPackMessage(header, request);

            // 重试机制
            var retryCount = 0;
            var sendSuccess = false;
            var lastException = default(Exception);

            while (retryCount < _options.MaxRetryCount && !sendSuccess)
            {
                try
                {
                    _logger.LogDebug(
                        "[TransportChannel] 尝试发送请求 (尝试 {RetryCount}/{MaxRetryCount}): MessageId={MessageId}",
                        retryCount + 1, _options.MaxRetryCount, messageId);

                    await _sendLock.WaitAsync(cancellationToken);
                    try
                    {
                        sendSuccess = await _transport.SendAsync(serializedMessage, cancellationToken);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }

                    if (!sendSuccess)
                    {
                        throw new IOException("发送请求失败");
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retryCount++;

                    if (retryCount < _options.MaxRetryCount)
                    {
                        _logger.LogWarning(ex,
                            "[TransportChannel] 发送请求失败，准备重试: MessageId={MessageId}, RetryCount={RetryCount}",
                            messageId, retryCount);

                        // 指数退避
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(_options.RetryBaseDelay, retryCount)),
                            cancellationToken);
                    }
                }
            }

            if (!sendSuccess)
            {
                throw new IOException($"发送请求失败，已重试 {_options.MaxRetryCount} 次", lastException);
            }

            // 等待响应，设置超时
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.DefaultTimeout);

            _logger.LogDebug("[TransportChannel] 等待响应: MessageId={MessageId}, Timeout={Timeout}ms",
                messageId, _options.DefaultTimeout.TotalMilliseconds);

            NetworkMessage response;
            try
            {
                response = await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogError(
                    "[TransportChannel] 请求超时: Service={ServiceName}, Method={MethodName}, MessageId={MessageId}",
                    serviceName, methodName, messageId);
                throw new TimeoutException($"请求 {methodName} 超时");
            }

            _logger.LogDebug("[TransportChannel] 收到响应: MessageId={MessageId}", messageId);

            // 反序列化响应
            if (typeof(TResponse) == typeof(EmptyResponse))
            {
                return (TResponse)(object)EmptyResponse.Instance;
            }
            else
            {
                try
                {
                    var result = _serializerProvider.Create(MethodType.Unary, null)
                        .Deserialize<TResponse>(new ReadOnlySequence<byte>(response.Body));

                    _logger.LogInformation(
                        "[TransportChannel] 请求处理完成: Service={ServiceName}, Method={MethodName}, MessageId={MessageId}",
                        serviceName, methodName, messageId);

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TransportChannel] 反序列化响应失败: MessageId={MessageId}", messageId);
                    throw new InvalidOperationException("反序列化响应失败", ex);
                }
            }
        }
        catch (Exception ex) when (ex is not TimeoutException && ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[TransportChannel] 处理请求时发生异常: Service={ServiceName}, Method={MethodName}, MessageId={MessageId}",
                serviceName, methodName, messageId);
            throw;
        }
        finally
        {
            lock (_syncRoot)
            {
                _pendingRequests.Remove(messageId);
            }
        }
    }

    /// <summary>
    /// 发送事件
    /// </summary>
    public async Task SendEventAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default)
    {
        // 创建消息头
        var header = new MessageHeader
        {
            Type = MessageType.Event, MessageId = Guid.NewGuid(), ServiceName = string.Empty, MethodName = eventName
        };

        // 序列化并发送事件
        var serializedMessage = SerializeAndPackMessage(header, eventData);
        await _transport.SendAsync(serializedMessage, cancellationToken);
    }

    /// <summary>
    /// 订阅事件
    /// </summary>
    public ISubscriptionToken SubscribeToEvent<T>(string eventName, System.EventHandler<T> handler)
    {
        lock (_syncRoot)
        {
            if (!_eventSubscriptions.TryGetValue(eventName, out var subscriptions))
            {
                subscriptions = new List<EventSubscription>();
                _eventSubscriptions[eventName] = subscriptions;
            }

            var subscription = new EventSubscription<T>(eventName, handler);
            subscriptions.Add(subscription);

            return new SubscriptionToken(subscription.Id, eventName, typeof(T),
                () => UnsubscribeEvent(eventName, subscription.Id));
        }
    }

    /// <summary>
    /// 取消订阅事件
    /// </summary>
    private void UnsubscribeEvent(string eventName, Guid subscriptionId)
    {
        lock (_syncRoot)
        {
            if (!_eventSubscriptions.TryGetValue(eventName, out var subscriptions))
            {
                return;
            }

            var subscription = subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
            if (subscription == null)
            {
                return;
            }

            subscriptions.Remove(subscription);

            // 如果没有更多订阅，移除事件
            if (subscriptions.Count == 0)
            {
                _eventSubscriptions.Remove(eventName);
            }
        }
    }

    /// <summary>
    /// 序列化并打包消息
    /// </summary>
    private ReadOnlyMemory<byte> SerializeAndPackMessage<T>(MessageHeader header, T? payload)
    {
        // 序列化消息头
        var serializer = _serializerProvider.Create(MethodType.Unary, null);

        var headerWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(headerWriter, in header);
        var headerSpan = headerWriter.WrittenSpan;

        // 序列化载荷
        var payloadWriter = new ArrayBufferWriter<byte>();
        ReadOnlySpan<byte> payloadSpan = default;
        if (payload != null)
        {
            serializer.Serialize(payloadWriter, in payload);
            payloadSpan = payloadWriter.WrittenSpan;
        }

        // 计算总大小并一次性分配
        var headerSize = headerSpan.Length;
        var payloadSize = payloadSpan.Length;
        var totalSize = sizeof(int) + headerSize + payloadSize;

        var result = new byte[totalSize];
        PackMessage(result.AsSpan(), headerSpan, payloadSpan);
        return result;
    }

    private void SerializeAndPackMessage<T>(IBufferWriter<byte> writer, MessageHeader header, T? payload)
    {
        // 序列化消息头
        var serializer = _serializerProvider.Create(MethodType.Unary, null);

        // 序列化头部
        var headerWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(headerWriter, in header);
        var headerSpan = headerWriter.WrittenSpan;

        // 序列化载荷
        var payloadWriter = new ArrayBufferWriter<byte>();
        ReadOnlySpan<byte> payloadSpan = default;

        if (payload != null)
        {
            serializer.Serialize(payloadWriter, in payload);
            payloadSpan = payloadWriter.WrittenSpan;
        }

        // 计算所需空间并获取目标缓冲区
        var totalSize = sizeof(int) + headerSpan.Length + payloadSpan.Length;
        var targetSpan = writer.GetSpan(totalSize);

        // 直接打包到目标缓冲区
        var bytesWritten = PackMessage(targetSpan, headerSpan, payloadSpan);
        writer.Advance(bytesWritten);
    }

    /// <summary>
    /// 将消息头和载荷打包到目标缓冲区
    /// </summary>
    /// <param name="destination">目标缓冲区</param>
    /// <param name="headerSpan">序列化后的消息头</param>
    /// <param name="payloadSpan">序列化后的载荷</param>
    /// <returns>写入的字节数</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PackMessage(Span<byte> destination, ReadOnlySpan<byte> headerSpan,
        ReadOnlySpan<byte> payloadSpan)
    {
        var headerSize = headerSpan.Length;
        var payloadSize = payloadSpan.Length;
        var totalSize = sizeof(int) + headerSize + payloadSize;

        if (destination.Length < totalSize)
        {
            throw new ArgumentException($"目标缓冲区太小。需要 {totalSize} 字节，但只有 {destination.Length} 字节", nameof(destination));
        }

        var offset = 0;

        // 写入消息头长度 (Little Endian)
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset), headerSize);
        offset += sizeof(int);

        // 写入消息头
        headerSpan.CopyTo(destination.Slice(offset));
        offset += headerSize;

        // 写入载荷
        if (payloadSize > 0)
        {
            payloadSpan.CopyTo(destination.Slice(offset));
            offset += payloadSize;
        }

        return offset;
    }

    /// <summary>
    /// 处理传输层数据接收
    /// </summary>
    private void OnTransportDataReceived(object? sender, TransportDataEventArgs e)
    {
        try
        {
            // TcpTransport 发送的数据已经是应用层消息格式：[HeaderLength(4)] + [Header] + [Payload]
            // 不需要再处理传输层头部

            var data = e.Data;
            if (data.Length < 4)
            {
                _logger.LogWarning("收到的消息太短，无法包含头部长度");
                return;
            }

            // 读取头部长度（小端序）
            var headerLengthBytes = data[..4].ToArray();
            var headerLength = BitConverter.ToInt32(headerLengthBytes, 0);

            // 检查头部长度合法性
            if (headerLength <= 0 || headerLength > data.Length - 4)
            {
                _logger.LogWarning("收到无效的消息头长度: {HeaderLength}, 数据总长度: {DataLength}", headerLength, data.Length);
                return;
            }

            // 读取头部
            var headerBytes = data.Slice(4, headerLength).ToArray();
            var header = _serializerProvider.Create(MethodType.Unary, null)
                .Deserialize<Messaging.MessageHeader>(new ReadOnlySequence<byte>(headerBytes));

            // 读取消息体
            var bodyStartIndex = 4 + headerLength;
            var bodyLength = data.Length - bodyStartIndex;
            var bodyBytes = bodyLength > 0 ? data.Slice(bodyStartIndex, bodyLength).ToArray() : Array.Empty<byte>();

            // 创建网络消息
            var message = new NetworkMessage(header, bodyBytes);

            // 处理消息
            // ProcessMessage(message);

            // 将消息加入队列
            if (!_messageQueue.Writer.TryWrite(message))
            {
                _logger.LogWarning("消息队列已满，丢弃消息: Type={MessageType}, MessageId={MessageId}",
                    message.Header.Type, message.Header.MessageId);
            }
            else
            {
                _logger.LogDebug("接收到新消息: Type={MessageType}, MessageId={MessageId}",
                    message.Header.Type, message.Header.MessageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理接收数据失败");
        }
    }

    /// <summary>
    /// 处理传输层状态变化
    /// </summary>
    private void OnTransportStateChanged(object? sender, TransportStateEventArgs e)
    {
        try
        {
            // 转换为通道状态事件
            var eventArgs = new ConnectionStateChangedEventArgs(
                e.PreviousState,
                e.CurrentState,
                e.Exception);

            // 触发状态变化事件
            ConnectionStateChanged?.Invoke(this, eventArgs);

            // 如果断开连接，取消所有待处理请求
            if (e.CurrentState == ConnectionState.Disconnected)
            {
                Dictionary<Guid, TaskCompletionSource<NetworkMessage>> pendingRequests;
                lock (_syncRoot)
                {
                    pendingRequests = new Dictionary<Guid, TaskCompletionSource<NetworkMessage>>(_pendingRequests);
                    _pendingRequests.Clear();
                }

                // 设置所有请求失败
                foreach (var request in pendingRequests.Values)
                {
                    request.TrySetException(new IOException("连接断开"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 处理状态变化时发生异常");
        }
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private void ProcessMessage(NetworkMessage message)
    {
        _logger.LogDebug("[TransportChannel] 开始处理消息: Type={MessageType}, MessageId={MessageId}",
            message.Header.Type, message.Header.MessageId);

        try
        {
            switch (message.Header.Type)
            {
                case MessageType.Response:
                    ProcessResponse(message);
                    break;

                case MessageType.Event:
                    ProcessEvent(message);
                    break;

                case MessageType.Ping:
                    ProcessPing(message);
                    break;

                default:
                    _logger.LogWarning("[TransportChannel] 收到未支持的消息类型: {Type}, MessageId={MessageId}",
                        message.Header.Type, message.Header.MessageId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 处理消息时发生异常: Type={MessageType}, MessageId={MessageId}",
                message.Header.Type, message.Header.MessageId);
        }
    }

    /// <summary>
    /// 处理响应消息
    /// </summary>
    private void ProcessResponse(NetworkMessage message)
    {
        _logger.LogDebug("[TransportChannel] 处理响应消息: MessageId={MessageId}", message.Header.MessageId);

        TaskCompletionSource<NetworkMessage>? tcs;

        lock (_syncRoot)
        {
            if (!_pendingRequests.Remove(message.Header.MessageId, out tcs))
            {
                _logger.LogWarning("[TransportChannel] 收到未匹配的响应消息: MessageId={MessageId}", message.Header.MessageId);
                return;
            }
        }

        tcs.TrySetResult(message);
        _logger.LogDebug("[TransportChannel] 响应消息处理完成: MessageId={MessageId}", message.Header.MessageId);
    }

    /// <summary>
    /// 处理事件消息
    /// </summary>
    private void ProcessEvent(NetworkMessage message)
    {
        var eventName = message.Header.MethodName;
        _logger.LogDebug("[TransportChannel] 处理事件消息: EventName={EventName}, MessageId={MessageId}",
            eventName, message.Header.MessageId);

        try
        {
            // 调用事件回调
            _eventCallback?.Invoke(eventName, message.Body);

            List<EventSubscription>? subscriptions;
            lock (_syncRoot)
            {
                if (!_eventSubscriptions.TryGetValue(eventName, out subscriptions))
                {
                    _logger.LogDebug("[TransportChannel] 没有找到事件订阅者: EventName={EventName}", eventName);
                    return;
                }

                // 复制列表，避免回调期间修改集合
                subscriptions = new List<EventSubscription>(subscriptions);
            }

            // 通知所有订阅者
            foreach (var subscription in subscriptions)
            {
                try
                {
                    subscription.Invoke(message.Body, _serializerProvider);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TransportChannel] 事件处理异常: EventName={EventName}, MessageId={MessageId}",
                        eventName, message.Header.MessageId);
                }
            }

            _logger.LogDebug("[TransportChannel] 事件消息处理完成: EventName={EventName}, MessageId={MessageId}",
                eventName, message.Header.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 处理事件消息时发生异常: EventName={EventName}, MessageId={MessageId}",
                eventName, message.Header.MessageId);
        }
    }

    /// <summary>
    /// 处理Ping消息
    /// </summary>
    private async void ProcessPing(NetworkMessage message)
    {
        try
        {
            // 创建Pong响应
            var header = new MessageHeader { Type = MessageType.Pong, MessageId = message.Header.MessageId };

            // 序列化并发送Pong
            var serializedMessage = SerializeAndPackMessage<object>(header, null);
            await _transport.SendAsync(serializedMessage, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送Pong响应失败");
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

        try
        {
            // 取消所有任务
            _cts.Cancel();

            // 等待消息处理任务完成
            Task.WaitAll(_messageProcessingTasks, TimeSpan.FromSeconds(5));

            // 关闭消息队列
            _messageQueue.Writer.Complete();

            // 取消心跳任务
            _heartbeatTask?.Wait(TimeSpan.FromSeconds(5));

            // 释放资源
            _sendLock.Dispose();
            _cts.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TransportChannel] 释放资源时发生异常");
        }
    }

    // 内部类：事件订阅基类
    private abstract class EventSubscription
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string EventName { get; }

        protected EventSubscription(string eventName)
        {
            EventName = eventName;
        }

        public abstract void Invoke(byte[] data, ISerializerProvider serializerProvider);
    }

    // 内部类：泛型事件订阅
    private class EventSubscription<T> : EventSubscription
    {
        private readonly System.EventHandler<T> _handler;

        public EventSubscription(string eventName, System.EventHandler<T> handler)
            : base(eventName)
        {
            _handler = handler;
        }

        public override void Invoke(byte[] data, ISerializerProvider serializerProvider)
        {
            var eventData = serializerProvider.Create(MethodType.Unary, null)
                .Deserialize<T>(new ReadOnlySequence<byte>(data));
            _handler.Invoke(this, eventData);
        }
    }
}
