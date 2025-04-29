using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using PulseRPC.Protocol;
#if UNITY_WEBGL && !UNITY_EDITOR
using PulseRPC.WebGLImplementation;
#else
using System.Net.WebSockets;
#endif

namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// WebSocket实现的PulseRPC连接，适合所有Unity平台，不依赖gRPC。
    /// </summary>
    public class PulseWebSocketConnection : IPulseConnection
    {
        private readonly string _url;
        private readonly int _reconnectAttempts;
        private readonly TimeSpan _reconnectDelay;
        private readonly TimeSpan _requestTimeout;

        private bool _isConnectedFlag;
        private readonly object _connectionLock = new object();
        private CancellationTokenSource _cts;
        private Task _receiveTask;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PulseResponse>> _pendingRequests
            = new ConcurrentDictionary<Guid, TaskCompletionSource<PulseResponse>>();

#if UNITY_WEBGL && !UNITY_EDITOR
        private WebGLWebSocket _webSocket;
#else
        private ClientWebSocket _webSocket;
#endif

        /// <summary>
        /// 创建一个新的PulseWebSocketConnection实例。
        /// </summary>
        /// <param name="url">WebSocket服务器URL，例如"ws://localhost:5000/pulse"</param>
        /// <param name="reconnectAttempts">连接断开时尝试重连的次数，0表示不重连</param>
        /// <param name="reconnectDelay">重连延迟时间</param>
        /// <param name="requestTimeout">请求超时时间</param>
        public PulseWebSocketConnection(
            string url,
            int reconnectAttempts = 3,
            TimeSpan? reconnectDelay = null,
            TimeSpan? requestTimeout = null)
        {
            _url = url;
            _reconnectAttempts = reconnectAttempts;
            _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(3);
            _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        }

        /// <inheritdoc />
        public bool IsConnected => _isConnectedFlag && IsSocketConnected();

        /// <inheritdoc />
        public event Func<PulseEvent, Task> OnEventReceived;

        /// <inheritdoc />
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_connectionLock)
            {
                if (IsConnected)
                {
                    return;
                }
                _isConnectedFlag = true; // 设置标志以防止竞争条件
            }

            _cts = new CancellationTokenSource();

            try
            {
                // 使用链接令牌源以支持取消和超时
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectCts.CancelAfter(TimeSpan.FromSeconds(15));

#if UNITY_WEBGL && !UNITY_EDITOR
                _webSocket = new WebGLWebSocket();
                await _webSocket.ConnectAsync(_url, connectCts.Token);
#else
                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(new Uri(_url), connectCts.Token);
#endif

                // 确保WebSocket成功连接
                if (!IsSocketConnected())
                {
                    _isConnectedFlag = false;
                    throw new Exception("WebSocket连接失败");
                }

                // 启动消息接收循环
                _receiveTask = ReceiveMessagesAsync(_cts.Token);

                // 启动心跳循环
                _ = StartHeartbeatLoopAsync(_cts.Token, TimeSpan.FromSeconds(15));
            }
            catch
            {
                _isConnectedFlag = false; // 连接失败时重置标志
                await CleanupConnectionAsync();
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DisconnectAsync()
        {
            await CleanupConnectionAsync();
        }

        /// <inheritdoc />
        public async Task<PulseResponse> SendRequestAsync(PulseRequest request, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("客户端未连接");

            var tcs = new TaskCompletionSource<PulseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(request.RequestId, tcs))
            {
                throw new InvalidOperationException("检测到重复的请求ID");
            }

            // 将提供的令牌与连接的令牌链接
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cts?.Token ?? CancellationToken.None);

            linkedCts.CancelAfter(_requestTimeout);

            try
            {
                var requestBytes = MemoryPackSerializer.Serialize(request);
                var envelope = new MessageEnvelope { Type = MessageType.Request, Payload = requestBytes };
                await SendEnvelopeAsync(envelope, linkedCts.Token);

                // 等待响应或超时/取消
                return await tcs.Task.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("请求被调用者取消", cancellationToken);
                else if (_cts?.IsCancellationRequested ?? true)
                    throw new InvalidOperationException("等待响应时连接关闭");
                else
                    throw new TimeoutException("请求超时");
            }
            catch (Exception ex)
            {
                // 确保TCS在发送失败时异常完成
                tcs.TrySetException(ex);
                throw; // 重新抛出原始异常
            }
            finally
            {
                _pendingRequests.TryRemove(request.RequestId, out _);
            }
        }

        /// <summary>
        /// 清理连接资源
        /// </summary>
        private async Task CleanupConnectionAsync()
        {
            if (!_isConnectedFlag) return; // 已经清理过

            lock (_connectionLock)
            {
                if (!_isConnectedFlag) return;
                _isConnectedFlag = false;
            }

            _cts?.Cancel(); // 向循环发送取消信号

            // 等待接收循环完成
            if (_receiveTask != null && !_receiveTask.IsCompleted)
            {
                try
                {
                    // 给它一个简短的时间优雅地完成
                    await Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
                }
                catch (OperationCanceledException) { /* 预期的 */ }
                catch (Exception ex)
                {
                    Debug.LogError($"关闭接收任务时出错: {ex.Message}");
                }
            }

            // 将挂起的请求标记为失败
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new TaskCanceledException("连接关闭"));
            }
            _pendingRequests.Clear();

            // 关闭WebSocket
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (_webSocket != null)
                {
                    await _webSocket.CloseAsync();
                    _webSocket = null;
                }
