namespace ChatApp;

public class NetworkManager : MonoBehaviour
{
    [Header("服务器配置")]
    [SerializeField] private string _host = "game-server.example.com";
    [SerializeField] private int _tcpPort = 7000;
    [SerializeField] private int _kcpPort = 7001;

    // 通道管理器
    private ChannelManager _channelManager;
    private ChannelFactory _channelFactory;

    // 游戏服务
    private IPlayerService _playerService;

    // 事件处理器
    private IEventHandler<IPlayerLoginEvents> _loginEventsHandler;
    private IEventHandler<IPlayerMovementEvents> _movementEventsHandler;

    // 事件订阅
    private List<ISubscriptionToken> _subscriptions = new List<ISubscriptionToken>();

    private void Awake()
    {
        // 创建通道工厂和管理器
        _channelFactory = new ChannelFactory();
        _channelManager = new ChannelManager();

        // 初始化网络系统
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        // 初始化通道
        await InitializeChannelsAsync();

        // 获取服务代理
        _playerService = new PlayerServiceProxy(_channelManager);

        // 获取事件处理器
        _loginEventsHandler = new PlayerLoginEventsHandler(_channelManager.GetChannel("TcpChannel"));
        _movementEventsHandler = new PlayerMovementEventsHandler(_channelManager.GetChannel("KcpChannel"));

        // 连接到服务器
        await ConnectAsync();
    }

    private async Task InitializeChannelsAsync()
    {
        try
        {
            // 创建TCP通道
            var tcpOptions = new TransportOptions
            {
                ReadBufferSize = 8192,
                WriteBufferSize = 8192,
                ConnectionTimeout = 5000,
                UseCompression = true
            };

            var tcpChannel = await _channelFactory.CreateChannelAsync("TcpChannel", TransportType.Tcp, tcpOptions);
            _channelManager.RegisterChannel("TcpChannel", tcpChannel, true);

            // 创建KCP通道
            var kcpOptions = new TransportOptions
            {
                ReadBufferSize = 8192,
                WriteBufferSize = 8192,
                ConnectionTimeout = 5000,
                UseCompression = false,
                CustomOptions = { { "KcpNoDelay", 1 } }
            };

            var kcpChannel = await _channelFactory.CreateChannelAsync("KcpChannel", TransportType.Kcp, kcpOptions);
            _channelManager.RegisterChannel("KcpChannel", kcpChannel);

            Debug.Log("通道初始化完成");
        }
        catch (Exception ex)
        {
            Debug.LogError($"通道初始化失败: {ex.Message}");
        }
    }

    private async Task ConnectAsync()
    {
        try
        {
            // 连接TCP通道
            var tcpChannel = _channelManager.GetChannel("TcpChannel") as MessageChannel;
            await tcpChannel.ConnectAsync(_host, _tcpPort, CancellationToken.None);

            // 连接KCP通道
            var kcpChannel = _channelManager.GetChannel("KcpChannel") as MessageChannel;
            await kcpChannel.ConnectAsync(_host, _kcpPort, CancellationToken.None);

            Debug.Log("已连接到服务器");

            // 登录
            await LoginAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"连接失败: {ex.Message}");
        }
    }

    private async Task LoginAsync()
    {
        try
        {
            var response = await _playerService.LoginAsync(new LoginRequest
            {
                Username = "Player" + UnityEngine.Random.Range(1000, 9999),
                Password = "password"
            });

            if (response.Success)
            {
                Debug.Log($"登录成功: {response.Player.Username}");

                // 订阅事件
                SubscribeToEvents();
            }
            else
            {
                Debug.LogError($"登录失败: {response.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"登录失败: {ex.Message}");
        }
    }

    private void SubscribeToEvents()
    {
        // 登录事件处理器
        var loginHandler = new PlayerLoginEventsImpl();
        var loginToken = _loginEventsHandler.Subscribe(loginHandler);
        _subscriptions.Add(loginToken);

        // 移动事件处理器
        var movementHandler = new PlayerMovementEventsImpl();
        var movementToken = _movementEventsHandler.Subscribe(movementHandler);
        _subscriptions.Add(movementToken);

        Debug.Log("已订阅游戏事件");
    }

    private void OnDestroy()
    {
        // 清理订阅
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        // 断开连接
        _channelManager?.Dispose();
    }

    // 实现事件接口
    private class PlayerLoginEventsImpl : IPlayerLoginEvents
    {
        public void OnPlayerJoined(PlayerJoinedEvent eventData)
        {
            Debug.Log($"玩家加入: {eventData.PlayerName} (ID: {eventData.PlayerId})");
            // 处理玩家加入...
        }

        public void OnPlayerLeft(PlayerLeftEvent eventData)
        {
            Debug.Log($"玩家离开: {eventData.PlayerId}, 原因: {eventData.Reason}");
            // 处理玩家离开...
        }
    }

    private class PlayerMovementEventsImpl : IPlayerMovementEvents
    {
        public void OnPlayerMoved(PlayerMovedEvent eventData)
        {
            // 更新玩家位置
            var playerObject = GetPlayerObject(eventData.PlayerId);
            if (playerObject != null)
            {
                var position = new Vector3(eventData.X, eventData.Y, eventData.Z);
                playerObject.transform.position = position;
                playerObject.transform.rotation = Quaternion.Euler(0, eventData.RotationY, 0);
            }
        }

        public void OnPlayersMovedBatch(PlayerMovedEvent[] eventData)
        {
            // 批量更新玩家位置
            foreach (var evt in eventData)
            {
                OnPlayerMoved(evt);
            }
        }

        private GameObject GetPlayerObject(Guid playerId)
        {
            // 查找玩家游戏对象...
            return null;
        }
    }
}
