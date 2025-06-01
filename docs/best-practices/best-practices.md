# PulseRPC 最佳实践

本文档基于 ChatApp 示例项目，提供使用 PulseRPC 开发项目的建议和最佳实践。

## 项目结构

推荐的项目结构（基于 ChatApp 示例）：

```
ChatApp/
├── ChatApp.Shared/          # 共享项目 (.NET Standard)
│   ├── IChatHub.cs          # Hub接口定义
│   ├── IPlayerHub.cs        # 游戏服务接口
│   ├── package.json         # Unity包配置
│   └── ChatApp.Shared.Unity.asmdef  # Unity程序集定义
├── ChatApp.Server/          # 服务器项目 (.NET 8+)
│   ├── Program.cs           # 服务器启动配置
│   ├── PlayerHub.cs         # Hub实现
│   ├── PlayerManager.cs     # 玩家管理器
│   └── GameWorld.cs         # 游戏世界
├── ChatApp.Console/         # 控制台客户端
│   ├── Program.cs           # 客户端入口
│   └── GameConsoleClient.cs # 客户端逻辑
└── ChatApp.Unity/           # Unity客户端
    └── Assets/Scripts/
        ├── ChatComponent.cs      # Unity聊天组件
        ├── ChatSceneSetup.cs     # UI自动设置
        └── SimplePlayerController.cs # 角色控制器
```

### 1. 共享项目配置

共享项目包含服务接口定义和数据模型，需要同时被服务器和客户端引用。

**关键配置文件：**

```json
// package.json - Unity包配置
{
  "name": "com.pulserpc.chatapp.shared",
  "version": "1.0.0",
  "displayName": "ChatApp Shared",
  "description": "PulseRPC ChatApp shared interfaces"
}
```

```xml
<!-- Directory.Build.props - 避免Unity导入编译产物 -->
<Project>
  <PropertyGroup>
    <ArtifactsPath>$(MSBuildThisFileDirectory).artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

## 接口设计模式

### Hub接口设计

PulseRPC 使用 Hub 模式实现双向通信，分为Hub接口（客户端调用）和Receiver接口（服务端推送）：

```csharp
/// <summary>
/// 聊天Hub - 客户端调用服务端
/// </summary>
[Channel("TcpChannel")]
public interface IChatHub : IPulseHub
{
    Task<bool> JoinAsync(JoinRequest request);
    Task<bool> LeaveAsync();
    Task<bool> SendMessageAsync(string message);
}

/// <summary>
/// 聊天接收器 - 服务端推送到客户端
/// </summary>
[Channel("TcpChannel")]
public interface IChatHubReceiver : IPulseReceiver
{
    void OnJoin(string name);
    void OnLeave(string name);
    void OnSendMessage(MessageResponse message);
    
    // 支持异步接收器方法
    Task<string> HelloAsync(string name, int age);
}
```

### 多通道设计

PulseRPC 支持多种传输通道，可根据数据特性选择合适的传输方式：

```csharp
/// <summary>
/// 玩家Hub - 默认使用TCP，特定方法使用KCP
/// </summary>
[Channel("TcpChannel")]  // 默认通道
public interface IPlayerHub : IPulseHub
{
    // 登录等重要操作使用TCP
    ValueTask<LoginResponse> LoginAsync(LoginRequest request);
    
    // 实时移动数据使用KCP低延迟通道
    [Channel("KcpChannel")]
    ValueTask MoveAsync(MoveRequest request);
    
    // 允许匿名访问的方法
    [AllowAnonymous]
    ValueTask<string> PingAsync(PingRequest request);
}

/// <summary>
/// 分离不同类型的事件到不同通道
/// </summary>
[Channel("TcpChannel")]
public interface IPlayerLoginEvents : IPulseReceiver
{
    void OnPlayerJoined(PlayerJoinedEvent eventData);
    void OnPlayerLeft(PlayerLeftEvent eventData);
}

