using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PulseRPC.WebGLImplementation
{
    /// <summary>
    /// 处理来自JavaScript的WebSocket回调的静态类
    /// </summary>
    public static class WebSocketCallbacks
    {
        // 事件处理程序
        public static event Action<int> OnOpen;
        public static event Action<int> OnClose;
        public static event Action<int, string> OnError;
        public static event Action<int, byte[]> OnMessage;

        // 消息队列用于存储接收到的消息
        public static ConcurrentQueue<byte[]> MessageQueue = new ConcurrentQueue<byte[]>();

        // 维护消息队列的最大大小以避免内存泄漏
        private const int MaxQueueSize = 100;

        /// <summary>
        /// 由JavaScript调用 - 当WebSocket连接打开时
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(Action<int>))]
        public static void OnOpenCallback(int instanceId)
        {
            try
            {
                // 将回调分派到主线程
                MainThreadDispatcher.Enqueue(() => {
                    OnOpen?.Invoke(instanceId);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket OnOpen回调错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 由JavaScript调用 - 当WebSocket连接关闭时
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(Action<int>))]
        public static void OnCloseCallback(int instanceId)
        {
            try
            {
                MainThreadDispatcher.Enqueue(() => {
                    OnClose?.Invoke(instanceId);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket OnClose回调错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 由JavaScript调用 - 当WebSocket发生错误时
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        public static void OnErrorCallback(int instanceId, string errorMsg)
        {
            try
            {
                MainThreadDispatcher.Enqueue(() => {
                    OnError?.Invoke(instanceId, errorMsg);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket OnError回调错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 由JavaScript调用 - 当WebSocket接收到消息时
        /// </summary>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        public static void OnMessageCallback(int instanceId, string base64Data)
        {
            try
            {
                // 将Base64数据解码为字节数组
                byte[] binaryData = Convert.FromBase64String(base64Data);

                // 添加到消息队列，供WebSocket.ReceiveAsync使用
                if (MessageQueue.Count >= MaxQueueSize)
                {
                    // 如果队列已满，移除最早的消息
                    byte[] oldMessage;
                    MessageQueue.TryDequeue(out oldMessage);
                }

                MessageQueue.Enqueue(binaryData);

                MainThreadDispatcher.Enqueue(() => {
                    OnMessage?.Invoke(instanceId, binaryData);
                });
            }
            catch (Exception ex)
            {
                Debug.LogError($"WebSocket OnMessage回调错误: {ex.Message}");
            }
        }
    }
}
