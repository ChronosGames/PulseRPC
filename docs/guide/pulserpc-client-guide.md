# PulseRPC.Client 使用指南

## 概述

PulseRPC.Client 是一个高性能的 RPC 客户端库，提供了简洁易用的 API 来连接和调用远程服务。它支持多种传输协议（TCP/KCP）、智能连接管理、负载均衡和服务发现等企业级功能。

## 核心特性

- ⚡ **高性能**: 基于 MemoryPack 序列化和优化的网络传输
- 🔄 **智能连接**: 支持连接池、自动重连和故障切换
- 🎮 **游戏优化**: 专为游戏场景设计的连接策略和 API
- 🔒 **安全认证**: 内置认证机制和安全传输
- 📊 **监控友好**: 丰富的性能指标和日志支持
- 🌐 **多协议**: 支持 TCP、KCP 传输协议

## 快速开始

### 1. 安装包

```xml
<PackageReference Include="PulseRPC.Client" Version="1.0.0" />
```

### 2. 定义服务接口

```csharp
// 定义消息类型
[MemoryPackable]
public partial class UserInfo
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Email { get; set; } = string.Empty;
}

// 定义服务接口
public interface IUserService : IPulseHub
{
    Task<UserInfo> GetUserAsync(int userId);
    Task<bool> UpdateUserAsync(UserInfo user);
    Task<List<UserInfo>> GetAllUsersAsync();
}
```

### 3. 创建客户端

```csharp
using PulseRPC.Client;

// 基本客户端配置
var client = new PulseRPCClientBuilder()
    .ConfigureConnection("127.0.0.1", 8080)
    .ConfigureTransport(TransportType.Tcp)
    .Build();

// 连接到服务器
await client.ConnectAsync();

// 获取服务代理
var userService = client.GetService<IUserService>();

// 调用远程方法
var user = await userService.GetUserAsync(123);
Console.WriteLine($"User: {user.Name}, Age: {user.Age}");
```

## 详细配置

### 客户端构建器

PulseRPC.Client 使用建造者模式进行配置：

```csharp
var client = new PulseRPCClientBuilder()
    // 基本连接配置
    .ConfigureConnection("127.0.0.1", 8080)
    .ConfigureTransport(TransportType.Tcp)

    // 认证配置
    .ConfigureAuthentication(auth =>
    {
        auth.UseToken("your-auth-token");
        auth.SetAuthProvider<CustomAuthProvider>();
    })

    // 传输选项配置
    .ConfigureTransportOptions(options =>
    {
        options.ConnectTimeoutMs = 5000;
        options.ReadBufferSize = 8192;
        options.WriteBufferSize = 8192;
        options.EnableCompression = true;
    })

    // KCP 特定配置
    .ConfigureKcp(kcp =>
    {
        kcp.NoDelay = true;
        kcp.Interval = 10;
        kcp.SendWindow = 128;
        kcp.RecvWindow = 128;
    })

    // 序列化配置
    .ConfigureSerialization(serialization =>
    {
        serialization.UseMemoryPack();
        serialization.AddCustomSerializer<CustomType, CustomSerializer>();
    })

    // 日志配置
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })

    .Build();
```

### 连接策略

#### 基本连接
```csharp
// 简单连接
await client.ConnectAsync();

// 带超时的连接
await client.ConnectAsync(TimeSpan.FromSeconds(10));

// 指定连接参数
await client.ConnectAsync("192.168.1.100", 9090);
```

#### 高级连接配置
```csharp
var client = new PulseRPCClientBuilder()
    .ConfigureConnection(conn =>
    {
        conn.Host = "game-server.example.com";
        conn.Port = 8080;
        conn.Strategy = ConnectionStrategy.Session; // Session, Persistent, Transient
        conn.MaxRetries = 3;
        conn.RetryInterval = TimeSpan.FromSeconds(2);
        conn.EnableAutoReconnect = true;
    })
    .Build();
```

### 游戏客户端专用配置

PulseRPC.Client 为游戏场景提供了特殊的连接 API：

```csharp
// 连接到核心服务器
await client.ConnectToCoreServerAsync("core.game.com", 8080);

// 连接到战斗服务器
await client.ConnectToBattleServerAsync("battle.game.com", 8090);

// 连接到聊天服务器
await client.ConnectToChatServerAsync("chat.game.com", 8070);

// 多服务器配置
var gameClient = new PulseRPCClientBuilder()
    .ConfigureGameServers(servers =>
    {
        servers.AddCoreServer("core.game.com", 8080);
        servers.AddBattleServer("battle.game.com", 8090);
        servers.AddChatServer("chat.game.com", 8070);
        servers.EnableLoadBalancing();
    })
    .Build();
```

