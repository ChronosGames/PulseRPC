using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Channels;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Shared;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 优化的传输通道 - 减少热路径内存分配
/// </summary>
internal class TransportChannel : TransportChannelBase, IClientChannel
{
    private readonly IClientTransport _transport;
    private readonly ISerializerProvider _serializerProvider;
    private readonly TransportChannelOptions _options;
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

    // 优化: 预分配的缓冲区和线程本地存储
    private static readonly ThreadLocal<byte[]> ThreadLocalTempBuffer =
        new(() => new byte[8192]);

    // 优化: 预分配的消息头池
    private readonly UnityCompatibleObjectPool<MessageHeader> _messageHeaderPool;
    private readonly UnityCompatibleObjectPool<ArrayBufferWriter<byte>> _bufferWriterPool;

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

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return _transport.ConnectAsync(host, port, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _transport.DisconnectAsync(cancellationToken);
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

            // 注意: 必须拷贝数据，因为底层传输缓冲区会被复用
            // TODO: 未来可考虑使用 ArrayPool 池化分配
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

            // 注意：断开连接时，ResponseContextManager 会通过超时机制处理待处理请求
            // 如需立即处理，可调用 _responseManager.Dispose() 并重新创建
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
                case MessageType.ReverseRequest:
                    ProcessReverseRequest(message);
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
        // 统一使用 ResponseContextManager 处理响应
        if (!_responseManager.TryComplete(message.Header.MessageId, message.Body))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("收到未匹配的响应: MessageId={MessageId}", message.Header.MessageId);
            }
        }
    }

    private void ProcessEvent(NetworkMessage message)
    {
        // 仅协议号路径
        if (message.Header.ProtocolId == 0)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("收到无协议号的事件: MessageId={MessageId}", message.Header.MessageId);
            }
            return;
        }

        // 无锁读取：ConcurrentDictionary + ImmutableArray
        if (!_protocolIdEventHandlers.TryGetValue(message.Header.ProtocolId, out var handlers))
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning("收到未注册的事件: ProtocolId=0x{Id:X4}", message.Header.ProtocolId);
            }
            return;
        }

        // ImmutableArray 是值类型，无需额外复制
        foreach (var handler in handlers)
        {
            try
            {
                handler(message.Body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "事件处理异常: ProtocolId=0x{Id:X4}", message.Header.ProtocolId);
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

        // 注册取消处理。
        // 已知限制：此处仅本地取消（结束等待并抛 OperationCanceledException），
        // 不会向服务端发送 MessageType.Cancel 帧，服务端不会中止已在执行的方法。
        // 端到端取消见 MessageType.Cancel 说明与优化计划 P2-13。
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
                    Timestamp = DateTimeOffset.UtcNow.Ticks,
                    // Deadline 传播：告知服务端本次请求愿意等待的相对时长（毫秒）。
                    // 服务端据此在本地单调时钟上强制 Deadline（见 MessageHeader.TimeoutMs 说明）。
                    TimeoutMs = _options.DefaultTimeout > TimeSpan.Zero
                        ? (int)Math.Clamp(_options.DefaultTimeout.TotalMilliseconds, 0, int.MaxValue)
                        : 0
                };

                var headerBytes = MemoryPack.MemoryPackSerializer.Serialize(header);
                BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.GetSpan(4), headerBytes.Length);
                messageBuffer.Advance(4);
                messageBuffer.Write(headerBytes);

                // 写入载荷
                messageBuffer.Write(serializedRequest.Span);

                // Step 3: 直接交给传输层发送（传输层内部有单写者发送队列并做单帧写出）。
                // SendAsync 在返回前已同步将数据拷入其自有池化缓冲，故此后可安全归还 messageBuffer。
                await _transport.SendAsync(messageBuffer.WrittenMemory, cancellationToken);
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

            // 直接交给传输层发送（传输层内部有单写者发送队列）。
            await _transport.SendAsync(messageBuffer.WrittenMemory, cancellationToken);
        }
        finally
        {
            _bufferWriterPool.Return(messageBuffer);
        }
    }

    // 基于协议号的事件处理器字典（protocolId -> 不可变委托数组）
    // 使用 ConcurrentDictionary + ImmutableArray 实现无锁读取
    private readonly ConcurrentDictionary<ushort, ImmutableArray<Action<ReadOnlyMemory<byte>>>> _protocolIdEventHandlers = new();

    /// <summary>
    /// [Server Sent Event] 注册事件接收处理器 - 使用协议号（零拷贝路径）
    /// 源生成器专用：使用协议号替代事件名，性能更优
    /// 使用 CAS 操作实现无锁注册
    /// </summary>
    /// <param name="protocolId">协议号（由源生成器生成）</param>
    /// <param name="deserializeAndInvoke">反序列化+调用委托（由源生成器生成）</param>
    /// <returns>订阅令牌，用于取消订阅</returns>
    public ISubscriptionToken RegisterEventHandler(
        ushort protocolId,
        Action<ReadOnlyMemory<byte>> deserializeAndInvoke)
    {
        // 使用 CAS 操作无锁更新 ImmutableArray
        _protocolIdEventHandlers.AddOrUpdate(
            protocolId,
            // 添加新条目
            _ => ImmutableArray.Create(deserializeAndInvoke),
            // 更新现有条目：创建新的 ImmutableArray 包含新处理器
            (_, existing) => existing.Add(deserializeAndInvoke));

        var subscriptionId = Guid.NewGuid();
        return new SubscriptionToken(
            subscriptionId,
            $"Protocol:{protocolId:X4}",  // 使用协议号的十六进制表示作为标识
            typeof(ReadOnlyMemory<byte>),
            () => UnsubscribeProtocolIdEvent(protocolId, deserializeAndInvoke));
    }

    // ============================================================================
    // [P-4] 服务端→客户端反向 Ask（Reverse Ask）
    // ============================================================================

    // 基于协议号的反向请求处理器字典（protocolId -> 处理器），1:1 语义
    private readonly ConcurrentDictionary<ushort, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>> _requestHandlers = new();

    /// <summary>
    /// [Server→Client Reverse Ask] 注册反向请求处理器 - 使用协议号（零拷贝路径）
    /// </summary>
    public ISubscriptionToken RegisterRequestHandler(
        ushort protocolId,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        if (!_requestHandlers.TryAdd(protocolId, handler))
        {
            throw new InvalidOperationException(
                $"协议号 0x{protocolId:X4} 已注册反向请求处理器；反向 Ask 为 1:1 语义，不支持重复注册。");
        }

        var subscriptionId = Guid.NewGuid();
        return new SubscriptionToken(
            subscriptionId,
            $"ReverseRequest:{protocolId:X4}",
            typeof(ReadOnlyMemory<byte>),
            () => _requestHandlers.TryRemove(protocolId, out _));
    }

    /// <summary>
    /// 处理服务端发起的反向请求：调用已注册处理器，并以 Response/Error 回传应答。
    /// </summary>
    private async void ProcessReverseRequest(NetworkMessage message)
    {
        var messageId = message.Header.MessageId;
        var protocolId = message.Header.ProtocolId;

        try
        {
            if (protocolId == 0 || !_requestHandlers.TryGetValue(protocolId, out var handler))
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("收到未注册的反向请求: ProtocolId=0x{Id:X4}, MessageId={MessageId}", protocolId, messageId);
                }

                var notFound = ErrorResponse.Create(
                    "REVERSE_HANDLER_NOT_FOUND",
                    $"客户端未注册协议号 0x{protocolId:X4} 的反向请求处理器。");
                await SendReverseReplyAsync(messageId, MessageType.Error, MemoryPack.MemoryPackSerializer.Serialize(notFound));
                return;
            }

            try
            {
                var result = await handler(message.Body, _cts.Token);
                await SendReverseReplyAsync(messageId, MessageType.Response, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "反向请求处理异常: ProtocolId=0x{Id:X4}, MessageId={MessageId}", protocolId, messageId);
                var error = ErrorResponse.Create(ex.GetType().Name, ex.Message);
                await SendReverseReplyAsync(messageId, MessageType.Error, MemoryPack.MemoryPackSerializer.Serialize(error));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理反向请求失败（无法回传应答）: ProtocolId=0x{Id:X4}, MessageId={MessageId}", protocolId, messageId);
        }
    }

    /// <summary>
    /// 构建并发送反向请求的应答帧（Response 成功 / Error 失败），回显 MessageId。
    /// </summary>
    private async ValueTask SendReverseReplyAsync(Guid messageId, MessageType type, ReadOnlyMemory<byte> body)
    {
        var messageBuffer = _bufferWriterPool.Get();
        try
        {
            messageBuffer.Clear();

            var header = new MessageHeader
            {
                Type = type,
                MessageId = messageId,
                ProtocolId = 0,
                ServiceName = string.Empty,
                MethodName = string.Empty,
                Flags = type == MessageType.Error ? MessageFlags.Error : MessageFlags.None,
                Timestamp = DateTimeOffset.UtcNow.Ticks
            };

            var headerBytes = MemoryPack.MemoryPackSerializer.Serialize(header);
            BinaryPrimitives.WriteInt32LittleEndian(messageBuffer.GetSpan(4), headerBytes.Length);
            messageBuffer.Advance(4);
            messageBuffer.Write(headerBytes);

            if (!body.IsEmpty)
            {
                messageBuffer.Write(body.Span);
            }

            // BUGFIX: 复制数据以避免缓冲区被复用
            var data = messageBuffer.WrittenMemory.ToArray();
            await _transport.SendAsync(data, CancellationToken.None);
        }
        finally
        {
            _bufferWriterPool.Return(messageBuffer);
        }
    }

    /// <summary>
    /// 取消订阅基于协议号的事件（无锁实现）
    /// </summary>
    private void UnsubscribeProtocolIdEvent(ushort protocolId, Action<ReadOnlyMemory<byte>> handler)
    {
        // 使用 CAS 操作无锁更新
        while (_protocolIdEventHandlers.TryGetValue(protocolId, out var existing))
        {
            var newHandlers = existing.Remove(handler);

            if (newHandlers.IsEmpty)
            {
                // 移除空条目（使用 TryUpdate 配合空数组检查，兼容 netstandard2.1）
                if (_protocolIdEventHandlers.TryRemove(protocolId, out _))
                {
                    break;
                }
            }
            else
            {
                // 更新为新数组
                if (_protocolIdEventHandlers.TryUpdate(protocolId, newHandlers, existing))
                {
                    break;
                }
            }
            // CAS 失败，重试
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            // 取消订阅事件处理程序，防止内存泄漏
            _transport.DataReceived -= OnTransportDataReceived;
            _transport.StateChanged -= OnTransportStateChanged;

            _cts.Cancel();
            Task.WaitAll(_messageProcessingTasks, TimeSpan.FromSeconds(5));
            _messageQueue.Writer.Complete();
            _heartbeatTask?.Wait(TimeSpan.FromSeconds(5));
            _sendLock.Dispose();
            _cts.Dispose();

            // 释放零拷贝组件
            _responseManager?.Dispose();
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

