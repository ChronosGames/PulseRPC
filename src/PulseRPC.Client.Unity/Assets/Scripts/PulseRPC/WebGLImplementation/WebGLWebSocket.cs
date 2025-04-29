using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PulseRPC.WebGLImplementation
{
    /// <summary>
    /// WebGL平台特定的WebSocket实现，使用JavaScript互操作与浏览器WebSocket API交互。
    /// </summary>
    public class WebGLWebSocket
    {
        private int _instanceId = -1;
        private bool _isConnected = false;
        private TaskCompletionSource<bool> _connectionTcs;

        public bool IsConnected => _isConnected;

        /// <summary>
        /// 异步连接到WebSocket服务器
        /// </summary>
        public async Task ConnectAsync(string url, CancellationToken cancellationToken = default)
        {
            _connectionTcs = new TaskCompletionSource<bool>();

            // 注册回调
            WebSocketCallbacks.OnOpen += OnOpen;
            WebSocketCallbacks.OnClose += OnClose;
            WebSocketCallbacks.OnError += OnError;
            WebSocketCallbacks.OnMessage += OnMessage;

            // 创建WebSocket实例
            _instanceId = WebSocketCreate(url);
            if (_instanceId < 0)
            {
                throw new Exception("无法创建WebSocket实例");
            }

            // 使用链接令牌等待连接或取消
            using var registration = cancellationToken.Register(() => {
                _connectionTcs.TrySetCanceled();
                CloseInternal();
            });

            await _connectionTcs.Task;
            _isConnected = true;
        }

        /// <summary>
        /// 异步发送数据
        /// </summary>
        public Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                throw new InvalidOperationException("WebSocket未连接");

            // 转换字节数组为Base64字符串以通过JS接口发送
            string base64Data = Convert.ToBase64String(data);
            WebSocketSendData(_instanceId, base64Data);

            return Task.CompletedTask;
        }

        /// <summary>
        /// 异步接收数据
        /// 注意：在WebGL中，实际接收是基于事件的，此方法只是从消息队列中获取最后一条消息
        /// </summary>
        public Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default)
        {
            if (!_isConnected)
                throw new InvalidOperationException("WebSocket未连接");

            // 检查队列中的消息
            if (WebSocketCallbacks.MessageQueue.Count > 0)
            {
                if (WebSocketCallbacks.MessageQueue.TryDequeue(out var message))
                {
                    if (message.Length <= buffer.Length)
                    {
                        Array.Copy(message, buffer, message.Length);
                        return Task.FromResult(new WebSocketReceiveResult { Count = message.Length });
                    }
                    else
                    {
                        Debug.LogError($"接收缓冲区太小：需要 {message.Length} 字节，但只有 {buffer.Length} 字节可用");
                        // 只复制能容纳的部分
                        Array.Copy(message, buffer, buffer.Length);
                        return Task.FromResult(new WebSocketReceiveResult { Count = buffer.Length });
                    }
                }
            }

            // 如果没有消息，返回空结果
            return Task.FromResult(new WebSocketReceiveResult { Count = 0 });
        }

        /// <summary>
        /// 关闭WebSocket连接
        /// </summary>
        public Task CloseAsync()
        {
            CloseInternal();
            return Task.CompletedTask;
        }

        private void CloseInternal()
        {
            if (_instanceId >= 0)
            {
                WebSocketClose(_instanceId);
                _isConnected = false;
                _instanceId = -1;

                // 取消注册回调
                WebSocketCallbacks.OnOpen -= OnOpen;
                WebSocketCallbacks.OnClose -= OnClose;
                WebSocketCallbacks.OnError -= OnError;
                WebSocketCallbacks.OnMessage -= OnMessage;
            }
        }

        private void OnOpen(int instanceId)
        {
            if (instanceId == _instanceId)
            {
                _connectionTcs?.TrySetResult(true);
            }
        }

        private void OnClose(int instanceId)
        {
            if (instanceId == _instanceId)
            {
                _isConnected = false;
                _connectionTcs?.TrySetResult(false);
            }
        }

        private void OnError(int instanceId, string errorMsg)
        {
            if (instanceId == _instanceId)
            {
                _connectionTcs?.TrySetException(new Exception($"WebSocket错误: {errorMsg}"));
            }
        }

        private void OnMessage(int instanceId, byte[] data)
        {
            // 消息处理由WebSocketCallbacks处理并添加到队列
        }

        #region JavaScript Native Methods

        [DllImport("__Internal")]
        private static extern int WebSocketCreate(string url);

        [DllImport("__Internal")]
        private static extern void WebSocketClose(int instanceId);

        [DllImport("__Internal")]
        private static extern void WebSocketSendData(int instanceId, string base64Data);

        #endregion
    }

    /// <summary>
    /// 从WebSocket接收操作的结果
    /// </summary>
    public struct WebSocketReceiveResult
    {
        public int Count { get; set; }
    }
}
