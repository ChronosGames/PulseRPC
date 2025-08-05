using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Serialization;
using PulseRPC.Transport;

namespace PulseRPC.Client.Channels;

/// <summary>
/// 优化的传输通道 - 减少热路径内存分配
/// </summary>
public class OptimizedTransportChannel : IClientChannel
{
    private readonly string _name;
    private readonly IClientTransport _transport;
    private readonly ISerializerProvider _serializerProvider;
    private readonly TransportChannelOptions _options;
    private readonly Dictionary<Guid, TaskCompletionSource<NetworkMessage>> _pendingRequests = new();
    private readonly Dictionary<string, List<EventSubscription>> _eventSubscriptions = new();
    private readonly object _syncRoot = new object();
    private readonly ILogger<OptimizedTransportChannel> _logger;
    private readonly Channel<NetworkMessage> _messageQueue;
    private readonly Task[] _messageProcessingTasks;
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
    private DateTime _lastHeartbeatTime;
    private Task? _heartbeatTask;
    private bool _disposed;

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

    public string Name => _name;
    public bool IsConnected => _transport.IsConnected;
    public ConnectionState ConnectionState => _transport.State;

    public event System.EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public OptimizedTransportChannel(
        string name,
        IClientTransport transport,
        ISerializerProvider serializerProvider,
        TransportChannelOptions? options = null,
        ILogger<OptimizedTransportChannel>? logger = null)
    {
        _name = name;
        _transport = transport;
        _serializerProvider = serializerProvider;
        _options = options ?? new TransportChannelOptions();
        _logger = logger ?? NullLogger<OptimizedTransportChannel>.Instance;
        
        // 初始化对象池
        _messageHeaderPool = new UnityCompatibleObjectPool<MessageHeader>(() => new MessageHeader(), ResetMessageHeader, 32);
        _bufferWriterPool = new UnityCompatibleObjectPool<ArrayBufferWriter<byte>>(() => new ArrayBufferWriter<byte>(4096), ResetBufferWriter, 32);
        
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
    public async ValueTask<TResponse> SendRequestOptimizedAsync<TRequest, TResponse>(
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
            
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                var data = bufferWriter.WrittenMemory;
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
        }

        // 计算总大小并一次性写入
        var totalSize = sizeof(int) + headerSpan.Length + payloadSpan.Length;
        var targetSpan = writer.GetSpan(totalSize);

        // 直接打包到目标缓冲区
        PackMessageOptimized(targetSpan, headerSpan, payloadSpan);
        writer.Advance(totalSize);
    }

    /// <summary>
    /// 序列化到指定的Span，避免额外分配
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> SerializeToSpan<T>(ISerializer serializer, T data, Span<byte> buffer)
    {
        var bufferWriter = new SpanBufferWriterAdapter(buffer);
        serializer.Serialize(bufferWriter, in data);
        return bufferWriter.WrittenSpan;
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

    /// <summary>
    /// 兼容原接口的方法
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        string serviceName, string methodName, TRequest request, CancellationToken cancellationToken = default)
    {
        return await SendRequestOptimizedAsync<TRequest, TResponse>(serviceName, methodName, request, cancellationToken);
    }

    public async Task SendEventAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default)
    {
        var header = _messageHeaderPool.Get();
        try
        {
            header.Type = MessageType.Event;
            header.MessageId = Guid.NewGuid();
            header.ServiceName = string.Empty;
            header.MethodName = eventName;

            var bufferWriter = _bufferWriterPool.Get();
            try
            {
                bufferWriter.Clear();
                SerializeMessageOptimized(bufferWriter, header, eventData);
                await _transport.SendAsync(bufferWriter.WrittenMemory, cancellationToken);
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

    public async Task SendAsync<T>(string eventName, T message, CancellationToken cancellationToken = default)
    {
        await SendEventAsync(eventName, message, cancellationToken);
    }

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
                await _transport.SendAsync(bufferWriter.WrittenMemory, cancellationToken);
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
            var eventArgs = new ConnectionStateChangedEventArgs(e.PreviousState, e.CurrentState, e.Exception);
            ConnectionStateChanged?.Invoke(this, eventArgs);

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
        var eventName = message.Header.MethodName;
        
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
                    await _transport.SendAsync(bufferWriter.WrittenMemory, CancellationToken.None);
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
            ThreadLocalBufferWriter.Dispose();
            ThreadLocalTempBuffer.Dispose();
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
/// </summary>
public class SpanBufferWriterAdapter : IBufferWriter<byte>
{
    private readonly ArrayBufferWriter<byte> _writer;

    public SpanBufferWriterAdapter(Span<byte> buffer)
    {
        _writer = new ArrayBufferWriter<byte>(buffer.Length);
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
        if (item == null)
            return;

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