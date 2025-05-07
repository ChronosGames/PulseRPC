using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Serialization;
using PulseRPC.Serialization;
using UnityEngine;

namespace PulseRPC
{
    /// <summary>
    /// Unity客户端适配器，提供Unity环境下的RPC客户端功能
    /// </summary>
    public class UnityClient : MonoBehaviour
    {
        [SerializeField] private string serverAddress = "127.0.0.1";
        [SerializeField] private int serverPort = 5000;
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private float reconnectInterval = 5f;

        private TcpClient _client;
        private CancellationTokenSource _cts;
        private bool _isConnecting;
        private float _reconnectTimer;
        private readonly Dictionary<Type, IMessageHandler> _messageHandlers = new Dictionary<Type, IMessageHandler>();
        private readonly Dictionary<string, TaskCompletionSource<byte[]>> _pendingRequests = new Dictionary<string, TaskCompletionSource<byte[]>>();

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<bool> OnConnectionStateChanged;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;

        private void Awake()
        {
            // 创建客户端实例
            _client = new TcpClient();
            _cts = new CancellationTokenSource();

            // 注册连接状态变化事件
            _client.ConnectionStateChanged += OnClientConnectionStateChanged;

            // 注册消息接收事件
            _client.MessageReceived += OnMessageReceived;
        }

        private void Start()
        {
            if (autoConnect)
            {
                Connect();
            }
        }

        private void Update()
        {
            // 自动重连逻辑
            if (autoConnect && !IsConnected && !_isConnecting)
            {
                _reconnectTimer += Time.deltaTime;
                if (_reconnectTimer >= reconnectInterval)
                {
                    _reconnectTimer = 0;
                    Connect();
                }
            }
        }

        private void OnDestroy()
        {
            // 断开连接并释放资源
            _cts.Cancel();
            _client.ConnectionStateChanged -= OnClientConnectionStateChanged;
            _client.MessageReceived -= OnMessageReceived;
            _client.Dispose();
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async void Connect()
        {
            if (IsConnected || _isConnecting)
                return;

            _isConnecting = true;
            try
            {
                await _client.ConnectAsync(serverAddress, serverPort, _cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogError($"连接服务器失败: {ex.Message}");
            }
            finally
            {
                _isConnecting = false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _client.Disconnect();
        }

        /// <summary>
        /// 发送RPC请求
        /// </summary>
        /// <typeparam name="TRequest">请求类型</typeparam>
        /// <typeparam name="TResponse">响应类型</typeparam>
        /// <param name="request">请求对象</param>
        /// <param name="timeout">超时时间(毫秒)</param>
        /// <returns>响应对象</returns>
        public async Task<TResponse> SendRequest<TRequest, TResponse>(TRequest request, int timeout = 5000)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("客户端未连接");
            }

            // 使用AOT友好的序列化器
            var requestSerializer = AOTSerializerFactory.GetSerializer<TRequest>();
            var responseSerializer = AOTSerializerFactory.GetSerializer<TResponse>();

            // 序列化请求
            var requestData = requestSerializer.Serialize(request);

            // 发送请求并等待响应
            var responseData = await _client.SendRequestAsync(
                typeof(TRequest).FullName ?? "UnknownRequest",
                requestData,
                timeout,
                _cts.Token);

            // 反序列化响应
            return responseSerializer.Deserialize(responseData);
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <param name="handler">消息处理器</param>
        public void RegisterHandler(IMessageHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _messageHandlers[handler.MessageType] = handler;
        }

        /// <summary>
        /// 注销消息处理器
        /// </summary>
        /// <param name="messageType">消息类型</param>
        public void UnregisterHandler(Type messageType)
        {
            if (messageType == null)
                throw new ArgumentNullException(nameof(messageType));

            _messageHandlers.Remove(messageType);
        }

        /// <summary>
        /// 处理接收到的消息
        /// </summary>
        private void OnMessageReceived(string messageType, byte[] data)
        {
            // 在主线程上处理消息
            MainThreadDispatcher.Instance.Enqueue(() =>
            {
                try
                {
                    // 检查是否为响应消息
                    if (_pendingRequests.TryGetValue(messageType, out var tcs))
                    {
                        // 完成请求任务
                        tcs.SetResult(data);
                        _pendingRequests.Remove(messageType);
                        return;
                    }

                    // 查找对应的消息类型
                    var type = Type.GetType(messageType);
                    if (type == null)
                    {
                        Debug.LogWarning($"找不到消息类型: {messageType}");
                        return;
                    }

                    // 查找对应的处理器
                    if (!_messageHandlers.TryGetValue(type, out var handler))
                    {
                        Debug.LogWarning($"找不到消息处理器: {messageType}");
                        return;
                    }

                    // 反序列化消息
                    var message = MessageSerializer.Deserialize(type, data);

                    // 处理消息
                    handler.HandleMessage(message);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"处理消息失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 客户端连接状态变化处理
        /// </summary>
        private void OnClientConnectionStateChanged(bool isConnected)
        {
            // 在主线程上触发事件
            MainThreadDispatcher.Instance.Enqueue(() =>
            {
                OnConnectionStateChanged?.Invoke(isConnected);

                if (isConnected)
                {
                    Debug.Log($"已连接到服务器: {serverAddress}:{serverPort}");
                }
                else
                {
                    Debug.Log("已断开与服务器的连接");

                    // 取消所有挂起的请求
                    foreach (var tcs in _pendingRequests.Values)
                    {
                        tcs.TrySetCanceled();
                    }
                    _pendingRequests.Clear();
                }
            });
        }
    }

    /// <summary>
    /// 主线程调度器，用于在主线程上执行操作
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static MainThreadDispatcher _instance;
        private readonly System.Collections.Generic.Queue<Action> _actions = new System.Collections.Generic.Queue<Action>();

        public static MainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("MainThreadDispatcher");
                    _instance = go.AddComponent<MainThreadDispatcher>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public void Enqueue(Action action)
        {
            lock (_actions)
            {
                _actions.Enqueue(action);
            }
        }

        private void Update()
        {
            lock (_actions)
            {
                while (_actions.Count > 0)
                {
                    _actions.Dequeue()?.Invoke();
                }
            }
        }
    }
}