## 服务调用

### 基本服务调用

```csharp
// 获取服务代理
var userService = client.GetService<IUserService>();

// 同步调用（实际异步执行）
var user = await userService.GetUserAsync(123);

// 带取消令牌的调用
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var users = await userService.GetAllUsersAsync();

// 批量调用
var tasks = new[]
{
    userService.GetUserAsync(1),
    userService.GetUserAsync(2),
    userService.GetUserAsync(3)
};
var results = await Task.WhenAll(tasks);
```

### 错误处理

```csharp
try
{
    var user = await userService.GetUserAsync(999);
}
catch (PulseRPCException ex) when (ex.ErrorCode == "USER_NOT_FOUND")
{
    Console.WriteLine("用户不存在");
}
catch (TimeoutException)
{
    Console.WriteLine("请求超时");
}
catch (Exception ex)
{
    Console.WriteLine($"调用失败: {ex.Message}");
}
```

## 事件处理

### 服务端事件订阅

```csharp
// 定义事件处理器
public class UserEventHandler : IPulseEventHandler
{
    public async Task HandleAsync(string eventName, object eventData)
    {
        switch (eventName)
        {
            case "UserUpdated":
                var userInfo = (UserInfo)eventData;
                Console.WriteLine($"用户更新: {userInfo.Name}");
                break;

            case "UserDeleted":
                var userId = (int)eventData;
                Console.WriteLine($"用户删除: {userId}");
                break;
        }
    }
}

// 注册事件处理器
client.RegisterEventHandler<UserEventHandler>();

// 订阅特定事件
await client.SubscribeToEventAsync("UserUpdated");
await client.SubscribeToEventAsync("UserDeleted");
```

### 强类型事件处理

```csharp
// 定义强类型事件处理器
public class UserEventHandler : IEventHandler<UserInfo>
{
    public async Task HandleAsync(UserInfo user)
    {
        Console.WriteLine($"收到用户事件: {user.Name}");
        // 处理用户事件逻辑
    }
}

// 订阅强类型事件
client.Subscribe<UserInfo, UserEventHandler>();
```

## 连接池和重用

### 连接池配置

```csharp
var client = new PulseRPCClientBuilder()
    .ConfigureConnectionPool(pool =>
    {
        pool.MaxConnections = 10;
        pool.MinConnections = 2;
        pool.ConnectionIdleTimeout = TimeSpan.FromMinutes(5);
        pool.EnableConnectionSharing = true;
    })
    .Build();
```

### 多实例管理

```csharp
// 创建多个客户端实例
var clients = new Dictionary<string, IPulseRPCClient>();

// 核心服务客户端
clients["core"] = new PulseRPCClientBuilder()
    .ConfigureConnection("core.server.com", 8080)
    .Build();

// 数据库服务客户端
clients["database"] = new PulseRPCClientBuilder()
    .ConfigureConnection("db.server.com", 8090)
    .Build();

// 使用不同的客户端
var userService = clients["core"].GetService<IUserService>();
var dataService = clients["database"].GetService<IDataService>();
```

## 性能优化

### 序列化优化

```csharp
// 使用 MemoryPack 预编译
[MemoryPackable]
public partial class GameState
{
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public float Health { get; set; }

    // 使用 MemoryPackOrder 优化序列化顺序
    [MemoryPackOrder(0)]
    public int PlayerId { get; set; }

    [MemoryPackOrder(1)]
    public string PlayerName { get; set; } = string.Empty;
}

// 配置序列化选项
var client = new PulseRPCClientBuilder()
    .ConfigureSerialization(options =>
    {
        options.UseMemoryPack();
        options.EnableStringIntern = true; // 字符串内化
        options.EnableObjectPooling = true; // 对象池
    })
    .Build();
```

### 传输层优化

```csharp
// TCP 优化配置
.ConfigureTransportOptions(options =>
{
    options.ConnectTimeoutMs = 3000;
    options.ReadBufferSize = 16384;
    options.WriteBufferSize = 16384;
    options.EnableTcpNoDelay = true;
    options.EnableCompression = false; // 游戏场景通常关闭压缩
})

// KCP 低延迟配置
.ConfigureKcp(kcp =>
{
    kcp.NoDelay = true;
    kcp.Interval = 10;     // 10ms 更新间隔
    kcp.Resend = 2;        // 快速重传
    kcp.DisableFlowControl = false;
    kcp.SendWindow = 256;
    kcp.RecvWindow = 256;
})
```

