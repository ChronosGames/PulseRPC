namespace PulseRPC.Client.Unity
{
    /// <summary>
    /// Unity专用服务代理工厂
    /// </summary>
    public class UnityServiceProxyFactory : IServiceProxyGenerator
    {
        private readonly IMessageChannel _messageChannel;
        private readonly Dictionary<Type, object> _proxies = new Dictionary<Type, object>();

        public UnityServiceProxyFactory(IMessageChannel messageChannel)
        {
            _messageChannel = messageChannel;
        }

        public T CreateProxy<T>() where T : class, INetworkService
        {
            Type serviceType = typeof(T);

            lock (_proxies)
            {
                if (_proxies.TryGetValue(serviceType, out var existing))
                {
                    return (T)existing;
                }

                // 获取预生成的代理类型
                string proxyTypeName = $"{serviceType.Namespace}.{serviceType.Name.Substring(1)}Proxy";
                Type proxyType = Type.GetType(proxyTypeName) ??
                                 Type.GetType(proxyTypeName + ", " + serviceType.Assembly.GetName().Name);

                if (proxyType == null)
                {
                    throw new InvalidOperationException(
                        $"找不到{serviceType.Name}的代理类，请确保应用了[GameService]特性并正确运行了源代码生成器");
                }

                // 创建代理实例
                var proxy = Activator.CreateInstance(proxyType, _messageChannel);
                _proxies[serviceType] = proxy;
                return (T)proxy;
            }
        }
    }

    /// <summary>
    /// Unity主线程调度器
    /// </summary>
    public class UnityMainThreadDispatcher : MonoBehaviour
    {
        private static UnityMainThreadDispatcher _instance;
        private readonly Queue<Action> _actions = new Queue<Action>();
        private readonly object _lockObject = new object();

        public static UnityMainThreadDispatcher Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("UnityMainThreadDispatcher");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<UnityMainThreadDispatcher>();
                }
                return _instance;
            }
        }

        public void Enqueue(Action action)
        {
            lock (_lockObject)
            {
                _actions.Enqueue(action);
            }
        }

        private void Update()
        {
            lock (_lockObject)
            {
                while (_actions.Count > 0)
                {
                    _actions.Dequeue().Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Unity专用网络客户端
    /// </summary>
    public class UnityNetworkClient : MonoBehaviour
    {
        [Header("网络设置")] [SerializeField] private string _host = "localhost";
        [SerializeField] private int _port = 7000;
        [SerializeField] private float _reconnectInterval = 5f;

        // 网络状态
        public bool IsConnected => _networkClient?.IsConnected ?? false;
        public ConnectionState ConnectionState { get; private set; }

        // 事件
        public event Action Connected;
        public event Action Disconnected;
        public event Action<Exception> ConnectionError;

        // 内部字段
        private NetworkClient _networkClient;
        private bool _autoReconnect = true;
        private CancellationTokenSource _reconnectCts;

        private void Awake()
        {
            // 创建网络客户端
            var config = new NetworkConfig { Host = _host, Port = _port };

            _networkClient = new NetworkClient(config);

            // 注册事件
            _networkClient.Connected += OnNetworkConnected;
            _networkClient.Disconnected += OnNetworkDisconnected;
            _networkClient.ErrorOccurred += OnNetworkError;

            // 设置状态
            ConnectionState = ConnectionState.Disconnected;

            // 保持场景切换时不销毁
            DontDestroyOnLoad(this.gameObject);
        }

        private void OnDestroy()
        {
            // 取消重连
            _reconnectCts?.Cancel();

            // 断开连接
            _ = _networkClient?.DisconnectAsync();

            // 取消事件订阅
            if (_networkClient != null)
            {
                _networkClient.Connected -= OnNetworkConnected;
                _networkClient.Disconnected -= OnNetworkDisconnected;
                _networkClient.ErrorOccurred -= OnNetworkError;
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync()
        {
            if (IsConnected || ConnectionState == ConnectionState.Connecting)
                return;

            ConnectionState = ConnectionState.Connecting;

            try
            {
                await _networkClient.ConnectAsync();
            }
            catch (Exception ex)
            {
                ConnectionState = ConnectionState.Disconnected;
                OnNetworkError(this, ex);

                // 启动自动重连
                if (_autoReconnect)
                {
                    StartReconnection();
                }

                throw;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            // 停止重连
            _reconnectCts?.Cancel();
            _autoReconnect = false;

            // 断开连接
            if (IsConnected)
            {
                await _networkClient.DisconnectAsync();
            }

            ConnectionState = ConnectionState.Disconnected;
        }

        /// <summary>
        /// 获取服务代理
        /// </summary>
        public T GetService<T>() where T : class, INetworkService
        {
            return _networkClient.GetService<T>();
        }

        /// <summary>
        /// 获取事件处理器
        /// </summary>
        public IEventHandler<T> GetEventHandler<T>() where T : class, IEventSubscriber
        {
            // 返回自动生成的事件处理器
            Type handlerType = Type.GetType($"{typeof(T).FullName}Handler");
            if (handlerType == null)
                throw new InvalidOperationException($"找不到事件处理器: {typeof(T).FullName}Handler");

            return (IEventHandler<T>)Activator.CreateInstance(handlerType, _networkClient.EventBus);
        }

        // 处理网络连接事件
        private void OnNetworkConnected(object sender, EventArgs e)
        {
            // 切换到Unity主线程执行
            MainThreadDispatcher.Enqueue(() =>
            {
                ConnectionState = ConnectionState.Connected;
                _reconnectCts?.Cancel();
                Connected?.Invoke();
            });
        }

        // 处理网络断开事件
        private void OnNetworkDisconnected(object sender, EventArgs e)
        {
            // 切换到Unity主线程执行
            MainThreadDispatcher.Enqueue(() =>
            {
                ConnectionState = ConnectionState.Disconnected;
                Disconnected?.Invoke();

                // 启动自动重连
                if (_autoReconnect)
                {
                    StartReconnection();
                }
            });
        }

        // 处理网络错误事件
        private void OnNetworkError(object sender, Exception e)
        {
            // 切换到Unity主线程执行
            MainThreadDispatcher.Enqueue(() =>
            {
                ConnectionError?.Invoke(e);
            });
        }

        // 启动自动重连
        private async void StartReconnection()
        {
            // 取消之前的重连
            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();

            try
            {
                while (!IsConnected && _autoReconnect)
                {
                    // 等待重连间隔
                    await Task.Delay(TimeSpan.FromSeconds(_reconnectInterval), _reconnectCts.Token);

                    if (_reconnectCts.IsCancellationRequested)
                        break;

                    // 尝试重连
                    try
                    {
                        ConnectionState = ConnectionState.Connecting;
                        await _networkClient.ConnectAsync();
                    }
                    catch
                    {
                        // 重连失败，继续循环
                        ConnectionState = ConnectionState.Disconnected;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 重连被取消
            }
        }
    }

    /// <summary>
    /// 连接状态枚举
    /// </summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Unity主线程调度器
    /// </summary>
    internal static class MainThreadDispatcher
    {
        private static readonly Queue<Action> _actionQueue = new Queue<Action>();
        private static readonly object _lock = new object();
        private static MonoBehaviour _behaviour;

        // 初始化调度器
        public static void Initialize(MonoBehaviour behaviour)
        {
            _behaviour = behaviour;
        }

        // 入队操作
        public static void Enqueue(Action action)
        {
            lock (_lock)
            {
                _actionQueue.Enqueue(action);
            }

            // 确保在主线程执行
            if (_behaviour != null)
            {
                _behaviour.StartCoroutine(ExecuteQueue());
            }
        }

        // 执行队列
        private static IEnumerator ExecuteQueue()
        {
            yield return null;

            Action[] actions;
            lock (_lock)
            {
                actions = _actionQueue.ToArray();
                _actionQueue.Clear();
            }

            foreach (var action in actions)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }

    /// <summary>
    /// Unity消息通道包装器，确保回调在主线程执行
    /// </summary>
    public class UnityMessageChannel : IMessageChannel
    {
        private readonly IMessageChannel _innerChannel;

        public UnityMessageChannel(IMessageChannel innerChannel)
        {
            _innerChannel = innerChannel;
        }

        public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(
            string serviceName, string methodName, TRequest request, CancellationToken cancellationToken = default)
        {
            TResponse response = await _innerChannel.SendRequestAsync<TRequest, TResponse>(
                serviceName, methodName, request, cancellationToken);

            // 确保回到Unity主线程
            await SwitchToMainThread();

            return response;
        }

        public async Task SendEventAsync<T>(
            string eventName, T eventData, CancellationToken cancellationToken = default)
        {
            await _innerChannel.SendEventAsync(eventName, eventData, cancellationToken);

            // 确保回到Unity主线程
            await SwitchToMainThread();
        }

        public ISubscriptionToken SubscribeToEvent<T>(
            string eventName, NetworkEventHandler<T> handler)
        {
            // 包装处理器，确保在主线程执行
            NetworkEventHandler<T> wrappedHandler = evt =>
            {
                // 如果已经在主线程，直接调用
                if (Thread.CurrentThread.ManagedThreadId == 1)
                {
                    handler(evt);
                }
                else
                {
                    // 否则，调度到主线程
                    UnityMainThreadDispatcher.Instance.Enqueue(() => handler(evt));
                }
            };

            return _innerChannel.SubscribeToEvent(eventName, wrappedHandler);
        }

        private Task SwitchToMainThread()
        {
            // 如果已经在主线程，直接返回
            if (Thread.CurrentThread.ManagedThreadId == 1)
            {
                return Task.CompletedTask;
            }

            // 否则，创建任务并调度到主线程
            var tcs = new TaskCompletionSource<bool>();

            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                tcs.SetResult(true);
            });

            return tcs.Task;
        }

        public void Dispose()
        {
            _innerChannel?.Dispose();
        }
    }
}
