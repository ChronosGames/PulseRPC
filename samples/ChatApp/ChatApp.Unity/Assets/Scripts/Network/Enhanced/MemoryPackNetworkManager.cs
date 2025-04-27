using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityTCP.MemoryPackIntegration;
using UnityTCP.MemoryPackModels;
using UnityTCP.ZeroCopy;

namespace UnityTCP.Enhanced
{
    /// <summary>
    /// 使用MemoryPack优化的网络管理器
    /// </summary>
    public class MemoryPackNetworkManager : MonoBehaviour
    {
        [Header("网络设置")]
        [SerializeField] private bool isServer = false;
        [SerializeField] private string serverIp = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private int sendBufferSize = 262144; // 256KB
        [SerializeField] private int receiveBufferSize = 262144; // 256KB
        [SerializeField] private bool disableNagle = true;

        [Header("性能优化")]
        [SerializeField] private bool enablePerformanceMonitoring = true;
        [SerializeField] private bool enableMemoryPooling = true;
        [SerializeField] private bool enableDirectMemoryAccess = true;

        // 内部组件
        private ZeroCopyTCPClient _client;
        private EnhancedTCPServer _server;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly ConcurrentDictionary<Type, Delegate> _messageHandlers = new ConcurrentDictionary<Type, Delegate>();

        // 性能监控
        private long _bytesSent;
        private long _bytesReceived;
        private long _messagesSent;
        private long _messagesReceived;
        private float _lastSendTime;

        // 预分配的缓冲区池
        private readonly ConcurrentStack<byte[]> _bufferPool = new ConcurrentStack<byte[]>();
        private const int BufferPoolSize = 20;
        private const int DefaultBufferSize = 4096;

        // 事件
        public event Action<string> ClientConnected;
        public event Action<string> ClientDisconnected;
        public event Action ServerStarted;
        public event Action ServerStopped;
        public event Action<Exception> ErrorOccurred;

        #region Unity生命周期

        private void Awake()
        {
            Application.runInBackground = true;

            // 初始化缓冲区池
            if (enableMemoryPooling)
            {
                for (int i = 0; i < BufferPoolSize; i++)
                {
                    _bufferPool.Push(new byte[DefaultBufferSize]);
                }
            }
        }

        private void Start()
        {
            if (isServer)
            {
                StartServerAsync(serverPort).ContinueWith(task => {
                    if (task.Exception != null)
                    {
                        LogError($"启动服务器失败: {task.Exception.Message}");
                    }
                });
            }
        }

