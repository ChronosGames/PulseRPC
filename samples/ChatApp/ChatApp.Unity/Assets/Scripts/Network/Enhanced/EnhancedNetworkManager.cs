using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityTCP.Memory;
using UnityTCP.Serialization;
using UnityTCP.ZeroCopy;

namespace UnityTCP.Enhanced
{
    /// <summary>
    /// 增强版网络管理器，支持对象直接序列化到网卡
    /// </summary>
    public class EnhancedNetworkManager : MonoBehaviour
    {
        [Header("网络设置")] [SerializeField] private int sendBufferSize = 262144; // 256KB
        [SerializeField] private int receiveBufferSize = 262144; // 256KB
        [SerializeField] private bool disableNagle = true;
        [SerializeField] private bool useZeroCopy = true;
        [SerializeField] private int maxConcurrentSends = 8;
        [SerializeField] private bool enableSocketPollOptimization = true;
        [SerializeField] private bool enableDirectNetworkAccess = true;

        [Header("性能监控")] [SerializeField] private bool enablePerfMetrics = true;

        // 内部组件
        private ZeroCopyTCPClient _client;
        private EnhancedTCPServer _server;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

        // 性能指标
        private long _bytesSent;
        private long _bytesReceived;
        private long _messagesSent;
        private long _messagesReceived;
        private long _peakMemoryUsage;
        private float _averageLatency;
        private readonly Queue<float> _latencySamples = new Queue<float>(100);

        // 对象池，减少GC压力
        private readonly ObjectPool<byte[]> _bufferPool = new ObjectPool<byte[]>();

        // 事件
        public event Action<string> ClientConnected;
        public event Action<string> ClientDisconnected;
        public event Action ServerStarted;
        public event Action ServerStopped;
        public event Action<Exception> ErrorOccurred;

        // 类型化消息事件
        private readonly ConcurrentDictionary<Type, Delegate> _messageHandlers =
            new ConcurrentDictionary<Type, Delegate>();

        private void Awake()
        {
            Application.runInBackground = true;

            if (enablePerfMetrics)
            {
                InvokeRepeating(nameof(UpdateMetrics), 1f, 1f);
            }
        }

        private void Update()
        {
            // 处理需要在主线程执行的操作
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }

        private void OnDestroy()
        {
            StopServer();
            DisconnectClient();
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartServerAsync(int port)
        {
            if (_server != null)
            {
                Debug.LogWarning("Server is already running.");
                return;
            }

            try
            {
                _server = new EnhancedTCPServer(new EnhancedTCPServerOptions
                {
                    SendBufferSize = sendBufferSize,
                    ReceiveBufferSize = receiveBufferSize,
                    DisableNagle = disableNagle,
                    UseZeroCopy = useZeroCopy,
                    MaxConcurrentSends = maxConcurrentSends,
                    EnableSocketPollOptimization = enableSocketPollOptimization
                });

                _server.ClientConnected += clientId =>
                {
                    _mainThreadActions.Enqueue(() => ClientConnected?.Invoke(clientId));
                };

                _server.ClientDisconnected += (clientId) =>
                {
                    _mainThreadActions.Enqueue(() => ClientDisconnected?.Invoke(clientId));
                };

                _server.ErrorOccurred += (ex) => { _mainThreadActions.Enqueue(() => ErrorOccurred?.Invoke(ex)); };

                _server.DataReceived += HandleServerDataReceived;

                await _server.StartAsync(port);
                ServerStarted?.Invoke();
                Debug.Log($"Enhanced server started on port {port}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Debug.LogError($"Failed to start server: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void StopServer()
        {
            if (_server == null)
                return;

            _server.Stop();
            _server = null;
            ServerStopped?.Invoke();
            Debug.Log("Server stopped");
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectToServerAsync(string ip, int port)
        {
            if (_client != null)
            {
                Debug.LogWarning("Client is already connected.");
                return;
            }

            try
            {
                _client = new ZeroCopyTCPClient();

                _client.DataReceived += HandleClientDataReceived;
                _client.ErrorOccurred += (ex) => { _mainThreadActions.Enqueue(() => ErrorOccurred?.Invoke(ex)); };

                _client.Disconnected += () =>
                {
                    _mainThreadActions.Enqueue(() =>
                    {
                        Debug.Log("Disconnected from server");
                        _client = null;
                    });
                };

                await _client.ConnectAsync(ip, port);
                Debug.Log($"Connected to server at {ip}:{port}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Debug.LogError($"Failed to connect: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开客户端连接
        /// </summary>
        public void DisconnectClient()
        {
            if (_client == null)
                return;

            _client.Dispose();
            _client = null;
            Debug.Log("Disconnected from server");
        }

        #region 发送对象方法

        /// <summary>
        /// 发送结构体对象到服务器（零拷贝）
        /// </summary>
        public async Task SendObjectToServerAsync<T>(T obj) where T : struct, INetworkSerializable
        {
            if (_client == null)
            {
                Debug.LogError("Client is not connected.");
                return;
            }

            try
            {
                await _client.SendObjectAsync(obj);
                Interlocked.Increment(ref _messagesSent);
                Interlocked.Add(ref _bytesSent, obj.GetSerializedSize() + sizeof(int));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Debug.LogError($"Failed to send object: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送Blittable对象到服务器（完全零拷贝）
        /// </summary>
        public async Task SendBlittableToServerAsync<T>(T obj) where T : unmanaged
        {
            if (_client == null)
            {
                Debug.LogError("Client is not connected.");
                return;
            }

            try
            {
                await _client.SendBlittableAsync(obj);
                Interlocked.Increment(ref _messagesSent);
                Interlocked.Add(ref _bytesSent, Marshal.SizeOf<T>() + sizeof(int));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Debug.LogError($"Failed to send blittable object: {ex.Message}");
            }
        }

        /// <summary>
        /// 向所有客户端广播对象（零拷贝）
        /// </summary>
        public async Task BroadcastObjectAsync<T>(T obj) where T : struct, INetworkSerializable
        {
            if (_server == null)
            {
                Debug.LogError("Server is not running.");
                return;
            }

            try
            {
                await _server.BroadcastObjectAsync(obj);
                Interlocked.Increment(ref _messagesSent);
                Interlocked.Add(ref _bytesSent, obj.GetSerializedSize() + sizeof(int));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Debug.LogError($"Failed to broadcast object: {ex.Message}");
            }
        }

        /// <summary>
        /// 向特定客户端发送对象（零拷贝）
        /// </summary>
        public async Task SendObjectToClientAsync<T>(string clientId, T obj) where T : struct, INetworkSerializable
        {
            if (_server == null)
            {
                Debug.LogError("Server is not running.");
                return;
            }

            try
            {
                await _server.SendObjectToClientAsync(clientId, obj);
                Interlocked.Increment(ref _messagesSent);
                Interlocked.Add(ref _bytesSent, obj.GetSerializedSize() + sizeof(int));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
                Debug.LogError($"Failed to send object to client: {ex.Message}");
            }
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 注册特定类型的消息处理器
        /// </summary>
        public void RegisterHandler<T>(Action<T> handler) where T : struct, INetworkSerializable
        {
            _messageHandlers[typeof(T)] = handler;
        }

        /// <summary>
        /// 注册特定类型的Blittable消息处理器
        /// </summary>
        public void RegisterBlittableHandler<T>(Action<T> handler) where T : unmanaged
        {
            _messageHandlers[typeof(T)] = handler;
        }

        /// <summary>
        /// 处理服务器收到的数据
        /// </summary>
        private void HandleServerDataReceived(string clientId, ReadOnlySequence<byte> data)
        {
            Interlocked.Add(ref _bytesReceived, data.Length);
            Interlocked.Increment(ref _messagesReceived);

            ProcessIncomingData(data);
        }

        /// <summary>
        /// 处理客户端收到的数据
        /// </summary>
        private void HandleClientDataReceived(ReadOnlySequence<byte> data)
        {
            Interlocked.Add(ref _bytesReceived, data.Length);
            Interlocked.Increment(ref _messagesReceived);

            ProcessIncomingData(data);
        }

        /// <summary>
        /// 处理接收到的数据
        /// </summary>
        private void ProcessIncomingData(ReadOnlySequence<byte> data)
        {
            // 检查数据的头4个字节，确定消息类型
            if (data.Length < 8) // 4字节消息长度 + 4字节消息类型ID
                return;

            var headerBytes = data.Slice(0, 8).ToArray();
            var messageLength = BitConverter.ToInt32(headerBytes, 0);
            var messageTypeId = BitConverter.ToInt32(headerBytes, 4);

            // 根据消息类型ID进行消息分发
            // 这里需要一个注册系统，将消息类型ID映射到对应的处理器
            // 在实际项目中，可以使用反射或代码生成来自动化这个过程

            // 调度到主线程处理
            _mainThreadActions.Enqueue(() =>
            {
                // 实际项目中在这里调用已注册的处理器
                Debug.Log($"Received message: Type={messageTypeId}, Length={messageLength}");
            });
        }

        #endregion

        #region 性能监控

        /// <summary>
        /// 更新性能指标
        /// </summary>
        private void UpdateMetrics()
        {
            if (!enablePerfMetrics)
                return;

            // 这里可以添加更多性能指标的计算
            // 例如：延迟测量、内存使用等

            _peakMemoryUsage = Math.Max(_peakMemoryUsage, GC.GetTotalMemory(false));

            // 输出性能指标
            Debug.Log($"Network Metrics: Sent={_bytesSent}B ({_messagesSent} msgs), " +
                      $"Received={_bytesReceived}B ({_messagesReceived} msgs), " +
                      $"Peak Memory={_peakMemoryUsage / 1024}KB, " +
                      $"Avg Latency={_averageLatency}ms");
        }

        /// <summary>
        /// 记录延迟样本
        /// </summary>
        private void AddLatencySample(float latencyMs)
        {
            lock (_latencySamples)
            {
                _latencySamples.Enqueue(latencyMs);
                if (_latencySamples.Count > 100)
                    _latencySamples.Dequeue();

                // 计算平均延迟
                var sum = _latencySamples.Sum();

                _averageLatency = sum / _latencySamples.Count;
            }
        }

        #endregion
    }
}