[Channel("KcpChannel")]  // 高频位置更新使用KCP
public interface IPlayerMovementEvents : IPulseReceiver
{
    void OnPlayerMoved(PlayerMovedEvent eventData);
    void OnPlayersMovedBatch(PlayerMovedEvent[] eventData);
}
```

## 数据模型设计

### 使用 MemoryPack 进行序列化

```csharp
[MemoryPackable]
public partial class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class LoginResponse
{
    public bool Success { get; set; }
    public string Token { get; set; } = string.Empty;
    public PlayerInfo? Player { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

[MemoryPackable]
public partial struct JoinRequest  // 小型数据可使用struct
{
    [MemoryPackOrder(0)]
    public string RoomName { get; set; }
    
    [MemoryPackOrder(1)]
    public string UserName { get; set; }
}
```

### 事件数据设计

```csharp
// 统一事件接口
public interface IEventData { }

[MemoryPackable]
public partial class PlayerMovedEvent : IEventData
{
    public Guid PlayerId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float RotationY { get; set; }
    public bool IsRunning { get; set; }
}

// 批量事件处理
[MemoryPackable]
public partial class PlayersBatchMovedEvent : IEventData
{
    public PlayerMovedEvent[]? Updates { get; set; }
}
```

## 服务器端实现

### 服务器配置和启动

```csharp
// Program.cs
private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
{
    // 添加游戏业务服务
    services.AddSingleton<IGameWorld, GameWorld>();
    services.AddSingleton<IPlayerManager, PlayerManager>();

    // 添加PulseRPC核心服务
    services.AddPulseRpcServer();

    // 配置认证
    services.AddSingleton<IAuthenticationProvider, SimpleAuthenticationProvider>();

    // 注册Hub实现
    services.AddTransient<IPlayerHub, PlayerHub>();

    // 添加后台服务
    services.AddSingleton<PlayerMovementBatcher>();
    services.AddHostedService(sp => sp.GetRequiredService<PlayerMovementBatcher>());

    // 配置服务器管理器
    services.AddSingleton<IServerManager>(sp =>
    {
        var serverManager = new ServerManager(/* ... */);

        // 添加TCP传输 (可靠通信)
        serverManager.AddTransport(
            "TcpChannel",
            TransportType.Tcp,
            7000,
            new TransportOptions { NoDelay = true },
            true);

        // 添加KCP传输 (低延迟)
        serverManager.AddTransport(
            "KcpChannel",
            TransportType.Kcp,
            7001,
            new TransportOptions
            {
                Kcp = new KcpOptions
                {
                    NoDelay = 1,
                    Interval = 10,
                    Resend = 2
                }
            });

        return serverManager;
    });
}
```

### Hub 实现模式

```csharp
public class PlayerHub : IPlayerHub
{
    private readonly IGameWorld _gameWorld;
    private readonly IPlayerManager _playerManager;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAuthenticationProvider _authProvider;
    private readonly ILogger<PlayerHub> _logger;

    public PlayerHub(/* 依赖注入 */) { }

    /// <summary>
    /// 登录处理 - 展示认证和错误处理
    /// </summary>
    public async ValueTask<LoginResponse> LoginAsync(LoginRequest request)
    {
        _logger.LogInformation("玩家登录请求: {Username}", request.Username);

        try
        {
            // 使用认证提供程序验证
            var credentials = $"{request.Username}:{request.Password}";
            var authResult = await _authProvider.AuthenticateAsync(credentials);

            if (!authResult.IsAuthenticated)
            {
                return new LoginResponse
                {
                    Success = false,
                    ErrorMessage = authResult.ErrorMessage ?? "认证失败"
                };
            }

            // 获取玩家信息
            var player = await _playerManager.GetPlayerAsync(playerId);
            if (player == null)
            {
                return new LoginResponse
                {
                    Success = false,
                    ErrorMessage = "玩家不存在"
                };
            }

            // 设置连接认证信息
            var connection = RequestContext.Current;
            var channel = _channelManager.GetChannel(connection.ConnectionId);
            if (channel != null)
            {
                var authContext = new AuthenticationContext(connection.ConnectionId);
                authContext.SetClientAuthentication(
                    player.Id.ToString(), 
                    player.Username, 
                    token, 
                    authResult.User);
                channel.SetAuthentication(authContext);
            }

            // 通知其他玩家
            await NotifyPlayerJoinedAsync(player);

            return new LoginResponse
            {
                Success = true,
                Token = GenerateToken(player),
                Player = new PlayerInfo
                {
                    Id = player.Id,
                    Username = player.Username
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录过程异常: {Username}", request.Username);
            return new LoginResponse
            {
                Success = false,
                ErrorMessage = "服务器内部错误"
            };
        }
    }

    /// <summary>
    /// 移动处理 - 需要认证，使用KCP通道
    /// </summary>
    [Authorize]
    public async ValueTask MoveAsync(MoveRequest request)
    {
        var connection = RequestContext.Current;
        var channel = _channelManager.GetChannel(connection.ConnectionId);
        
        if (channel?.Authentication?.UserId == null)
        {
            throw new UnauthorizedAccessException("用户未认证");
        }

        var playerId = Guid.Parse(channel.Authentication.UserId);
        
        // 更新玩家位置
        var player = await _playerManager.GetPlayerAsync(playerId);
        if (player != null)
        {
            player.Position = new Vector3(request.X, request.Y, request.Z);
            
            // 批量处理移动事件以提高性能
            var moveEvent = new PlayerMovedEvent
            {
                PlayerId = playerId,
                X = request.X,
                Y = request.Y,
                Z = request.Z,
                RotationY = request.RotationY
            };
            
            _movementBatcher.AddMovementUpdate(moveEvent);
        }
    }

    /// <summary>
    /// 允许匿名访问的方法
    /// </summary>
    [AllowAnonymous]
    public async ValueTask<string> PingAsync(PingRequest request)
    {
        return $"Pong: {request.Message} at {DateTime.UtcNow:HH:mm:ss}";
    }
}
```

### 批量处理模式

```csharp
/// <summary>
/// 移动事件批处理器 - 提高高频事件的性能
/// </summary>
public class PlayerMovementBatcher : BackgroundService
{
    private readonly IEventPublisher _eventPublisher;
    private readonly List<PlayerMovedEvent> _pendingUpdates = new();
    private readonly object _syncLock = new();

    public void AddMovementUpdate(PlayerMovedEvent update)
    {
        lock (_syncLock)
        {
            // 移除同一玩家的旧更新
            _pendingUpdates.RemoveAll(x => x.PlayerId == update.PlayerId);
            _pendingUpdates.Add(update);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            PlayerMovedEvent[] updates;
            lock (_syncLock)
            {
                updates = _pendingUpdates.ToArray();
                _pendingUpdates.Clear();
            }

            if (updates.Length > 0)
            {
                // 批量发布事件
                var batchEvent = new PlayersBatchMovedEvent { Updates = updates };
                await _eventPublisher.PublishAsync<IPlayerMovementEvents>(
                    receiver => receiver.OnPlayersMovedBatch(batchEvent));
            }

            await Task.Delay(33, stoppingToken); // ~30 FPS
        }
    }
}
```

## 客户端实现

### .NET 控制台客户端

```csharp
[PulseClientGeneration(typeof(IPlayerHub))]  // 自动生成客户端代理
[PulseClientGeneration(typeof(IPlayerLoginEvents))]
[PulseClientGeneration(typeof(IPlayerMovementEvents))]
public class GameConsoleClient
{
    private IChannelManager? _channelManager;
    private IPlayerHub? _playerService;
    private ISubscriptionToken? _eventsSubscription;

    public async Task InitializeAsync()
    {
        // 创建通道管理器
        _channelManager = new ChannelManager(loggerFactory);

        // 注册TCP通道 - 可靠传输
        var tcpOptions = new TransportOptions 
        { 
            NoDelay = true, 
            KeepAlive = true, 
            AutoReconnect = true 
        };
        _channelManager.RegisterChannel("TcpChannel", TransportType.Tcp, tcpOptions, true);

        // 注册KCP通道 - 低延迟传输
        var kcpOptions = new TransportOptions
        {
            Kcp = new KcpOptions
            {
                NoDelay = 1,
                Interval = 10,
                Resend = 2,
                SendWindow = 32,
                ReceiveWindow = 128
            }
        };
        _channelManager.RegisterChannel("KcpChannel", TransportType.Kcp, kcpOptions);

        // 获取服务代理
        _playerService = _channelManager.GetPlayerHub();

        // 订阅事件
        var eventsHandler = _channelManager.GetPlayerLoginEventsHandler();
        _eventsSubscription = eventsHandler.Subscribe(new PlayerEventsHandler(this));
    }

    public async Task ConnectAsync(string host, int tcpPort, int kcpPort)
    {
        // 连接多个通道
        var tcpChannel = _channelManager!.GetChannel("TcpChannel");
        await tcpChannel.ConnectAsync(host, tcpPort);

        var kcpChannel = _channelManager.GetChannel("KcpChannel");
        await kcpChannel.ConnectAsync(host, kcpPort);
    }

    public async Task LoginAsync(string username, string password)
    {
        try
        {
            var request = new LoginRequest { Username = username, Password = password };
            var response = await _playerService!.LoginAsync(request);
            
            if (response.Success)
            {
                _isLoggedIn = true;
                _playerInfo = response.Player;
                _logger.LogInformation("登录成功: {Username}", _playerInfo!.Username);
            }
            else
            {
                _logger.LogWarning("登录失败: {ErrorMessage}", response.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录过程中发生错误");
            throw;
        }
    }

    // 事件处理器实现
    private class PlayerEventsHandler : IPlayerLoginEvents, IPlayerMovementEvents
    {
        private readonly GameConsoleClient _client;

        public PlayerEventsHandler(GameConsoleClient client)
        {
            _client = client;
        }

        public void OnPlayerJoined(PlayerJoinedEvent eventData)
        {
            _client.AddPlayer(eventData.PlayerId, eventData.PlayerName, 
                new Vector3(eventData.X, eventData.Y, eventData.Z));
        }

        public void OnPlayerMoved(PlayerMovedEvent eventData)
        {
            _client.UpdatePlayerPosition(eventData.PlayerId, 
                new Vector3(eventData.X, eventData.Y, eventData.Z));
        }

        public void OnPlayersMovedBatch(PlayersBatchMovedEvent eventData)
        {
            if (eventData.Updates != null)
            {
                foreach (var update in eventData.Updates)
                {
                    OnPlayerMoved(update);
                }
            }
        }
    }
}
```

### Unity 客户端

Unity 客户端需要特殊处理，包括UI集成和组件化设计：

```csharp
/// <summary>
/// Unity聊天组件 - 集成PulseRPC到Unity
/// </summary>
public class ChatComponent : MonoBehaviour
{
    [Header("网络配置")]
    [SerializeField] private string serverHost = "localhost";
    [SerializeField] private int tcpPort = 7000;
    [SerializeField] private int kcpPort = 7001;

    [Header("UI引用")]
    [SerializeField] private Text chatText;
    [SerializeField] private InputField messageInput;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button connectButton;

    private IChannelManager _channelManager;
    private IChatHub _chatService;
    private IPlayerHub _playerService;
    private ISubscriptionToken _chatEventsSubscription;
    private ISubscriptionToken _playerEventsSubscription;

    private async void Start()
    {
        await InitializeClientAsync();
        SetupUI();
    }

    private async Task InitializeClientAsync()
    {
        try
        {
            // 创建Unity专用的通道管理器
            _channelManager = new ChannelManager(
                CreateUnityLoggerFactory(), 
                useUnityMainThread: true);  // Unity主线程集成

            // 配置通道
            _channelManager.RegisterChannel("TcpChannel", TransportType.Tcp, 
                new TransportOptions { NoDelay = true, AutoReconnect = true }, true);

            // 获取服务代理
            _chatService = _channelManager.GetChatHub();
            _playerService = _channelManager.GetPlayerHub();

            // 订阅事件（自动切换到主线程）
            var chatHandler = _channelManager.GetChatHubReceiverHandler();
            _chatEventsSubscription = chatHandler.Subscribe(new ChatEventsHandler(this));

            var playerHandler = _channelManager.GetPlayerLoginEventsHandler();
            _playerEventsSubscription = playerHandler.Subscribe(new PlayerEventsHandler(this));

            Debug.Log("[ChatComponent] 客户端初始化完成");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChatComponent] 初始化失败: {ex.Message}");
            AppendChatMessage($"[错误] 初始化失败: {ex.Message}");
        }
    }

    private void SetupUI()
    {
        // UI事件绑定
        connectButton.onClick.AddListener(async () => await ConnectToServerAsync());
        sendButton.onClick.AddListener(async () => await SendMessageAsync());
        
        messageInput.onEndEdit.AddListener(async (text) =>
        {
            if (Input.GetKeyDown(KeyCode.Return))
                await SendMessageAsync();
        });
    }

    public async Task ConnectToServerAsync()
    {
        try
        {
            SetConnectionButtonState(false, "连接中...");

            // 连接到服务器
            var tcpChannel = _channelManager.GetChannel("TcpChannel");
            await tcpChannel.ConnectAsync(serverHost, tcpPort);

            // 自动登录
            var loginResponse = await _playerService.LoginAsync(new LoginRequest
            {
                Username = $"Player_{UnityEngine.Random.Range(1000, 9999)}",
                Password = "password"
            });

            if (loginResponse.Success)
            {
                AppendChatMessage("[系统] 连接并登录成功！");
                SetConnectionButtonState(false, "已连接");
            }
            else
            {
                AppendChatMessage($"[错误] 登录失败: {loginResponse.ErrorMessage}");
                SetConnectionButtonState(true, "重新连接");
            }
        }
        catch (Exception ex)
        {
            AppendChatMessage($"[错误] 连接失败: {ex.Message}");
            SetConnectionButtonState(true, "重新连接");
        }
    }

    // Unity主线程安全的消息更新
    private void AppendChatMessage(string message)
    {
        if (chatText != null)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            chatText.text += $"\n[{timestamp}] {message}";
            
            // 自动滚动到底部
            if (chatText.transform.parent.GetComponent<ScrollRect>() is ScrollRect scrollRect)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    // 事件处理器 - 自动在Unity主线程执行
    public class ChatEventsHandler : IChatHubReceiver
    {
        private readonly ChatComponent _component;

        public ChatEventsHandler(ChatComponent component)
        {
            _component = component;
        }

        public void OnJoin(string name)
        {
            _component.AppendChatMessage($"{name} 加入了聊天室");
        }

        public void OnSendMessage(MessageResponse message)
        {
            _component.AppendChatMessage($"{message.UserName}: {message.Message}");
        }

        public async Task<string> HelloAsync(string name, int age)
        {
            return $"Hello {name}, you are {age} years old!";
        }
    }
}
```

## 性能优化

### 1. 批量处理高频事件

```csharp
// 服务端：批量处理移动更新
public class PlayerMovementBatcher : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var updates = CollectPendingUpdates();
            if (updates.Length > 0)
            {
                await _eventPublisher.PublishAsync<IPlayerMovementEvents>(
                    receiver => receiver.OnPlayersMovedBatch(new PlayersBatchMovedEvent 
                    { 
                        Updates = updates 
                    }));
            }
            await Task.Delay(33, stoppingToken); // ~30 FPS
        }
    }
}
```

### 2. 连接池和重连策略

```csharp
// 客户端自动重连配置
var options = new TransportOptions 
{ 
    AutoReconnect = true,
    ReconnectInterval = TimeSpan.FromSeconds(5),
    MaxReconnectAttempts = 10,
    KeepAlive = true
};
```

### 3. 内存优化

```csharp
// 使用struct减少GC压力
[MemoryPackable]
public partial struct MoveRequest
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

// 对象池化（高频对象）
public class EventPool<T> where T : class, new()
{
    private readonly ConcurrentQueue<T> _pool = new();
    
    public T Rent() => _pool.TryDequeue(out var item) ? item : new T();
    public void Return(T item) => _pool.Enqueue(item);
}
```

## 错误处理和日志

### 统一错误处理

```csharp
// 服务端错误处理
public async ValueTask<LoginResponse> LoginAsync(LoginRequest request)
{
    try
    {
        // 业务逻辑
        return new LoginResponse { Success = true };
    }
    catch (AuthenticationException ex)
    {
        _logger.LogWarning("认证失败: {Username}, {Error}", request.Username, ex.Message);
        return new LoginResponse { Success = false, ErrorMessage = "用户名或密码错误" };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "登录异常: {Username}", request.Username);
        return new LoginResponse { Success = false, ErrorMessage = "服务器内部错误" };
    }
}

// 客户端错误处理
try
{
    var response = await _playerService.LoginAsync(request);
    if (!response.Success)
    {
        ShowError($"登录失败: {response.ErrorMessage}");
        return;
    }
    // 处理成功情况
}
catch (TimeoutException ex)
{
    ShowError("连接超时，请检查网络");
}
catch (ConnectionException ex)
{
    ShowError("连接断开，尝试重新连接...");
    await TryReconnectAsync();
}
```

### 结构化日志

```csharp
// 使用结构化日志提高可观测性
_logger.LogInformation("玩家移动: {PlayerId} 从 {OldPosition} 到 {NewPosition}", 
    playerId, oldPos, newPos);

_logger.LogWarning("玩家 {Username} 认证失败: {Reason}", 
    request.Username, "密码错误");

_logger.LogError(ex, "处理移动请求失败: {PlayerId}, {Request}", 
    playerId, JsonSerializer.Serialize(request));
```

## 安全性最佳实践

### 认证和授权

```csharp
// 服务端认证实现
public class SimpleAuthenticationProvider : IAuthenticationProvider
{
    public async Task<AuthenticationResult> AuthenticateAsync(string credentials)
    {
        var parts = credentials.Split(':');
        if (parts.Length != 2)
            return AuthenticationResult.Failed("无效凭证格式");

        var username = parts[0];
        var password = parts[1];

        // 验证用户凭证（实际项目中应使用数据库）
        if (await ValidateCredentialsAsync(username, password))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, username)
            };
            
            var identity = new ClaimsIdentity(claims, "PulseRPC");
            var principal = new ClaimsPrincipal(identity);
            
            return AuthenticationResult.Success(principal);
        }

