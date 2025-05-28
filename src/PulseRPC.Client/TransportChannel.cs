using System.Buffers;
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

    public ConnectionStateChangedEventArgs(ConnectionState oldState, ConnectionState newState, Exception? exception = null)
    {
        OldState = oldState;
        NewState = newState;
        Exception = exception;
    }
}

/// <summary>
/// 基于传输层的消息通道
/// </summary>
public partial class TransportChannel : IMessageChannel, IHasTransport, IHasEventReceiver
{
    private readonly string _name;
    private readonly IClientTransport _transport;
    private readonly ISerializerProvider _serializerProvider;
    private readonly Dictionary<Guid, TaskCompletionSource<NetworkMessage>> _pendingRequests = new();
    private readonly Dictionary<string, List<EventSubscription>> _eventSubscriptions = new();
    private readonly object _syncRoot = new object();
    private readonly ILogger<TransportChannel> _logger;

    public string Name => _name;
    public bool IsConnected => _transport.IsConnected;
    public ConnectionState ConnectionState => _transport.State;

    // 事件回调
    private Action<string, byte[]>? _eventCallback;

    public event System.EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public TransportChannel(string name, IClientTransport transport, ISerializerProvider serializerProvider, ILogger<TransportChannel>? logger = null)
    {
        _name = name;
        _transport = transport;
        _serializerProvider = serializerProvider;
        _logger = logger ?? NullLogger<TransportChannel>.Instance;

        // 注册传输事件
        _transport.DataReceived += OnTransportDataReceived;
        _transport.StateChanged += OnTransportStateChanged;
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
    /// 发送请求并等待响应
    /// </summary>
    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
        string serviceName, string methodName, TRequest request, CancellationToken cancellationToken = default)
    {
        // 创建消息头
        var messageId = Guid.NewGuid();
        var header = new Messaging.MessageHeader
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
            // 发送请求
            await _transport.SendAsync(header, request, cancellationToken);

            // 等待响应，设置超时
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 可配置超时时间

            NetworkMessage response;
            try
            {
                response = await tcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException($"请求 {methodName} 超时");
            }

            // 反序列化响应
            return _serializerProvider.Create(MethodType.ClientStreaming, null).Deserialize<TResponse>(new ReadOnlySequence<byte>(response.Body));
        }
        finally
        {
            // 清理请求
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
        var header = new Messaging.MessageHeader
        {
            Type = MessageType.Event,
            MessageId = Guid.NewGuid(),
            ServiceName = string.Empty,
            MethodName = eventName
        };

        // 发送事件
        await _transport.SendAsync(header, eventData, cancellationToken);
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

            return new SubscriptionToken(subscription.Id, eventName, typeof(T), () => UnsubscribeEvent(eventName, subscription.Id));
        }
    }

    /// <summary>
    /// 取消订阅事件
    /// </summary>
    private void UnsubscribeEvent(string eventName, Guid subscriptionId)
    {
        lock (_syncRoot)
        {
            if (_eventSubscriptions.TryGetValue(eventName, out var subscriptions))
            {
                var subscription = subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
                if (subscription != null)
                {
                    subscriptions.Remove(subscription);

                    // 如果没有更多订阅，移除事件
                    if (subscriptions.Count == 0)
                    {
                        _eventSubscriptions.Remove(eventName);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 处理传输层数据接收
    /// </summary>
    private void OnTransportDataReceived(object? sender, TransportDataEventArgs e)
    {
        try
        {
            using var ms = new MemoryStream(e.Data.ToArray());
            using var reader = new BinaryReader(ms);

            // 读取头部长度
            int headerLength = reader.ReadInt32();

            // 检查头部长度合法性
            if (headerLength <= 0 || headerLength > e.Data.Length - 4)
            {
                _logger.LogWarning("收到无效的消息头");
                return;
            }

            // 读取头部
            byte[] headerBytes = reader.ReadBytes(headerLength);
            var header = _serializerProvider.Create(MethodType.ClientStreaming, null).Deserialize<Messaging.MessageHeader>(new ReadOnlySequence<byte>(headerBytes));

            // 读取消息体
            byte[] bodyBytes = reader.ReadBytes((int)(ms.Length - ms.Position));

            // 创建网络消息
            var message = new NetworkMessage(header, bodyBytes);

            // 处理消息
            ProcessMessage(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理接收数据失败");
        }
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    private void ProcessMessage(NetworkMessage message)
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
                _logger.LogWarning("收到未支持的消息类型: {Type}", message.Header.Type);
                break;
        }
    }

    /// <summary>
    /// 处理响应消息
    /// </summary>
    private void ProcessResponse(NetworkMessage message)
    {
        TaskCompletionSource<NetworkMessage>? tcs = null;

        lock (_syncRoot)
        {
            if (_pendingRequests.TryGetValue(message.Header.MessageId, out tcs))
            {
                _pendingRequests.Remove(message.Header.MessageId);
            }
        }

        tcs?.TrySetResult(message);
    }

    /// <summary>
    /// 处理事件消息
    /// </summary>
    private void ProcessEvent(NetworkMessage message)
    {
        var eventName = message.Header.MethodName;

        // 调用事件回调
        _eventCallback?.Invoke(eventName, message.Body);

        List<EventSubscription>? subscriptions;
        lock (_syncRoot)
        {
            if (!_eventSubscriptions.TryGetValue(eventName, out subscriptions))
            {
                return; // 没有订阅者
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
                _logger.LogError(ex, "事件处理异常: {EventName}", eventName);
            }
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
            var header = new Messaging.MessageHeader
            {
                Type = MessageType.Pong,
                MessageId = message.Header.MessageId
            };

            // 发送Pong
            await _transport.SendAsync<object>(header, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送Pong响应失败");
        }
    }

    /// <summary>
    /// 处理传输层状态变化
    /// </summary>
    private void OnTransportStateChanged(object? sender, TransportStateEventArgs e)
    {
        // 转换为通道状态事件
        var eventArgs = new ConnectionStateChangedEventArgs(
            ConvertState(e.PreviousState),
            ConvertState(e.CurrentState),
            e.Exception);

        // 触发状态变化事件
        ConnectionStateChanged?.Invoke(this, eventArgs);

        // 如果断开连接，取消所有待处理请求
        if (e.CurrentState != ConnectionState.Disconnected)
        {
            return;
        }

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

    /// <summary>
    /// 转换状态类型
    /// </summary>
    private static PulseRPC.Transport.ConnectionState ConvertState(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Connected => PulseRPC.Transport.ConnectionState.Connected,
            ConnectionState.Connecting => PulseRPC.Transport.ConnectionState.Connecting,
            ConnectionState.Disconnected => PulseRPC.Transport.ConnectionState.Disconnected,
            ConnectionState.Disconnecting => PulseRPC.Transport.ConnectionState.Disconnecting,
            ConnectionState.Reconnecting => PulseRPC.Transport.ConnectionState.Reconnecting,
            ConnectionState.Failed => PulseRPC.Transport.ConnectionState.Failed,
            _ => PulseRPC.Transport.ConnectionState.Disconnected
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // 取消事件订阅
        _transport.DataReceived -= OnTransportDataReceived;
        _transport.StateChanged -= OnTransportStateChanged;

        // 释放传输层
        _transport.Dispose();
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
            var eventData = serializerProvider.Create(MethodType.ClientStreaming, null).Deserialize<T>(new ReadOnlySequence<byte>(data));
            _handler(this, eventData);
        }
    }
}