        private void Update()
        {
            // 处理主线程任务
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    LogError($"执行主线程操作时出错: {ex.Message}");
                }
            }
        }

        private void OnDestroy()
        {
            StopServer();
            DisconnectClient();

            // 清理缓冲区池
            _bufferPool.Clear();
        }

        #endregion

        #region 服务器操作

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartServerAsync(int port)
        {
            if (_server != null)
            {
                LogWarning("服务器已在运行");
                return;
            }

            try
            {
                // 创建服务器选项
                var options = new EnhancedTCPServerOptions
                {
                    SendBufferSize = sendBufferSize,
                    ReceiveBufferSize = receiveBufferSize,
                    DisableNagle = disableNagle,
                    UseZeroCopy = enableDirectMemoryAccess
                };

                _server = new EnhancedTCPServer(options);

                // 绑定事件
                _server.ClientConnected += (clientId) =>
                {
                    _mainThreadActions.Enqueue(() => ClientConnected?.Invoke(clientId));
                };

                _server.ClientDisconnected += (clientId) =>
                {
                    _mainThreadActions.Enqueue(() => ClientDisconnected?.Invoke(clientId));
                };

                _server.DataReceived += HandleServerDataReceived;

                _server.ErrorOccurred += (ex) =>
                {
                    _mainThreadActions.Enqueue(() => ErrorOccurred?.Invoke(ex));
                };

                // 启动服务器
                await _server.StartAsync(port);

                _mainThreadActions.Enqueue(() =>
                {
                    Log($"服务器已启动，监听端口: {port}");
                    ServerStarted?.Invoke();
                });
            }
            catch (Exception ex)
            {
                LogError($"启动服务器失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void StopServer()
        {
            if (_server == null)
                return;

            _server.Dispose();
            _server = null;

            Log("服务器已停止");
            ServerStopped?.Invoke();
        }

        /// <summary>
        /// 处理服务器接收到的数据
        /// </summary>
        private void HandleServerDataReceived(string clientId, ReadOnlySequence<byte> data)
        {
            try
            {
                // 更新性能计数器
                Interlocked.Add(ref _bytesReceived, data.Length);
                Interlocked.Increment(ref _messagesReceived);

                // 处理不同类型的消息
                ProcessMessages(ref data);
            }
            catch (Exception ex)
            {
                LogError($"处理客户端数据时出错: {ex.Message}");
            }
        }

        #endregion

        #region 客户端操作

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectToServerAsync(string host = null, int? port = null)
        {
            if (_client != null)
            {
                LogWarning("客户端已连接");
                return;
            }

            string ip = host ?? serverIp;
            int portNumber = port ?? serverPort;

            try
            {
                _client = new ZeroCopyTCPClient();

                // 绑定事件
                _client.DataReceived += HandleClientDataReceived;

                _client.ErrorOccurred += (ex) =>
                {
                    _mainThreadActions.Enqueue(() => ErrorOccurred?.Invoke(ex));
                };

                _client.Disconnected += () =>
                {
                    _mainThreadActions.Enqueue(() =>
                    {
                        Log("与服务器的连接已断开");
                        _client = null;
                    });
                };

                // 连接到服务器
                await _client.ConnectAsync(ip, portNumber);

                Log($"已连接到服务器: {ip}:{portNumber}");
            }
            catch (Exception ex)
            {
                LogError($"连接到服务器失败: {ex.Message}");
                _client = null;
                throw;
            }
        }

        /// <summary>
        /// 断开与服务器的连接
        /// </summary>
        public void DisconnectClient()
        {
            if (_client == null)
                return;

            _client.Dispose();
            _client = null;

            Log("已断开与服务器的连接");
        }

        /// <summary>
        /// 处理客户端接收到的数据
        /// </summary>
        private void HandleClientDataReceived(ReadOnlySequence<byte> data)
        {
            try
            {
                // 更新性能计数器
                Interlocked.Add(ref _bytesReceived, data.Length);
                Interlocked.Increment(ref _messagesReceived);

                // 处理不同类型的消息
                ProcessMessages(ref data);
            }
            catch (Exception ex)
            {
                LogError($"处理服务器数据时出错: {ex.Message}");
            }
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        public void RegisterHandler<T>(Action<T> handler) where T : MemoryPack.IMemoryPackable<T>
        {
            _messageHandlers[typeof(T)] = handler;
        }

        /// <summary>
        /// 处理收到的消息
        /// </summary>
        private void ProcessMessages(ref ReadOnlySequence<byte> buffer)
        {
            var data = buffer;

            // 尝试解析不同类型的消息
            TryProcessMessageType<PlayerState>(ref data);
            TryProcessMessageType<WorldState>(ref data);
            TryProcessMessageType<NetworkCommand>(ref data);
            TryProcessMessageType<NetworkEvent>(ref data);
            TryProcessMessageType<ChatMessage>(ref data);
            TryProcessMessageType<BinaryPacket>(ref data);
        }

        /// <summary>
        /// 尝试处理特定类型的消息
        /// </summary>
        private void TryProcessMessageType<T>(ref ReadOnlySequence<byte> buffer) where T : MemoryPack.IMemoryPackable<T>
        {
            var data = buffer;

            // 查找并调用已注册的处理器
            if (!_messageHandlers.TryGetValue(typeof(T), out var handler)) return;

            while (MemoryPackNetworkAdapter.TryDeserialize<T>(ref data, out var message))
            {
                // 调度到主线程处理
                _mainThreadActions.Enqueue(() =>
                {
                    try
                    {
                        ((Action<T>)handler)(message);
                    }
                    catch (Exception ex)
                    {
                        LogError($"执行消息处理器时出错: {ex.Message}");
                    }
                });
            }
        }

        #endregion

        #region 消息发送

        /// <summary>
        /// 发送MemoryPack对象到服务器
        /// </summary>
        public async Task SendToServerAsync<T>(T message) where T : MemoryPack.IMemoryPackable<T>
        {
            if (_client == null)
            {
                LogError("客户端未连接");
                return;
            }

            try
            {
                await _client.SendMemoryPackObjectAsync(message);

                // 更新性能计数器
                Interlocked.Increment(ref _messagesSent);
                _lastSendTime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                LogError($"发送消息失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 广播MemoryPack对象到所有客户端
        /// </summary>
        public async Task BroadcastAsync<T>(T message) where T : MemoryPack.IMemoryPackable<T>
        {
            if (_server == null)
            {
                LogError("服务器未运行");
                return;
            }

            try
            {
                await _server.BroadcastMemoryPackObjectAsync(message);

                // 更新性能计数器
                Interlocked.Increment(ref _messagesSent);
                _lastSendTime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                LogError($"广播消息失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 发送MemoryPack对象到特定客户端
        /// </summary>
        public async Task SendToClientAsync<T>(string clientId, T message) where T : MemoryPack.IMemoryPackable<T>
        {
            if (_server == null)
            {
                LogError("服务器未运行");
                return;
            }

            try
            {
                await _server.SendMemoryPackObjectToClientAsync(clientId, message);

                // 更新性能计数器
                Interlocked.Increment(ref _messagesSent);
                _lastSendTime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                LogError($"发送消息到客户端失败: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取缓冲区从池中
        /// </summary>
        private byte[] GetBuffer(int minSize)
        {
            if (!enableMemoryPooling || minSize > DefaultBufferSize)
            {
                return new byte[minSize];
            }

            if (_bufferPool.TryPop(out var buffer))
            {
                return buffer;
            }

            return new byte[DefaultBufferSize];
        }

        /// <summary>
        /// 返回缓冲区到池
        /// </summary>
        private void ReturnBuffer(byte[] buffer)
        {
            if (!enableMemoryPooling || buffer == null || buffer.Length != DefaultBufferSize)
            {
                return;
            }

            // 清空缓冲区
            Array.Clear(buffer, 0, buffer.Length);
            _bufferPool.Push(buffer);
        }

        /// <summary>
        /// 获取性能统计信息
        /// </summary>
        public string GetPerformanceStats()
        {
            return $"发送: {_messagesSent} 消息 ({_bytesSent} 字节), " +
                   $"接收: {_messagesReceived} 消息 ({_bytesReceived} 字节), " +
                   $"最后发送: {(Time.realtimeSinceStartup - _lastSendTime):F2}秒前";
        }

        /// <summary>
        /// 重置性能计数器
        /// </summary>
        public void ResetPerformanceCounters()
        {
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            Interlocked.Exchange(ref _messagesSent, 0);
            Interlocked.Exchange(ref _messagesReceived, 0);
        }

        // 日志辅助方法
        private void Log(string message) => Debug.Log($"[MemoryPackNetwork] {message}");
        private void LogWarning(string message) => Debug.LogWarning($"[MemoryPackNetwork] {message}");
        private void LogError(string message) => Debug.LogError($"[MemoryPackNetwork] {message}");

        #endregion
    }
}
