using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Channels;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 优化的传输通道 - 减少热路径内存分配
/// </summary>
internal class TransportChannel : TransportChannelBase, IClientChannel
{
    private readonly IClientTransport _transport;
    private readonly ISerializerProvider _serializerProvider;
    private readonly TransportChannelOptions _options;
    private readonly Dictionary<Guid, TaskCompletionSource<NetworkMessage>> _pendingRequests = new(); // 保留向后兼容
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

    // ============================================================================
    // 零拷贝优化组件
    // ============================================================================

    // 三层响应管理器（替代 _pendingRequests）
    private readonly ResponseContextManager _responseManager;

    // 三层发送缓冲（批量零拷贝发送）
    private readonly ThreeTierSendBuffer _sendBuffer;

    // 零拷贝事件处理器字典（eventName -> 反序列化委托列表）
    private readonly Dictionary<string, List<Action<ReadOnlyMemory<byte>>>> _zeroCopyEventHandlers = new();

    // 优化: 预分配的缓冲区和线程本地存储
    private static readonly ThreadLocal<ArrayBufferWriter<byte>> ThreadLocalBufferWriter =
        new(() => new ArrayBufferWriter<byte>(4096));

    private static readonly ThreadLocal<byte[]> ThreadLocalTempBuffer =
        new(() => new byte[8192]);

    // 优化: 预分配的消息头池
    private readonly UnityCompatibleObjectPool<MessageHeader> _messageHeaderPool;
    private readonly UnityCompatibleObjectPool<ArrayBufferWriter<byte>> _bufferWriterPool;

    // 事件回调
    private Action<string, byte[]>? _eventCallback;

    // === TransportChannelBase 抽象成员实现 ===

    /// <inheritdoc />
    public override string ConnectionId => Descriptor.Id;

    /// <inheritdoc />
    public override bool IsConnected => _transport.IsConnected;

    /// <inheritdoc />
    public override EndPoint? RemoteEndPoint => _transport.RemoteEndPoint;

    /// <inheritdoc />
    public override EndPoint? LocalEndPoint => _transport.LocalEndPoint;

    /// <inheritdoc />
    public override DateTime ConnectedAt => Statistics.ConnectedAt ?? DateTime.UtcNow;

    /// <inheritdoc />
    public override DateTime LastActivityAt => Statistics.LastActiveAt;