### 批量操作

```csharp
// 批量调用优化
public async Task<List<UserInfo>> GetMultipleUsersOptimized(int[] userIds)
{
    // 使用批量接口而不是多次单独调用
    return await userService.GetUsersBatchAsync(userIds);
}

// 异步并发调用
public async Task<Dictionary<int, UserInfo>> GetMultipleUsersConcurrent(int[] userIds)
{
    var tasks = userIds.Select(async id => new { Id = id, User = await userService.GetUserAsync(id) });
    var results = await Task.WhenAll(tasks);
    return results.ToDictionary(r => r.Id, r => r.User);
}
```

## Unity 集成

### Unity 客户端配置

```csharp
using PulseRPC.Client.Unity;

public class GameNetworkManager : MonoBehaviour
{
    private IPulseRPCClient _client;

    async void Start()
    {
        // Unity 专用客户端构建
        _client = new UnityPulseRPCClientBuilder()
            .ConfigureConnection("game.server.com", 8080)
            .ConfigureTransport(TransportType.Kcp) // 游戏推荐 KCP
            .ConfigureUnity(unity =>
            {
                unity.EnableMainThreadDispatch = true; // UI 操作回到主线程
                unity.EnableAutoReconnect = true;
                unity.LogToUnityConsole = true;
            })
            .Build();

        await _client.ConnectAsync();
    }

    async void OnDestroy()
    {
        if (_client != null)
        {
            await _client.DisconnectAsync();
            _client.Dispose();
        }
    }
}
```

### Unity 事件处理

```csharp
public class PlayerController : MonoBehaviour, IEventHandler<PlayerMoveEvent>
{
    private IPlayerService _playerService;

    void Start()
    {
        _playerService = GameNetworkManager.Client.GetService<IPlayerService>();

        // 订阅玩家移动事件
        GameNetworkManager.Client.Subscribe<PlayerMoveEvent, PlayerController>();
    }

    // 处理服务端玩家移动事件
    public async Task HandleAsync(PlayerMoveEvent moveEvent)
    {
        // 在主线程更新 UI
        await UniTask.SwitchToMainThread();

        var player = FindPlayer(moveEvent.PlayerId);
        if (player != null)
        {
            player.transform.position = moveEvent.Position;
            player.transform.rotation = moveEvent.Rotation;
        }
    }

    // 发送玩家移动
    public async Task SendPlayerMove(Vector3 position, Quaternion rotation)
    {
        var moveEvent = new PlayerMoveEvent
        {
            PlayerId = GameState.CurrentPlayerId,
            Position = position,
            Rotation = rotation,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _playerService.UpdatePlayerPositionAsync(moveEvent);
    }
}
```

## 高级特性

### 自定义认证提供者

```csharp
public class GameAuthProvider : IAuthenticationProvider
{
    public async Task<AuthenticationResult> AuthenticateAsync(string token)
    {
        // 验证游戏 token
        var isValid = await ValidateGameTokenAsync(token);

        if (isValid)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, ExtractUserId(token)),
                new Claim(ClaimTypes.Name, ExtractUsername(token)),
                new Claim("GameLevel", ExtractGameLevel(token))
            };

            var identity = new ClaimsIdentity(claims, "game-auth");
            var principal = new ClaimsPrincipal(identity);

            return AuthenticationResult.Success(principal);
        }

        return AuthenticationResult.Failed("Invalid game token");
    }
}

// 注册自定义认证提供者
var client = new PulseRPCClientBuilder()
    .ConfigureAuthentication(auth =>
    {
        auth.SetAuthProvider<GameAuthProvider>();
        auth.UseToken(gameToken);
    })
    .Build();
```

### 请求拦截器

```csharp
public class LoggingInterceptor : IPulseClientInterceptor
{
    public async Task<T> InterceptAsync<T>(string serviceName, string methodName, object[] args, Func<Task<T>> next)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Console.WriteLine($"调用开始: {serviceName}.{methodName}");
            var result = await next();
            Console.WriteLine($"调用成功: {serviceName}.{methodName}, 耗时: {stopwatch.ElapsedMilliseconds}ms");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"调用失败: {serviceName}.{methodName}, 耗时: {stopwatch.ElapsedMilliseconds}ms, 错误: {ex.Message}");
            throw;
        }
    }
}

// 注册拦截器
var client = new PulseRPCClientBuilder()
    .AddInterceptor<LoggingInterceptor>()
    .Build();
```