        return AuthenticationResult.Failed("用户名或密码错误");
    }
}

// Hub方法权限控制
[Authorize]  // 需要认证
public async ValueTask MoveAsync(MoveRequest request) { }

[AllowAnonymous]  // 允许匿名访问
public async ValueTask<string> PingAsync(PingRequest request) { }

[Authorize(Roles = "Admin")]  // 角色权限
public async ValueTask<bool> KickPlayerAsync(Guid playerId) { }
```

## 测试策略

### 单元测试

```csharp
[Test]
public async Task LoginAsync_ValidCredentials_ReturnsSuccess()
{
    // Arrange
    var mockPlayerManager = new Mock<IPlayerManager>();
    var mockAuthProvider = new Mock<IAuthenticationProvider>();
    
    mockAuthProvider
        .Setup(x => x.AuthenticateAsync(It.IsAny<string>()))
        .ReturnsAsync(AuthenticationResult.Success(CreateTestUser()));

    var hub = new PlayerHub(mockPlayerManager.Object, mockAuthProvider.Object);

    // Act
    var result = await hub.LoginAsync(new LoginRequest 
    { 
        Username = "testuser", 
        Password = "password" 
    });

    // Assert
    Assert.IsTrue(result.Success);
    Assert.IsNotNull(result.Player);
}
```

### 集成测试

```csharp
[Test]
public async Task EndToEnd_LoginAndMove_Success()
{
    // 启动测试服务器
    using var server = new TestPulseRpcServer();
    server.AddService<IPlayerHub, PlayerHub>();
    await server.StartAsync();

    // 创建测试客户端
    using var client = new PulseRpcClient();
    await client.ConnectAsync("localhost", server.Port);

    var playerService = client.GetService<IPlayerHub>();

    // 测试登录
    var loginResult = await playerService.LoginAsync(new LoginRequest
    {
        Username = "testuser",
        Password = "password"
    });
    
    Assert.IsTrue(loginResult.Success);

    // 测试移动
    await playerService.MoveAsync(new MoveRequest { X = 10, Y = 0, Z = 5 });

    // 验证移动事件被触发
    // ...
}
```

## 部署和运维

### Docker 部署

```dockerfile
# ChatApp.Server/Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 7000 7001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ChatApp.Server/ChatApp.Server.csproj", "ChatApp.Server/"]
COPY ["ChatApp.Shared/ChatApp.Shared.csproj", "ChatApp.Shared/"]
RUN dotnet restore "ChatApp.Server/ChatApp.Server.csproj"