    /// <inheritdoc />
    public override Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        return _transport.SendAsync(data, cancellationToken);
    }

    // === IClientChannel 实现（向后兼容）===

    public string Id => ConnectionId;

    // IClientChannel properties that integrate IConnection functionality
    public ConnectionDescriptor Descriptor { get; private set; }
    public ExtendedConnectionState State => _transport.State.ToExtended();
    public ConnectionStatistics Statistics { get; private set; }
    public Dictionary<string, string> Tags => Descriptor?.Tags ?? new Dictionary<string, string>();

    public event EventHandler<TransportStateEventArgs>? ConnectionStateChanged;

    // Backward compatibility - keep old event for existing code
    public event EventHandler<ConnectionStateChangedEventArgs>? LegacyConnectionStateChanged;

    public TransportChannel(
        IClientTransport transport,
        ISerializerProvider serializerProvider,
        TransportChannelOptions? options = null,
        ILogger<TransportChannel>? logger = null)
    {
        _transport = transport;
        _serializerProvider = serializerProvider;
        _options = options ?? new TransportChannelOptions();
        _logger = logger ?? NullLogger<TransportChannel>.Instance;

        // Initialize IConnection integration properties
        Descriptor = new ConnectionDescriptor
        {
            Id = transport.Id,
            Name = transport.Id,
            Transport = transport.Type,
            Strategy = ConnectionStrategy.Session
        };
        Statistics = new ConnectionStatistics
        {
            ConnectionId = transport.Id,
            CreatedAt = DateTime.UtcNow
        };

        // 初始化对象池
        _messageHeaderPool = new UnityCompatibleObjectPool<MessageHeader>(() => new MessageHeader(), ResetMessageHeader, 32);
        _bufferWriterPool = new UnityCompatibleObjectPool<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(4096), ResetBufferWriter, 32);

        // 初始化零拷贝组件
        _responseManager = new ResponseContextManager(shardCount: 16, defaultTimeout: _options.DefaultTimeout);
        _sendBuffer = new ThreeTierSendBuffer(_transport, l1BatchSize: 16, l2BatchSize: 64, queueCapacity: 1024);

        _messageQueue = Channel.CreateBounded<NetworkMessage>(new BoundedChannelOptions(_options.MessageQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _messageProcessingTasks = new Task[_options.MessageProcessingConcurrency];
        for (var i = 0; i < _options.MessageProcessingConcurrency; i++)
        {
            _messageProcessingTasks[i] = Task.Run(ProcessMessageQueueAsync);
        }

        _transport.DataReceived += OnTransportDataReceived;
        _transport.StateChanged += OnTransportStateChanged;

        if (_options.HeartbeatInterval > TimeSpan.Zero)
        {
            _heartbeatTask = Task.Run(SendHeartbeatAsync);
        }
    }

    public void RegisterEventCallback(Action<string, byte[]> callback)
    {
        _eventCallback = callback;
    }

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return _transport.ConnectAsync(host, port, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _transport.DisconnectAsync(cancellationToken);
    }

    /// <summary>
    /// 优化的发送请求方法 - 使用 ValueTask 减少分配
    /// </summary>
    public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
        string serviceName, string methodName, TRequest request, CancellationToken cancellationToken = default)
    {
        // 优化: 使用预分配的MessageHeader，减少对象分配
        var header = _messageHeaderPool.Get();
        try
        {
            header.Type = MessageType.Request;
            header.MessageId = Guid.NewGuid();
            header.ServiceName = serviceName;
            header.MethodName = methodName;

            // 计算并设置 ProtocolId (使用与服务端相同的 FNV-1a 算法)
            // 传递请求参数类型以生成正确的签名
            header.ProtocolId = ComputeProtocolId(serviceName, methodName, typeof(TRequest));

            // 创建待处理请求
            var tcs = new TaskCompletionSource<NetworkMessage>();
            lock (_syncRoot)
            {
                _pendingRequests[header.MessageId] = tcs;
            }

            try
            {
                // 优化: 使用零拷贝序列化
                var success = await SendRequestOptimizedInternal(header, request, cancellationToken);
                if (!success)
                {
                    throw new IOException("发送请求失败");
                }

                // 等待响应
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.DefaultTimeout);

                NetworkMessage response;
                try
                {
                    response = await tcs.Task.WaitAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    throw new TimeoutException($"请求 {methodName} 超时");
                }

                // 优化: 直接反序列化，避免中间对象
                if (typeof(TResponse) == typeof(EmptyResponse))
                {
                    return (TResponse)(object)EmptyResponse.Instance;
                }
                else
                {
                    return DeserializeResponseOptimized<TResponse>(response.Body);
                }
            }
            finally
            {
                lock (_syncRoot)
                {
                    _pendingRequests.Remove(header.MessageId);
                }
            }
        }
        finally
        {
            // 归还MessageHeader到池中
            _messageHeaderPool.Return(header);
        }
    }

    /// <summary>
    /// 优化的内部发送方法
    /// </summary>
    private async ValueTask<bool> SendRequestOptimizedInternal<TRequest>(
        MessageHeader header, TRequest request, CancellationToken cancellationToken)
    {
        // 使用对象池获取缓冲区
        var bufferWriter = _bufferWriterPool.Get();
        try
        {
            bufferWriter.Clear();

            // 优化: 直接序列化到缓冲区，避免临时分配
            SerializeMessageOptimized(bufferWriter, header, request);

            // BUGFIX: 必须在 SendAsync 之前复制数据，因为 SendAsync 可能异步执行，
            // 而 bufferWriter 在 finally 块中会被归还到对象池并被清空/复用
            var data = bufferWriter.WrittenMemory.ToArray();

            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                return await _transport.SendAsync(data, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        finally
        {
            _bufferWriterPool.Return(bufferWriter);
        }
    }

    /// <summary>
    /// 优化的序列化方法 - 减少内存拷贝
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SerializeMessageOptimized<T>(IBufferWriter<byte> writer, MessageHeader header, T? payload)
    {
        var serializer = _serializerProvider.Create(MethodType.Unary, null);

        // 使用临时缓冲区进行序列化，避免多次分配
        var tempBuffer = ThreadLocalTempBuffer.Value!;

        // 序列化头部到临时缓冲区
        var headerSpan = SerializeToSpan(serializer, header, tempBuffer.AsSpan(0, 1024));

        // 序列化载荷到临时缓冲区
        ReadOnlySpan<byte> payloadSpan = default;
        if (payload != null)
        {
            payloadSpan = SerializeToSpan(serializer, payload, tempBuffer.AsSpan(1024));
            // Console.WriteLine removed for performance
        }

        // 计算总大小并一次性写入
        var totalSize = sizeof(int) + headerSpan.Length + payloadSpan.Length;
        var targetSpan = writer.GetSpan(totalSize); // 只取需要的部分

        // Console.WriteLine($"TotalSize={totalSize}, HeaderSize={headerSpan.Length}, PayloadSize={payloadSpan.Length}");

        // 直接打包到目标缓冲区
        PackMessageOptimized(targetSpan, headerSpan, payloadSpan);
        // Console.WriteLine(
        //     $"[消息封装] {Id} 消息包2: Size={totalSize} bytes, TargetSpanLength={targetSpan.Length}, Data=[{BitConverter.ToString(targetSpan[..Math.Min(totalSize, 128)].ToArray()).Replace("-", "")}]");
        writer.Advance(totalSize);

        // Console.WriteLine($"TotalSize={totalSize}, HeaderSize={headerSpan.Length}, PayloadSize={payloadSpan.Length}, TargetSpanLength={targetSpan.Length}");
    }

    /// <summary>
    /// 序列化到指定的Span，避免额外分配
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> SerializeToSpan<T>(ISerializer serializer, T data, Span<byte> targetBuffer)
    {
        // 使用临时的ArrayBufferWriter进行序列化
        var bufferWriter = new SpanBufferWriterAdapter(targetBuffer.Length);
        serializer.Serialize(bufferWriter, in data);

        // 将序列化的数据复制到目标Span
        var serializedData = bufferWriter.WrittenSpan;
        if (serializedData.Length > targetBuffer.Length)
        {
            throw new InvalidOperationException($"Serialized data ({serializedData.Length} bytes) exceeds target buffer size ({targetBuffer.Length} bytes)");
        }

        serializedData.CopyTo(targetBuffer);
        // Console.WriteLine removed for performance

        return targetBuffer[..serializedData.Length];
    }

    /// <summary>
    /// 优化的消息打包 - 使用unsafe代码提升性能
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PackMessageOptimized(Span<byte> destination, ReadOnlySpan<byte> headerSpan, ReadOnlySpan<byte> payloadSpan)
    {
        var headerSize = headerSpan.Length;
        var offset = 0;

        // 写入消息头长度 (Little Endian) - 优化版本
        BinaryPrimitives.WriteInt32LittleEndian(destination, headerSize);
        offset += sizeof(int);

        // 使用高性能拷贝
        headerSpan.CopyTo(destination[offset..]);
        offset += headerSize;

        if (payloadSpan.Length > 0)
        {
            payloadSpan.CopyTo(destination[offset..]);
        }
    }

    /// <summary>
    /// 优化的反序列化方法
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TResponse DeserializeResponseOptimized<TResponse>(byte[] responseBody)
    {
        var serializer = _serializerProvider.Create(MethodType.Unary, null);
        return serializer.Deserialize<TResponse>(new ReadOnlySequence<byte>(responseBody));
    }

    public async Task SendEventAsync<T>(string hubName, string methodName, T eventData, CancellationToken cancellationToken = default)
    {
        var header = _messageHeaderPool.Get();
        try
        {
            header.Type = MessageType.Event;
            header.MessageId = Guid.NewGuid();
            header.ServiceName = hubName;
            header.MethodName = methodName;

            var bufferWriter = _bufferWriterPool.Get();
            try
            {
                bufferWriter.Clear();
                SerializeMessageOptimized(bufferWriter, header, eventData);

                // BUGFIX: 复制数据以避免缓冲区被复用
                var data = bufferWriter.WrittenMemory.ToArray();
                await _transport.SendAsync(data, cancellationToken);
            }
            finally
            {
                _bufferWriterPool.Return(bufferWriter);
            }
        }
        finally
        {
            _messageHeaderPool.Return(header);
        }
    }

    public ISubscriptionToken SubscribeToEvent<T>(string eventName, EventHandler<T> handler)
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

            return new SubscriptionToken(subscription.Id, eventName, typeof(T), () => UnsubscribeEvent(eventName, subscription.Id));
        }
    }

    // ... 其余方法保持不变，但可以进行类似的优化

    private async Task ProcessMessageQueueAsync()
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var message = await _messageQueue.Reader.ReadAsync(_cts.Token);
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    timeoutCts.CancelAfter(_options.MessageProcessingTimeout);

                    try
                    {
                        ProcessMessage(message);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        if (_logger.IsEnabled(LogLevel.Warning))
                        {
                            _logger.LogWarning("消息处理超时: Type={MessageType}, MessageId={MessageId}",
                                message.Header.Type, message.Header.MessageId);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理消息队列时发生异常");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息处理任务异常退出");
        }
    }

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
                        continue;

                    var now = DateTime.UtcNow;
                    if (now - _lastHeartbeatTime < _options.HeartbeatInterval)
                        continue;

                    await SendHeartbeatOptimizedAsync(_cts.Token);
                    _lastHeartbeatTime = now;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送心跳时发生异常");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "心跳任务异常退出");
        }
    }

    private async ValueTask SendHeartbeatOptimizedAsync(CancellationToken cancellationToken)
    {
        var header = _messageHeaderPool.Get();
        try
        {
            header.Type = MessageType.Ping;
            header.MessageId = Guid.NewGuid();
            header.ServiceName = string.Empty;
            header.MethodName = string.Empty;

            var bufferWriter = _bufferWriterPool.Get();
            try
            {
                bufferWriter.Clear();
                SerializeMessageOptimized<object>(bufferWriter, header, null);

                // BUGFIX: 复制数据以避免缓冲区被复用
                var data = bufferWriter.WrittenMemory.ToArray();
                await _transport.SendAsync(data, cancellationToken);
            }
            finally
            {
                _bufferWriterPool.Return(bufferWriter);
            }
        }
        finally
        {
            _messageHeaderPool.Return(header);
        }
    }

    private void OnTransportDataReceived(object? sender, TransportDataEventArgs e)
    {
        try
        {
            var data = e.Data;
            if (data.Length < 4)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("收到的消息太短，无法包含头部长度");
                }
                return;
            }

            // 优化: 直接从Memory读取，避免ToArray分配
            var headerLength = BinaryPrimitives.ReadInt32LittleEndian(data.Span[..4]);

            if (headerLength <= 0 || headerLength > data.Length - 4)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("收到无效的消息头长度: {HeaderLength}, 数据总长度: {DataLength}",
                        headerLength, data.Length);
                }
                return;
            }

            // 优化: 直接使用Memory切片，避免ToArray
            var headerMemory = data.Slice(4, headerLength);
            var header = _serializerProvider.Create(MethodType.Unary, null)
                .Deserialize<Messaging.MessageHeader>(new ReadOnlySequence<byte>(headerMemory));

            var bodyStartIndex = 4 + headerLength;
            var bodyLength = data.Length - bodyStartIndex;
            var bodyBytes = bodyLength > 0 ? data.Slice(bodyStartIndex, bodyLength).ToArray() : Array.Empty<byte>();

            var message = new NetworkMessage(header, bodyBytes);

            if (!_messageQueue.Writer.TryWrite(message))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("消息队列已满，丢弃消息: Type={MessageType}, MessageId={MessageId}",
                        message.Header.Type, message.Header.MessageId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理接收数据失败");
        }
    }

    private void OnTransportStateChanged(object? sender, TransportStateEventArgs e)
    {
        try
        {
            // Update Statistics
            if (e.CurrentState == ConnectionState.Connected)
            {
                Statistics.ConnectedAt = DateTime.UtcNow;
            }
            Statistics.LastActiveAt = DateTime.UtcNow;

            // Fire the new event with the correct signature
            ConnectionStateChanged?.Invoke(this, e);

            // Fire legacy event for backward compatibility
            var legacyEventArgs = new ConnectionStateChangedEventArgs
            {
                ConnectionId = e.ConnectionId,
                PreviousState = e.PreviousState.ToExtended(),
                CurrentState = e.CurrentState.ToExtended(),
                Reason = e.Reason,
                Exception = e.Exception,
            };
            LegacyConnectionStateChanged?.Invoke(this, legacyEventArgs);

            if (e.CurrentState == ConnectionState.Disconnected)
            {
                Dictionary<Guid, TaskCompletionSource<NetworkMessage>> pendingRequests;
                lock (_syncRoot)
                {
                    pendingRequests = new Dictionary<Guid, TaskCompletionSource<NetworkMessage>>(_pendingRequests);
                    _pendingRequests.Clear();
                }

                foreach (var request in pendingRequests.Values)
                {
                    request.TrySetException(new IOException("连接断开"));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理状态变化时发生异常");
        }
    }

    private void ProcessMessage(NetworkMessage message)
    {
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
                    if (_logger.IsEnabled(LogLevel.Warning))
                    {
                        _logger.LogWarning("收到未支持的消息类型: {Type}, MessageId={MessageId}",
                            message.Header.Type, message.Header.MessageId);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息时发生异常: Type={MessageType}, MessageId={MessageId}",
                message.Header.Type, message.Header.MessageId);
        }
    }

    private void ProcessResponse(NetworkMessage message)
    {
        // 优先尝试零拷贝路径（ResponseContextManager）
        if (_responseManager.TryComplete(message.Header.MessageId, message.Body))
        {
            // 零拷贝路径成功完成
            return;
        }

        // 回退到旧路径（向后兼容）
        TaskCompletionSource<NetworkMessage>? tcs;
        lock (_syncRoot)
        {
            if (!_pendingRequests.Remove(message.Header.MessageId, out tcs))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("收到未匹配的响应消息: MessageId={MessageId}", message.Header.MessageId);
                }
                return;
            }
        }
        tcs.TrySetResult(message);
    }

    private void ProcessEvent(NetworkMessage message)
    {
        // 优先使用协议号路径（高性能）
        if (message.Header.ProtocolId != 0)
        {
            List<Action<ReadOnlyMemory<byte>>>? protocolIdHandlers;
            lock (_syncRoot)
            {
                if (_protocolIdEventHandlers.TryGetValue(message.Header.ProtocolId, out protocolIdHandlers))
                {
                    protocolIdHandlers = new List<Action<ReadOnlyMemory<byte>>>(protocolIdHandlers);
                }
            }

            if (protocolIdHandlers != null && protocolIdHandlers.Count > 0)
            {
                // 基于协议号的零拷贝路径：直接传递原始字节流
                foreach (var handler in protocolIdHandlers)
                {
                    try
                    {
                        handler(message.Body);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "协议号事件处理异常: ProtocolId=0x{ProtocolId:X4}, MessageId={MessageId}",
                            message.Header.ProtocolId, message.Header.MessageId);
                    }
                }
                return; // 协议号路径处理完成，直接返回
            }
        }

        // 回退到方法名路径（向后兼容）
        var eventName = message.Header.MethodName;

        // 构造完整事件名（包含服务名）
        var fullEventName = !string.IsNullOrEmpty(message.Header.ServiceName)
            ? $"{message.Header.ServiceName}.{eventName}"
            : eventName;

        // 尝试基于方法名的零拷贝路径
        List<Action<ReadOnlyMemory<byte>>>? zeroCopyHandlers;
        lock (_syncRoot)
        {
            if (_zeroCopyEventHandlers.TryGetValue(fullEventName, out zeroCopyHandlers))
            {
                zeroCopyHandlers = new List<Action<ReadOnlyMemory<byte>>>(zeroCopyHandlers);
            }
        }

        if (zeroCopyHandlers != null && zeroCopyHandlers.Count > 0)
        {
            // 零拷贝路径：直接传递原始字节流
            foreach (var handler in zeroCopyHandlers)
            {
                try
                {
                    handler(message.Body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "零拷贝事件处理异常: EventName={EventName}, MessageId={MessageId}",
                        fullEventName, message.Header.MessageId);
                }
            }
        }

        // 回退到旧路径（向后兼容）
        _eventCallback?.Invoke(eventName, message.Body);

        List<EventSubscription>? subscriptions;
        lock (_syncRoot)
        {
            if (!_eventSubscriptions.TryGetValue(eventName, out subscriptions))
                return;
            subscriptions = new List<EventSubscription>(subscriptions);
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Invoke(message.Body, _serializerProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事件处理异常: EventName={EventName}, MessageId={MessageId}",
                    eventName, message.Header.MessageId);
            }
        }
    }

    private async void ProcessPing(NetworkMessage message)
    {
        try
        {
            var header = _messageHeaderPool.Get();
            try
            {
                header.Type = MessageType.Pong;
                header.MessageId = message.Header.MessageId;
                header.ServiceName = string.Empty;
                header.MethodName = string.Empty;

                var bufferWriter = _bufferWriterPool.Get();
                try
                {
                    bufferWriter.Clear();
                    SerializeMessageOptimized<object>(bufferWriter, header, null);

                    // BUGFIX: 复制数据以避免缓冲区被复用
                    var data = bufferWriter.WrittenMemory.ToArray();
                    await _transport.SendAsync(data, CancellationToken.None);
                }
                finally
                {
                    _bufferWriterPool.Return(bufferWriter);
                }
            }
            finally
            {
                _messageHeaderPool.Return(header);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送Pong响应失败");
        }
    }

    private void UnsubscribeEvent(string eventName, Guid subscriptionId)
    {
        lock (_syncRoot)
        {
            if (!_eventSubscriptions.TryGetValue(eventName, out var subscriptions))
                return;

            var subscription = subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
            if (subscription == null)
                return;

            subscriptions.Remove(subscription);

            if (subscriptions.Count == 0)
            {
                _eventSubscriptions.Remove(eventName);
            }
        }
    }

    // ============================================================================
    // 零拷贝优化接口实现
    // ============================================================================

    /// <summary>
    /// [Request/Response] 发送请求并等待响应 - 零拷贝路径
    /// </summary>
    public async ValueTask<ReadOnlyMemory<byte>> InvokeRawAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> serializedRequest,
        CancellationToken cancellationToken = default)
    {
        var messageId = Guid.NewGuid();

        // Step 1: 创建响应上下文
        var tcs = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ResponseContext
        {
            MessageId = messageId,
            Tcs = tcs,
            EnqueueTimestamp = Stopwatch.GetTimestamp()
        };

        // 注册取消处理
        context.CancellationRegistration = cancellationToken.Register(() =>
        {
            _responseManager.TryCancel(messageId, new OperationCanceledException(cancellationToken));
        });

        _responseManager.Register(context);

        try
        {
            // Step 2: 构建消息包（零拷贝）
            var messageBuffer = _bufferWriterPool.Get();
            try
            {
                // 写入消息头
                var header = new MessageHeader(MessageType.Request, serviceName, methodName)
                {
                    MessageId = messageId,
                    Flags = MessageFlags.RequireResponse,
                    ProtocolId = ComputeProtocolId(serviceName, methodName)
                };

                var headerBytes = MemoryPack.MemoryPackSerializer.Serialize(header);
                BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.GetSpan(4), headerBytes.Length);
                messageBuffer.Advance(4);
                messageBuffer.Write(headerBytes);

                // 写入载荷
                messageBuffer.Write(serializedRequest.Span);

                // Step 3: 投入三层发送缓冲（零拷贝批量发送）
                // ThreeTierSendBuffer 会在内部管理数据的生命周期
                await _sendBuffer.EnqueueAsync(messageBuffer.WrittenMemory, cancellationToken);
                await _sendBuffer.FlushAsync(cancellationToken); // 立即刷新以减少延迟
            }
            finally
            {
                _bufferWriterPool.Return(messageBuffer);
            }

            // Step 4: 等待响应
            return await tcs.Task;
        }
        catch
        {
            _responseManager.TrySetException(messageId, new InvalidOperationException("Request failed"));
            throw;
        }
    }

    /// <summary>
    /// [Command/OneWay] 发送命令不等待响应 - 零拷贝路径
    /// </summary>
    public async ValueTask SendCommandAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> serializedCommand,
        CancellationToken cancellationToken = default)
    {
        // Command/OneWay 消息无需等待响应
        var messageBuffer = _bufferWriterPool.Get();
        try
        {
            var header = new MessageHeader(MessageType.OneWay, serviceName, methodName)
            {
                MessageId = Guid.NewGuid(),
                Flags = MessageFlags.None,
                ProtocolId = ComputeProtocolId(serviceName, methodName)
            };

            var headerBytes = MemoryPack.MemoryPackSerializer.Serialize(header);
            BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.GetSpan(4), headerBytes.Length);
            messageBuffer.Advance(4);
            messageBuffer.Write(headerBytes);
            messageBuffer.Write(serializedCommand.Span);

            // 投入三层发送缓冲（零拷贝批量发送）
            // ThreeTierSendBuffer 会在内部管理数据的生命周期
            await _sendBuffer.EnqueueAsync(messageBuffer.WrittenMemory, cancellationToken);
            // 注意：Command 不需要立即刷新，可以等待批量发送以提高吞吐量
        }
        finally
        {
            _bufferWriterPool.Return(messageBuffer);
        }
    }

    /// <summary>
    /// [Server Sent Event] 注册事件接收处理器 - 零拷贝路径
    /// </summary>
    public ISubscriptionToken RegisterEventHandler(
        string eventName,
        Action<ReadOnlyMemory<byte>> deserializeAndInvoke)
    {
        lock (_syncRoot)
        {
            if (!_zeroCopyEventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers = new List<Action<ReadOnlyMemory<byte>>>();
                _zeroCopyEventHandlers[eventName] = handlers;
            }

            handlers.Add(deserializeAndInvoke);

            var subscriptionId = Guid.NewGuid();
            return new SubscriptionToken(
                subscriptionId,
                eventName,
                typeof(ReadOnlyMemory<byte>),
                () => UnsubscribeZeroCopyEvent(eventName, deserializeAndInvoke));
        }
    }

    // ============================================================================
    // 基于协议号的方法 (高性能路径 - 推荐使用)
    // ============================================================================

    /// <summary>
    /// [Request/Response] 发送请求并等待响应 - 使用协议号（零拷贝路径）
    /// 源生成器专用：使用协议号替代方法名，性能更优
    /// </summary>
    /// <param name="protocolId">协议号（由源生成器生成）</param>
    /// <param name="serializedRequest">已通过 MemoryPack 序列化的请求载荷</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>原始响应字节流（待反序列化）</returns>
    public async ValueTask<ReadOnlyMemory<byte>> InvokeRawAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> serializedRequest,
        CancellationToken cancellationToken = default)
    {
        var messageId = Guid.NewGuid();

        // Step 1: 创建响应上下文
        var tcs = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ResponseContext
        {
            MessageId = messageId,
            Tcs = tcs,
            EnqueueTimestamp = Stopwatch.GetTimestamp()
        };

        // 注册取消处理
        context.CancellationRegistration = cancellationToken.Register(() =>
        {
            _responseManager.TryCancel(messageId, new OperationCanceledException(cancellationToken));
        });

        _responseManager.Register(context);

        try
        {
            // Step 2: 构建消息包（零拷贝）
            var messageBuffer = _bufferWriterPool.Get();
            try
            {
                // 写入消息头 - 使用协议号
                var header = new MessageHeader
                {
                    Type = MessageType.Request,
                    MessageId = messageId,
                    ProtocolId = protocolId,
                    ServiceName = string.Empty,  // 协议号模式下无需服务名
                    MethodName = string.Empty,   // 协议号模式下无需方法名
                    Flags = MessageFlags.RequireResponse,
                    Timestamp = DateTimeOffset.UtcNow.Ticks
                };

                var headerBytes = MemoryPack.MemoryPackSerializer.Serialize(header);
                BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.GetSpan(4), headerBytes.Length);
                messageBuffer.Advance(4);
                messageBuffer.Write(headerBytes);

                // 写入载荷
                messageBuffer.Write(serializedRequest.Span);

                // Step 3: 投入三层发送缓冲（零拷贝批量发送）
                await _sendBuffer.EnqueueAsync(messageBuffer.WrittenMemory, cancellationToken);
                await _sendBuffer.FlushAsync(cancellationToken); // 立即刷新以减少延迟
            }
            finally
            {
                _bufferWriterPool.Return(messageBuffer);
            }

            // Step 4: 等待响应
            return await tcs.Task;
        }
        catch
        {
            _responseManager.TrySetException(messageId, new InvalidOperationException("Request failed"));
            throw;
        }
    }

    /// <summary>
    /// [Command/OneWay] 发送命令不等待响应 - 使用协议号（零拷贝路径）
    /// 源生成器专用：使用协议号替代方法名，性能更优
    /// </summary>
    /// <param name="protocolId">协议号（由源生成器生成）</param>
    /// <param name="serializedCommand">已通过 MemoryPack 序列化的命令载荷</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async ValueTask SendCommandAsync(
        ushort protocolId,
        ReadOnlyMemory<byte> serializedCommand,
        CancellationToken cancellationToken = default)
    {
        // Command/OneWay 消息无需等待响应
        var messageBuffer = _bufferWriterPool.Get();
        try
        {
            var header = new MessageHeader
            {
                Type = MessageType.OneWay,
                MessageId = Guid.NewGuid(),
                ProtocolId = protocolId,
                ServiceName = string.Empty,  // 协议号模式下无需服务名
                MethodName = string.Empty,   // 协议号模式下无需方法名
                Flags = MessageFlags.None,
                Timestamp = DateTimeOffset.UtcNow.Ticks
            };

            var headerBytes = MemoryPack.MemoryPackSerializer.Serialize(header);
            BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.GetSpan(4), headerBytes.Length);
            messageBuffer.Advance(4);
            messageBuffer.Write(headerBytes);
            messageBuffer.Write(serializedCommand.Span);

            // 投入三层发送缓冲（零拷贝批量发送）
            await _sendBuffer.EnqueueAsync(messageBuffer.WrittenMemory, cancellationToken);
            // 注意：Command 不需要立即刷新，可以等待批量发送以提高吞吐量
        }
        finally
        {
            _bufferWriterPool.Return(messageBuffer);
        }
    }

    // 基于协议号的事件处理器字典（protocolId -> 反序列化委托列表）
    private readonly Dictionary<ushort, List<Action<ReadOnlyMemory<byte>>>> _protocolIdEventHandlers = new();

    /// <summary>
    /// [Server Sent Event] 注册事件接收处理器 - 使用协议号（零拷贝路径）
    /// 源生成器专用：使用协议号替代事件名，性能更优
    /// </summary>
    /// <param name="protocolId">协议号（由源生成器生成）</param>
    /// <param name="deserializeAndInvoke">反序列化+调用委托（由源生成器生成）</param>
    /// <returns>订阅令牌，用于取消订阅</returns>
    public ISubscriptionToken RegisterEventHandler(
        ushort protocolId,
        Action<ReadOnlyMemory<byte>> deserializeAndInvoke)
    {
        lock (_syncRoot)
        {
            if (!_protocolIdEventHandlers.TryGetValue(protocolId, out var handlers))
            {
                handlers = new List<Action<ReadOnlyMemory<byte>>>();
                _protocolIdEventHandlers[protocolId] = handlers;
            }

            handlers.Add(deserializeAndInvoke);

            var subscriptionId = Guid.NewGuid();
            return new SubscriptionToken(
                subscriptionId,
                $"Protocol:{protocolId:X4}",  // 使用协议号的十六进制表示作为标识
                typeof(ReadOnlyMemory<byte>),
                () => UnsubscribeProtocolIdEvent(protocolId, deserializeAndInvoke));
        }
    }

    /// <summary>
    /// 取消订阅基于协议号的事件
    /// </summary>
    private void UnsubscribeProtocolIdEvent(ushort protocolId, Action<ReadOnlyMemory<byte>> handler)
    {
        lock (_syncRoot)
        {
            if (_protocolIdEventHandlers.TryGetValue(protocolId, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _protocolIdEventHandlers.Remove(protocolId);
                }
            }
        }
    }

    /// <summary>
    /// 租借序列化缓冲区 - 支持零拷贝序列化
    /// </summary>
    public IBufferWriter<byte> RentSerializationBuffer(int estimatedSize = 256)
    {
        var writer = _bufferWriterPool.Get();
        writer.Clear();
        return writer;
    }

    /// <summary>
    /// 归还序列化缓冲区到对象池
    /// </summary>
    public void ReturnSerializationBuffer(IBufferWriter<byte> buffer)
    {
        if (buffer is ArrayBufferWriter<byte> abw)
        {
            _bufferWriterPool.Return(abw);
        }
    }

    /// <summary>
    /// 取消零拷贝事件订阅
    /// </summary>
    private void UnsubscribeZeroCopyEvent(string eventName, Action<ReadOnlyMemory<byte>> handler)
    {
        lock (_syncRoot)
        {
            if (_zeroCopyEventHandlers.TryGetValue(eventName, out var handlers))
            {
                handlers.Remove(handler);
                if (handlers.Count == 0)
                {
                    _zeroCopyEventHandlers.Remove(eventName);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _cts.Cancel();
            Task.WaitAll(_messageProcessingTasks, TimeSpan.FromSeconds(5));
            _messageQueue.Writer.Complete();
            _heartbeatTask?.Wait(TimeSpan.FromSeconds(5));
            _sendLock.Dispose();
            _cts.Dispose();

            // BUGFIX: 不应该释放静态的 ThreadLocal 变量，它们被所有实例共享
            // 移除: ThreadLocalBufferWriter.Dispose();
            // 移除: ThreadLocalTempBuffer.Dispose();

            // 释放零拷贝组件
            _responseManager?.Dispose();
            _sendBuffer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放资源时发生异常");
        }
    }

    // Unity兼容的重置方法
    private static void ResetMessageHeader(MessageHeader header)
    {
        header.Type = default;
        header.MessageId = default;
        header.ServiceName = string.Empty;
        header.MethodName = string.Empty;
    }

    private static void ResetBufferWriter(ArrayBufferWriter<byte> writer)
    {
        writer.Clear();
    }

    /// <summary>
    /// 计算协议号 - 委托给 ProtocolIdHelper
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ComputeProtocolId(string serviceName, string methodName, Type? requestType = null)
    {
        var paramTypes = requestType != null ? new[] { requestType } : null;
        return ProtocolIdHelper.ComputeProtocolId(serviceName, methodName, paramTypes);
    }

    // 内部类：事件订阅基类和泛型实现保持不变
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

    private class EventSubscription<T> : EventSubscription
    {
        private readonly EventHandler<T> _handler;

        public EventSubscription(string eventName, EventHandler<T> handler)
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

/// <summary>
/// Span基础的缓冲区写入器 - 避免额外分配
/// </summary>
public ref struct SpanBufferWriter
{
    private readonly Span<byte> _buffer;
    private int _written;

    public SpanBufferWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _written = 0;
    }

    public int WrittenCount => _written;

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        return _buffer[_written..];
    }

    public void Advance(int count)
    {
        _written += count;
    }
}

/// <summary>
/// 适配器类，使用ArrayBufferWriter来兼容IBufferWriter<byte>
/// 注意：此类创建自己的缓冲区，序列化完成后需要将数据复制到目标Span
/// </summary>
public class SpanBufferWriterAdapter : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> _writer;

    public SpanBufferWriterAdapter(int initialCapacity)
    {
        _writer = new ArrayBufferWriter<byte>(initialCapacity);
    }

    public int WrittenCount => _writer.WrittenCount;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        return _writer.GetMemory(sizeHint);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        return _writer.GetSpan(sizeHint);
    }

    public void Advance(int count)
    {
        _writer.Advance(count);
    }

    public ReadOnlySpan<byte> WrittenSpan => _writer.WrittenSpan;

    public void Clear()
    {
        _writer.Clear();
    }
}

/// <summary>
/// Unity兼容的对象池实现
/// </summary>
public class UnityCompatibleObjectPool<T> where T : class
{
    private readonly Func<T> _createFunc;
    private readonly Action<T> _resetAction;
    private readonly ConcurrentQueue<T> _objects;
    private readonly int _maxCapacity;
    private int _currentCount;

    public UnityCompatibleObjectPool(Func<T> createFunc, Action<T> resetAction, int maxCapacity = 32)
    {
        _createFunc = createFunc;
        _resetAction = resetAction;
        _maxCapacity = maxCapacity;
        _objects = new ConcurrentQueue<T>();
        _currentCount = 0;
    }

    public T Get()
    {
        if (_objects.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _currentCount);
            return item;
        }

        return _createFunc();
    }

    public void Return(T item)
    {
        try
        {
            _resetAction(item);

            if (_currentCount < _maxCapacity)
            {
                _objects.Enqueue(item);
                Interlocked.Increment(ref _currentCount);
            }
        }
        catch
        {
            // 重置失败时忽略该对象
        }
    }
}