#else
                if (_webSocket != null)
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭连接", CancellationToken.None);
                    }
                    _webSocket.Dispose();
                    _webSocket = null;
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"关闭WebSocket时出错: {ex.Message}");
            }

            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>
        /// 持续从WebSocket读取消息的循环
        /// </summary>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[16 * 1024]; // 16KB接收缓冲区
            List<byte> messageBuffer = new List<byte>();
            int headerSize = 4; // 4字节长度前缀

            try
            {
                while (!cancellationToken.IsCancellationRequested && IsSocketConnected())
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    var result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
#else
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
#endif

                    if (result.Count == 0)
                        continue;

                    // 将接收到的数据添加到消息缓冲区
                    for (int i = 0; i < result.Count; i++)
                    {
                        messageBuffer.Add(buffer[i]);
                    }

                    // 处理所有完整消息
                    while (messageBuffer.Count >= headerSize)
                    {
                        // 读取消息长度
                        int messageLength = BitConverter.ToInt32(messageBuffer.ToArray(), 0);

                        // 基本完整性检查
                        if (messageLength <= 0 || messageLength > 16 * 1024 * 1024) // 最大16MB
                        {
                            Debug.LogError($"收到无效的消息长度: {messageLength}");
                            messageBuffer.Clear();
                            break;
                        }

                        // 检查我们是否有一个完整的消息
                        if (messageBuffer.Count >= headerSize + messageLength)
                        {
                            // 提取完整消息
                            byte[] message = new byte[messageLength];
                            for (int i = 0; i < messageLength; i++)
                            {
                                message[i] = messageBuffer[headerSize + i];
                            }

                            // 从缓冲区中移除处理过的消息
                            messageBuffer.RemoveRange(0, headerSize + messageLength);

                            // 处理消息
                            _ = ProcessMessageAsync(message);
                        }
                        else
                        {
                            // 消息不完整，等待更多数据
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Debug.Log("接收循环已取消");
            }
            catch (Exception ex)
            {
                Debug.LogError($"接收循环中出错: {ex.Message}");
                // 连接可能丢失，触发清理
                await CleanupConnectionAsync();
            }
            finally
            {
                Debug.Log("接收循环已完成");
                // 确保如果循环意外退出，连接清理会发生
                await CleanupConnectionAsync();
            }
        }

        /// <summary>
        /// 处理接收到的消息负载
        /// </summary>
        private async Task ProcessMessageAsync(byte[] messageData)
        {
            try
            {
                var envelope = MemoryPackSerializer.Deserialize<MessageEnvelope>(messageData);

                switch (envelope.Type)
                {
                    case MessageType.Response:
                        HandleResponse(envelope.Payload);
                        break;
                    case MessageType.Event:
                        await HandleEventAsync(envelope.Payload);
                        break;
                    case MessageType.Heartbeat:
                        HandleHeartbeat(envelope.Payload);
                        break;
                    default:
                        Debug.LogWarning($"收到未知消息类型: {envelope.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理接收到的消息时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 将接收到的响应与待处理的请求匹配
        /// </summary>
        private void HandleResponse(byte[] payload)
        {
            try
            {
                var response = MemoryPackSerializer.Deserialize<PulseResponse>(payload);
                if (_pendingRequests.TryGetValue(response.RequestId, out var tcs))
                {
                    if (response.IsSuccess)
                    {
                        tcs.TrySetResult(response);
                    }
                    else
                    {
                        tcs.TrySetException(new RpcException(response.ErrorMessage ?? "RPC调用失败"));
                    }
                }
                else
                {
                    Debug.LogWarning($"收到未知请求ID的响应: {response.RequestId}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理响应时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 通过调用OnEventReceived委托处理接收到的事件
        /// </summary>
        private async Task HandleEventAsync(byte[] payload)
        {
            try
            {
                var eventPacket = MemoryPackSerializer.Deserialize<PulseEvent>(payload);
                var handler = OnEventReceived;
                if (handler != null)
                {
                    try
                    {
                        await handler.Invoke(eventPacket);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"OnEventReceived处理程序中出错: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理事件时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理接收到的心跳
        /// </summary>
        private void HandleHeartbeat(byte[] payload)
        {
            try
            {
                var heartbeat = MemoryPackSerializer.Deserialize<PulseHeartbeat>(payload);
                // 潜在的RTT计算或连接健康更新
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理心跳时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 定期发送心跳消息
        /// </summary>
        private async Task StartHeartbeatLoopAsync(CancellationToken cancellationToken, TimeSpan interval)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(interval, cancellationToken);

                    if (!IsConnected)
                        continue;

                    try
                    {
                        var heartbeat = new PulseHeartbeat { Timestamp = DateTime.UtcNow.Ticks };
                        var heartbeatBytes = MemoryPackSerializer.Serialize(heartbeat);
                        var envelope = new MessageEnvelope { Type = MessageType.Heartbeat, Payload = heartbeatBytes };
                        await SendEnvelopeAsync(envelope, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break; // 如果在发送过程中取消，则退出循环
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"发送心跳失败: {ex.Message}");
                        // 如果心跳持续失败，考虑断开连接
                        await CleanupConnectionAsync();
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("心跳循环已取消");
            }
            finally
            {
                Debug.Log("心跳循环已完成");
            }
        }

        /// <summary>
        /// 序列化并发送带有长度前缀的消息信封
        /// </summary>
        private async Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("无法发送，客户端未连接");
            }

            try
            {
                byte[] envelopeBytes = MemoryPackSerializer.Serialize(envelope);
                int messageLength = envelopeBytes.Length;

                // 创建带长度前缀的消息
                byte[] completeMessage = new byte[4 + messageLength];
                BitConverter.GetBytes(messageLength).CopyTo(completeMessage, 0);
                envelopeBytes.CopyTo(completeMessage, 4);

#if UNITY_WEBGL && !UNITY_EDITOR
                await _webSocket.SendAsync(completeMessage, cancellationToken);
#else
                await _webSocket.SendAsync(new ArraySegment<byte>(completeMessage), WebSocketMessageType.Binary, true, cancellationToken);
#endif
            }
            catch (ObjectDisposedException ex)
            {
                throw new InvalidOperationException("无法发送，连接已释放", ex);
            }
            catch (Exception ex)
            {
                // 网络错误
                await CleanupConnectionAsync(); // 假设连接丢失
                throw new RpcException("发送消息失败", ex);
            }
        }

        /// <summary>
        /// 检查WebSocket是否已连接
        /// </summary>
        private bool IsSocketConnected()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return _webSocket != null && _webSocket.IsConnected;
#else
            return _webSocket != null && _webSocket.State == WebSocketState.Open;
#endif
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await CleanupConnectionAsync();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// RPC特定错误抛出的异常
    /// </summary>
    public class RpcException : Exception
    {
        public RpcException(string message) : base(message) { }
        public RpcException(string message, Exception innerException) : base(message, innerException) { }
    }
}