COPY . .
WORKDIR "/src/ChatApp.Server"
RUN dotnet build "ChatApp.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ChatApp.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ChatApp.Server.dll"]
```

### Kubernetes 配置

```yaml
# k8s/deployment.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: chatapp-server
spec:
  replicas: 3
  selector:
    matchLabels:
      app: chatapp-server
  template:
    metadata:
      labels:
        app: chatapp-server
    spec:
      containers:
      - name: chatapp-server
        image: chatapp-server:latest
        ports:
        - containerPort: 7000
          name: tcp
        - containerPort: 7001
          name: kcp
---
apiVersion: v1
kind: Service
metadata:
  name: chatapp-service
spec:
  selector:
    app: chatapp-server
  ports:
  - name: tcp
    port: 7000
    targetPort: 7000
  - name: kcp
    port: 7001
    targetPort: 7001
    protocol: UDP
  type: LoadBalancer
```

## 监控和诊断

### 性能指标收集

```csharp
// 添加性能计数器
services.AddSingleton<IMetricsCollector, MetricsCollector>();

public class PlayerHub : IPlayerHub
{
    private readonly IMetricsCollector _metrics;

    public async ValueTask<LoginResponse> LoginAsync(LoginRequest request)
    {
        using var timer = _metrics.StartTimer("player_login_duration");
        _metrics.IncrementCounter("player_login_attempts");

        try
        {
            var result = await ProcessLoginAsync(request);
            
            if (result.Success)
                _metrics.IncrementCounter("player_login_success");
            else
                _metrics.IncrementCounter("player_login_failure");

            return result;
        }
        catch (Exception ex)
        {
            _metrics.IncrementCounter("player_login_error");
            throw;
        }
    }
}
```

这份最佳实践文档基于实际的 ChatApp 示例项目，提供了完整的项目架构、代码示例和部署指南，涵盖了从接口设计到生产部署的所有重要方面。 
