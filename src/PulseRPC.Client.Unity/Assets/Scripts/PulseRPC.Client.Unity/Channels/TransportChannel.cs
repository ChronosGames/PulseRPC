using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Serialization;
using PulseRPC.Transport;
using UnityEngine;

namespace PulseRPC.Client.Channels
{
    /// <summary>
    /// 基于传输层的消息通道实现
    /// </summary>
    public class TransportChannel : IMessageChannel, IHasTransport, IDisposable
    {
        private readonly string _name;
        private readonly IClientTransport _transport;
        private readonly ISerializer _serializer;
        private readonly ILogger _logger;
        private readonly Dictionary<string, List<Delegate>> _eventHandlers = new Dictionary<string, List<Delegate>>();
        private readonly Dictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new Dictionary<string, TaskCompletionSource<byte[]>>();
        private readonly object _syncLock = new object();
        private readonly List<ISubscriptionToken> _subscriptions = new List<ISubscriptionToken>();
        private int _requestId = 0;
        private bool _isDisposed;

        /// <summary>
        /// 通道名称
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// 通道是否已连接
        /// </summary>
        public bool IsConnected => _transport?.IsConnected ?? false;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<bool> ConnectionStateChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="name">通道名称</param>
        /// <param name="transport">传输实例</param>
        /// <param name="serializer">序列化器</param>
        /// <param name="logger">日志记录器</param>
        public TransportChannel(
            string name,
            IClientTransport transport,
            ISerializer serializer,
            ILogger logger = null)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _logger = logger;