### 健康检查

```csharp
// 配置健康检查
var client = new PulseRPCClientBuilder()
    .ConfigureHealthCheck(health =>
    {
        health.EnableHealthCheck = true;
        health.HealthCheckInterval = TimeSpan.FromSeconds(30);
        health.HealthCheckTimeout = TimeSpan.FromSeconds(5);
        health.OnHealthChanged = (isHealthy) =>
        {
            Console.WriteLine($"服务器健康状态: {(isHealthy ? "正常" : "异常")}");
        };
    })
    .Build();

// 手动健康检查
var isHealthy = await client.CheckHealthAsync();
```

## 最佳实践

### 1. 连接管理

```csharp
// ✅ 推荐：使用 using 语句或适当的生命周期管理
using var client = new PulseRPCClientBuilder()
    .ConfigureConnection("server.com", 8080)
    .Build();

// ✅ 推荐：在应用程序生命周期内重用客户端
public class GameService
{
    private static readonly IPulseRPCClient _sharedClient = CreateClient();

    public async Task<UserInfo> GetUserAsync(int userId)
    {
        var service = _sharedClient.GetService<IUserService>();
        return await service.GetUserAsync(userId);
    }
}
```

### 2. 错误处理

```csharp
// ✅ 推荐：具体的错误处理
try
{
    var result = await service.CallAsync();
}
catch (PulseRPCTimeoutException)
{
    // 处理超时
    return GetCachedResult();
}
catch (PulseRPCConnectionException)
{
    // 处理连接错误
    await TryReconnectAsync();
}
catch (PulseRPCException ex)
{
    // 处理业务逻辑错误
    LogError($"业务错误: {ex.ErrorCode} - {ex.Message}");
}
```

### 3. 性能优化

```csharp
// ✅ 推荐：重用服务代理
private readonly IUserService _userService;

public GameController(IPulseRPCClient client)
{
    _userService = client.GetService<IUserService>(); // 重用代理
}

// ✅ 推荐：合理使用取消令牌
public async Task<List<UserInfo>> LoadUsersAsync(CancellationToken cancellationToken = default)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(30)); // 设置超时

    return await _userService.GetAllUsersAsync(cts.Token);
}
```

## 故障排除

### 常见问题

#### 1. 连接失败
```csharp
// 问题：连接超时
// 解决：增加超时时间或检查网络
var client = new PulseRPCClientBuilder()
    .ConfigureConnection("server.com", 8080)
    .ConfigureTransportOptions(options =>
    {
        options.ConnectTimeoutMs = 10000; // 增加到 10 秒
    })
    .Build();
```

#### 2. 序列化错误
```csharp
// 问题：序列化失败
// 解决：确保消息类型正确标记
[MemoryPackable]
public partial class MyMessage // 必须是 partial class
{
    public string Name { get; set; } = string.Empty; // 确保有默认值
}
```

#### 3. 性能问题
```csharp
// 问题：调用延迟高
// 解决：启用性能监控
var client = new PulseRPCClientBuilder()
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Debug); // 启用详细日志
    })
    .EnablePerformanceCounters() // 启用性能计数器
    .Build();
```

### 调试技巧

```csharp
// 启用详细日志
var client = new PulseRPCClientBuilder()
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
        logging.AddFilter("PulseRPC", LogLevel.Trace); // 最详细的日志
    })
    .Build();

// 监控连接状态
client.ConnectionStateChanged += (sender, args) =>
{
    Console.WriteLine($"连接状态变更: {args.PreviousState} -> {args.CurrentState}");
    if (args.Exception != null)
    {
        Console.WriteLine($"连接异常: {args.Exception.Message}");
    }
};

// 监控性能指标
client.PerformanceCounters.OnMetricsUpdated += (metrics) =>
{
    Console.WriteLine($"RPS: {metrics.RequestsPerSecond}, 平均延迟: {metrics.AverageLatency}ms");
};
```

## 示例项目

完整的示例项目可以在 `samples/` 目录中找到：

- **ChatApp.Client** - 聊天应用客户端示例
- **GameClient.Unity** - Unity 游戏客户端示例
- **ConsoleClient** - 控制台客户端示例

这些示例展示了 PulseRPC.Client 在不同场景下的使用方法和最佳实践。