            // 注册传输层事件
            _transport.Connected += OnTransportConnected;
            _transport.Disconnected += OnTransportDisconnected;
            _transport.MessageReceived += OnTransportMessageReceived;
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            try
            {
                LogInfo($"正在连接到 {host}:{port}...");
                await _transport.ConnectAsync(host, port, cancellationToken);
            }
            catch (Exception ex)
            {
                LogError($"连接失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            ThrowIfDisposed();

            try
            {
                _transport.Disconnect();
            }
            catch (Exception ex)
            {
                LogError($"断开连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送请求并等待响应
        /// </summary>
        public async Task<byte[]> SendRequestAsync(
            string serviceName,
            string methodName,
            byte[] requestData,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!IsConnected)
                throw new InvalidOperationException("通道未连接");

            var requestId = Interlocked.Increment(ref _requestId).ToString();
            var tcs = new TaskCompletionSource<byte[]>();

            // 创建包含元数据的请求消息
            var requestMessage = new Dictionary<string, object>
            {
                ["id"] = requestId,
                ["type"] = "request",
                ["service"] = serviceName,
                ["method"] = methodName,
                ["data"] = requestData
            };

            // 序列化请求消息
            var messageData = _serializer.Serialize(requestMessage);

            lock (_syncLock)
            {
                _pendingRequests[requestId] = tcs;
            }

            try
            {
                // 设置取消令牌
                cancellationToken.Register(() =>
                {
                    lock (_syncLock)
                    {
                        if (_pendingRequests.TryGetValue(requestId, out var pendingTcs) && pendingTcs == tcs)
                        {
                            _pendingRequests.Remove(requestId);
                            tcs.TrySetCanceled();
                        }
                    }
                });

                // 发送请求
                await _transport.SendAsync(messageData, cancellationToken);

                // 等待响应
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                lock (_syncLock)
                {
                    _pendingRequests.Remove(requestId);
                }

                LogError($"发送请求失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 发送通知消息
        /// </summary>
        public async Task SendNotificationAsync(
            string eventName,
            byte[] eventData,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (!IsConnected)
                throw new InvalidOperationException("通道未连接");

            // 创建包含元数据的通知消息
            var notificationMessage = new Dictionary<string, object>
            {
                ["type"] = "notification",
                ["event"] = eventName,
                ["data"] = eventData
            };

            // 序列化通知消息
            var messageData = _serializer.Serialize(notificationMessage);

            try
            {
                // 发送通知
                await _transport.SendAsync(messageData, cancellationToken);
            }
            catch (Exception ex)
            {
                LogError($"发送通知失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 订阅事件
        /// </summary>
        public ISubscriptionToken SubscribeToEvent<TEvent>(
            string eventName,
            Action<object, TEvent> handler)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("事件名称不能为空", nameof(eventName));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (_syncLock)
            {
                if (!_eventHandlers.TryGetValue(eventName, out var handlers))
                {
                    handlers = new List<Delegate>();
                    _eventHandlers[eventName] = handlers;
                }

                handlers.Add(handler);
            }

            // 创建订阅令牌
            var token = new SubscriptionToken(this, eventName, handler);
            _subscriptions.Add(token);
            return token;
        }

        /// <summary>
        /// 取消订阅事件
        /// </summary>
        internal void Unsubscribe<TEvent>(string eventName, Action<object, TEvent> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null)
                return;

            lock (_syncLock)
            {
                if (!_eventHandlers.TryGetValue(eventName, out var handlers))
                    return;

                handlers.Remove(handler);

                if (handlers.Count == 0)
                {
                    _eventHandlers.Remove(eventName);
                }
            }
        }

        /// <summary>
        /// 处理连接事件
        /// </summary>
        private void OnTransportConnected()
        {
            LogInfo("已连接到服务器");
            ConnectionStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 处理断开连接事件
        /// </summary>
        private void OnTransportDisconnected()
        {
            LogInfo("已断开连接");
            ConnectionStateChanged?.Invoke(false);

            // 取消所有挂起的请求
            lock (_syncLock)
            {
                foreach (var tcs in _pendingRequests.Values)
                {
                    tcs.TrySetCanceled();
                }
                _pendingRequests.Clear();
            }
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        private void OnTransportMessageReceived(byte[] data)
        {
            try
            {
                // 反序列化消息
                var message = _serializer.Deserialize<Dictionary<string, object>>(data);

                // 获取消息类型
                if (!message.TryGetValue("type", out var typeObj) || !(typeObj is string type))
                {
                    LogWarning("收到无效消息: 缺少类型信息");
                    return;
                }

                // 处理响应消息
                if (type == "response" && message.TryGetValue("id", out var idObj) && idObj is string id)
                {
                    HandleResponseMessage(id, message);
                    return;
                }

                // 处理事件消息
                if (type == "event" && message.TryGetValue("event", out var eventObj) && eventObj is string eventName)
                {
                    HandleEventMessage(eventName, message);
                    return;
                }

                LogWarning($"收到未知类型的消息: {type}");
            }
            catch (Exception ex)
            {
                LogError($"处理消息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理响应消息
        /// </summary>
        private void HandleResponseMessage(string requestId, Dictionary<string, object> message)
        {
            TaskCompletionSource<byte[]> tcs;

            // 获取挂起的请求
            lock (_syncLock)
            {
                if (!_pendingRequests.TryGetValue(requestId, out tcs))
                {
                    LogWarning($"收到未知请求ID的响应: {requestId}");
                    return;
                }

                _pendingRequests.Remove(requestId);
            }

            // 检查是否有错误
            if (message.TryGetValue("error", out var errorObj) && errorObj != null)
            {
                var errorMessage = errorObj.ToString();
                LogError($"请求 {requestId} 失败: {errorMessage}");
                tcs.TrySetException(new Exception(errorMessage));
                return;
            }

            // 获取响应数据
            if (!message.TryGetValue("data", out var dataObj) || !(dataObj is byte[] responseData))
            {
                LogWarning($"响应 {requestId} 缺少数据");
                tcs.TrySetException(new Exception("响应缺少数据"));
                return;
            }

            // 设置响应结果
            tcs.TrySetResult(responseData);
        }

        /// <summary>
        /// 处理事件消息
        /// </summary>
        private void HandleEventMessage(string eventName, Dictionary<string, object> message)
        {
            // 获取事件数据
            if (!message.TryGetValue("data", out var dataObj))
            {
                LogWarning($"事件 {eventName} 缺少数据");
                return;
            }

            // 获取事件处理器
            List<Delegate> handlers;
            lock (_syncLock)
            {
                if (!_eventHandlers.TryGetValue(eventName, out handlers) || handlers.Count == 0)
                {
                    // 没有订阅此事件
                    return;
                }

                // 复制处理器列表，避免并发修改
                handlers = new List<Delegate>(handlers);
            }

            // 调用事件处理器
            foreach (var handler in handlers)
            {
                try
                {
                    // 获取处理器的事件类型
                    var eventType = handler.Method.GetParameters()[1].ParameterType;

                    // 将数据转换为事件对象
                    var eventData = ConvertToEventObject(dataObj, eventType);

                    // 调用处理器
                    handler.DynamicInvoke(this, eventData);
                }
                catch (Exception ex)
                {
                    LogError($"调用事件处理器失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 将数据转换为事件对象
        /// </summary>
        private object ConvertToEventObject(object data, Type eventType)
        {
            if (data is byte[] binaryData)
            {
                // 反序列化二进制数据
                return _serializer.Deserialize(eventType, binaryData);
            }

            // 尝试直接转换
            return Convert.ChangeType(data, eventType);
        }

        /// <summary>
        /// 记录信息日志
        /// </summary>
        private void LogInfo(string message)
        {
            _logger?.Log(LogType.Log, $"[{_name}] {message}");
            Debug.Log($"[{_name}] {message}");
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        private void LogWarning(string message)
        {
            Debug.LogWarning($"[{_name}] {message}");
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        private void LogError(string message)
        {
            _logger?.LogError($"[{_name}] {message}");
            Debug.LogError($"[{_name}] {message}");
        }

        /// <summary>
        /// 检查是否已释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            // 清理所有订阅
            foreach (var token in _subscriptions)
            {
                if (token is SubscriptionToken subscriptionToken)
                {
                    subscriptionToken.DetachFromChannel();
                }
            }
            _subscriptions.Clear();

            // 取消所有挂起的请求
            lock (_syncLock)
            {
                foreach (var tcs in _pendingRequests.Values)
                {
                    tcs.TrySetCanceled();
                }
                _pendingRequests.Clear();
            }

            // 解除事件注册
            _transport.Connected -= OnTransportConnected;
            _transport.Disconnected -= OnTransportDisconnected;
            _transport.MessageReceived -= OnTransportMessageReceived;

            // 断开连接
            if (_transport.IsConnected)
            {
                _transport.Disconnect();
            }

            // 释放传输层资源
            if (_transport is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _isDisposed = true;
        }

        /// <summary>
        /// 订阅令牌实现
        /// </summary>
        private class SubscriptionToken : ISubscriptionToken
        {
            private readonly TransportChannel _channel;
            private readonly string _eventName;
            private readonly Delegate _handler;
            private bool _isDisposed;

            public Guid Id { get; } = Guid.NewGuid();
            public bool IsActive => !_isDisposed;

            public SubscriptionToken(TransportChannel channel, string eventName, Delegate handler)
            {
                _channel = channel;
                _eventName = eventName;
                _handler = handler;
            }

            public void Unsubscribe()
            {
                if (_isDisposed)
                    return;

                if (_channel != null && _handler != null)
                {
                    // 通过反射调用泛型方法
                    var eventType = _handler.Method.GetParameters()[1].ParameterType;
                    var unsubscribeMethod = typeof(TransportChannel).GetMethod("Unsubscribe");
                    var genericMethod = unsubscribeMethod.MakeGenericMethod(eventType);
                    genericMethod.Invoke(_channel, new object[] { _eventName, _handler });
                }

                _isDisposed = true;
            }

            public void DetachFromChannel()
            {
                _isDisposed = true;
            }

            public void Dispose()
            {
                Unsubscribe();
            }
        }
    }
}
