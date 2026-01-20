# PulseRPC 客户端和服务端使用指南

本指南提供了 PulseRPC 框架的完整使用说明，包含客户端和服务端的配置、API 使用、最佳实践等内容。

## 目录

1. [项目概述](#项目概述)
2. [核心概念](#核心概念)
3. [服务端开发](#服务端开发)
4. [客户端开发](#客户端开发)
5. [传输层配置](#传输层配置)
6. [认证和中间件](#认证和中间件)
7. [序列化配置](#序列化配置)
8. [性能优化](#性能优化)
9. [最佳实践](#最佳实践)
10. [示例代码](#示例代码)

## 项目概述

PulseRPC 是一个基于 .NET 的高性能 RPC 框架，专为 Unity 和服务端应用设计。主要特性包括：

- **多传输协议支持**：TCP 和 KCP 传输协议
- **高性能架构**：基于 System.IO.Pipelines 的零拷贝设计
- **灵活的序列化**：基于 MemoryPack 的高效序列化
- **服务发现**：支持 Consul、Etcd、Kubernetes
- **负载均衡**：多种负载均衡策略
- **Unity 支持**：专为 Unity 客户端优化

### 核心项目结构

```
src/
├── PulseRPC.Abstractions/     # 核心抽象接口和基础类型
├── PulseRPC.Client/           # 客户端实现
├── PulseRPC.Server/           # 服务端实现
├── PulseRPC.Client.Unity/     # Unity 客户端支持
├── PulseRPC.Infrastructure/   # 基础设施（服务发现、负载均衡）
└── PulseRPC.Shared/          # 共享组件
```

## 核心概念

### 服务接口定义

所有 RPC 服务必须继承 `IPulseHub` 接口：

```csharp
/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService : IPulseHub
{
    Task<User> GetUserAsync(int userId);
    Task<bool> CreateUserAsync(string username, string email);
    Task<List<User>> GetUsersAsync(int pageSize, int pageIndex);
}
```

### 消息类型定义

使用 MemoryPack 进行序列化，需要标记 `[MemoryPackable]`：

```csharp
[MemoryPackable]
public partial struct User
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }

    [MemoryPackOrder(1)]
    public string Username { get; set; }

    [MemoryPackOrder(2)]
    public string Email { get; set; }
}
```

### 事件处理接口

客户端接收服务端推送事件需要实现 `IPulseEventHandler`：

```csharp
public interface IChatEventHandler : IPulseEventHandler
{
    void OnUserJoined(string username);
    void OnMessageReceived(ChatMessage message);
    Task<string> OnPingAsync(string message);
}
```

## 服务端开发

### 基本服务端设置

1. **配置服务端**

```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);

// 配置 PulseRPC 服务端
builder.Services.AddPulseRpcServer(serverBuilder =>
{
    serverBuilder
        // 基本配置
        .ConfigureServer(options =>
        {
            options.AppName = "MyGameServer";
            options.AppVersion = "1.0.0";
            options.MaxConnections = 1000;
            options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        })

        // 添加传输层
        .AddTcp("TcpChannel", 7000, options =>
        {
            options.NoDelay = true;
            options.KeepAlive = true;
        }, isDefault: true)

        .AddKcp("KcpChannel", 7001, options =>
        {
            options.NoDelay = true;
            options.Interval = 10;
            options.Resend = 2;
        })

        // 性能优化
        .UseHighPerformanceEngine()
        .UseTieredMessageProcessor()
        .UsePriorityScheduler()

        // 注册服务
        .AddService<IUserService, UserService>()
        .AddService<IChatHub, ChatHub>();
});

var host = builder.Build();
var server = host.Services.GetRequiredService<IPulseRPCServer>();

// 启动服务器
await server.StartAsync();
Console.WriteLine("服务器已启动，按任意键停止...");
Console.ReadKey();
await server.StopAsync();
```

2. **实现服务接口**

```csharp
public class UserService : IUserService
{
    private readonly ILogger<UserService> _logger;
    private readonly IUserRepository _userRepository;

    public UserService(ILogger<UserService> logger, IUserRepository userRepository)
    {
        _logger = logger;
        _userRepository = userRepository;
    }

    public async Task<User> GetUserAsync(int userId)
    {
        _logger.LogInformation("获取用户信息: {UserId}", userId);
        return await _userRepository.GetByIdAsync(userId);
    }

    public async Task<bool> CreateUserAsync(string username, string email)
    {
        _logger.LogInformation("创建用户: {Username}, {Email}", username, email);
        var user = new User { Username = username, Email = email };
        return await _userRepository.CreateAsync(user);
    }

    public async Task<List<User>> GetUsersAsync(int pageSize, int pageIndex)
    {
        return await _userRepository.GetPagedAsync(pageSize, pageIndex);
    }
}
```

### 高级服务端配置

1. **启用认证**

```csharp
builder.Services.AddPulseRpcServer(serverBuilder =>
{
    serverBuilder
        .UseAuthentication(options =>
        {
            options.Enabled = true;
            options.JwtSecretKey = "your-secret-key";
            options.JwtExpiration = TimeSpan.FromHours(24);
        })
        .UseAuthorization(options =>
        {
            options.Enabled = true;
            options.SupportedRoles.AddRange(new[] { "Admin", "User", "Guest" });
        });
});

// 自定义认证提供者
builder.Services.AddSingleton<IAuthenticationProvider, CustomAuthenticationProvider>();
```

2. **添加中间件和拦截器**

```csharp
public class LoggingMiddleware : IPulseRpcMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(IPulseRpcContext context, Func<Task> next)
    {
        _logger.LogInformation("请求开始: {Service}.{Method}",
            context.ServiceName, context.MethodName);

        var stopwatch = Stopwatch.StartNew();
        await next();
        stopwatch.Stop();

        _logger.LogInformation("请求完成: {Service}.{Method}, 耗时: {ElapsedMs}ms",
            context.ServiceName, context.MethodName, stopwatch.ElapsedMilliseconds);
    }
}

// 注册中间件
serverBuilder.UseMiddleware<LoggingMiddleware>();
```

3. **性能监控**

```csharp
// 获取服务器统计信息
var metrics = server.GetPerformanceMetrics();
Console.WriteLine($"活动连接数: {metrics.ActiveConnections}");
Console.WriteLine($"处理消息总数: {metrics.TotalMessagesProcessed}");
Console.WriteLine($"平均延迟: {metrics.AverageLatencyMs}ms");
Console.WriteLine($"吞吐量: {metrics.ThroughputMsgsPerSec} msg/s");
```

## 客户端开发

### 基本客户端设置

1. **创建客户端**

```csharp
// 创建客户端构建器
var clientBuilder = new PulseRPCClientBuilder();

// 配置连接
var client = clientBuilder
    .AddTcpConnection("main", "MainServer", "localhost", 7000)
    .AddKcpConnection("battle", "BattleServer", "localhost", 7001)
    .WithLoadBalancing(LoadBalancingStrategy.RoundRobin)
    .WithLogging(loggerFactory)
    .Build();

// 初始化客户端
await client.InitializeAsync();
```

2. **使用服务代理**

```csharp
// 获取服务代理
var userService = await client.GetServiceAsync<IUserService>();

// 调用远程方法
var user = await userService.GetUserAsync(123);
var users = await userService.GetUsersAsync(10, 0);
var created = await userService.CreateUserAsync("alice", "alice@example.com");
```

3. **连接管理**

```csharp
// 连接到特定服务
var connectionConfig = new ConnectionConfig
{
    Name = "game-server",
    Host = "localhost",
    Port = 8000,
    Transport = TransportType.Tcp,
    Lifetime = ConnectionLifetime.Session,
    AutoReconnect = true,
    Tags = { ["type"] = "game", ["region"] = "us-west" }
};

var connection = await client.Connections.ConnectAsync(connectionConfig);

// 获取特定连接的服务代理
var gameService = await client.GetServiceAsync<IGameService>(connection.Id);
```

### 游戏客户端特殊用法

PulseRPC 为游戏开发提供了便捷的扩展方法：

```csharp
// 连接到核心游戏服务器
var coreConnection = await client.ConnectToCoreServerAsync("game-world-service");

// 连接到战斗服务器
var battleConnection = await client.ConnectToBattleServerAsync(
    battleId: "battle_123",
    host: "battle-server.example.com",
    port: 9000);

// 连接到副本服务器
var instanceConnection = await client.ConnectToInstanceServerAsync(
    instanceId: "dungeon_456",
    host: "instance-server.example.com",
    port: 9100);

// 切换地图服务器
var newMapConnection = await client.SwitchMapAsync(
    oldMapId: "map_1",
    newMapId: "map_2",
    serviceName: "map-server-2");

// 使用临时连接执行操作
await client.WithTemporaryConnectionAsync(tempConfig, async connection =>
{
    var tempService = await client.GetServiceAsync<ITempService>(connection.Id);
    await tempService.DoSomethingAsync();
});
```

### 事件处理

1. **注册事件监听器**

```csharp
public class ChatEventHandler : IChatEventHandler
{
    public void OnUserJoined(string username)
    {
        Console.WriteLine($"用户 {username} 加入了聊天室");
    }

    public void OnMessageReceived(ChatMessage message)
    {
        Console.WriteLine($"{message.Username}: {message.Content}");
    }

    public async Task<string> OnPingAsync(string message)
    {
        return $"Pong: {message}";
    }
}

// 注册事件监听器
var eventHandler = new ChatEventHandler();
var subscription = await client.RegisterEventListenerAsync(eventHandler);

// 取消订阅
await subscription.DisposeAsync();
```

### 高级客户端配置

1. **服务发现客户端**

```csharp
var clientBuilder = new PulseRPCClientBuilder()
    .WithServiceDiscovery(new ConsulServiceDiscovery(consulOptions))
    .AddServiceConnection("user-service", "UserService", TransportType.Tcp)
    .AddServiceConnection("game-service", "GameService", TransportType.Kcp);

var client = clientBuilder.Build();
await client.InitializeAsync();

// 通过服务发现连接
var connection = await client.ConnectToServiceAsync("UserService");
```

2. **连接池配置**

```csharp
var poolOptions = new ConnectionPoolOptions
{
    MaxConnections = 100,
    MinConnections = 5,
    IdleTimeout = TimeSpan.FromMinutes(5),
    EnableHealthCheck = true,
    HealthCheckInterval = TimeSpan.FromSeconds(30)
};

var client = clientBuilder
    .WithConnectionPooling(poolOptions)
    .Build();
```

3. **重试策略**

```csharp
var retryPolicy = new RetryPolicy
{
    MaxAttempts = 3,
    InitialDelay = TimeSpan.FromMilliseconds(100),
    MaxDelay = TimeSpan.FromSeconds(5),
    BackoffStrategy = BackoffStrategy.Exponential
};

var client = clientBuilder
    .WithRetryPolicy(retryPolicy)
    .Build();
```

## 传输层配置

### TCP 传输配置

```csharp
serverBuilder.AddTcp("TcpChannel", 7000, options =>
{
    options.NoDelay = true;              // 禁用 Nagle 算法
    options.KeepAlive = true;            // 启用保活
    options.KeepAliveInterval = 30000;   // 保活间隔 30 秒
    options.ConnectTimeout = TimeSpan.FromSeconds(10);
    options.RecvBufferSize = 8192;       // 接收缓冲区大小
    options.SendBufferSize = 8192;       // 发送缓冲区大小
    options.EnableLinger = false;        // 禁用 Linger
});
```

### KCP 传输配置

```csharp
serverBuilder.AddKcp("KcpChannel", 7001, options =>
{
    options.NoDelay = true;              // 无延迟模式
    options.Interval = 10;               // 内部更新间隔 10ms
    options.Resend = 2;                  // 快速重传门限
    options.DisableFlowControl = true;   // 关闭拥塞控制
    options.SendWindow = 32;             // 发送窗口大小
    options.RecvWindow = 128;            // 接收窗口大小
    options.ConversationId = 1;          // 会话ID
});
```

### 传输选项比较

| 特性 | TCP | KCP |
|------|-----|-----|
| 可靠性 | 高 | 高 |
| 延迟 | 中等 | 低 |
| 带宽利用率 | 高 | 中等 |
| CPU 消耗 | 低 | 中等 |
| 适用场景 | 一般游戏通信 | 实时对战、FPS |

## 认证和中间件

### 自定义认证提供者

```csharp
public class JwtAuthenticationProvider : IAuthenticationProvider
{
    private readonly IJwtService _jwtService;
    private readonly IUserService _userService;

    public JwtAuthenticationProvider(IJwtService jwtService, IUserService userService)
    {
        _jwtService = jwtService;
        _userService = userService;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string credentials)
    {
        try
        {
            // 解析 JWT Token
            var principal = _jwtService.ValidateToken(credentials);
            if (principal == null)
            {
                return AuthenticationResult.Fail("无效的访问令牌");
            }

            // 获取用户信息
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var user = await _userService.GetUserByIdAsync(userId);

            if (user == null)
            {
                return AuthenticationResult.Fail("用户不存在");
            }

            return AuthenticationResult.Success(principal);
        }
        catch (Exception ex)
        {
            return AuthenticationResult.Fail($"认证失败: {ex.Message}");
        }
    }
}
```

### 性能监控中间件

```csharp
public class PerformanceMiddleware : IPulseRpcMiddleware
{
    private readonly IMetricsCollector _metrics;

    public PerformanceMiddleware(IMetricsCollector metrics)
    {
        _metrics = metrics;
    }

    public async Task InvokeAsync(IPulseRpcContext context, Func<Task> next)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestSize = GetRequestSize(context.RequestData);

        try
        {
            await next();
            stopwatch.Stop();

            var responseSize = GetResponseSize(context.ResponseData);

            _metrics.RecordRequest(new RequestMetrics
            {
                ServiceName = context.ServiceName,
                MethodName = context.MethodName,
                Duration = stopwatch.Elapsed,
                RequestSize = requestSize,
                ResponseSize = responseSize,
                Success = true
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordRequest(new RequestMetrics
            {
                ServiceName = context.ServiceName,
                MethodName = context.MethodName,
                Duration = stopwatch.Elapsed,
                RequestSize = requestSize,
                Success = false,
                Error = ex.Message
            });
            throw;
        }
    }
}
```

### 请求拦截器

```csharp
public class SecurityInterceptor : IPulseRpcInterceptor
{
    public async Task OnRequestAsync(IPulseRpcContext context)
    {
        // 请求前安全检查
        if (IsRateLimited(context))
        {
            throw new RateLimitExceededException("请求频率超限");
        }

        // 记录请求
        context.Items["RequestStartTime"] = DateTime.UtcNow;
    }

    public async Task OnResponseAsync(IPulseRpcContext context)
    {
        // 响应后清理
        var startTime = (DateTime)context.Items["RequestStartTime"];
        var duration = DateTime.UtcNow - startTime;

        // 记录慢查询
        if (duration > TimeSpan.FromSeconds(1))
        {
            LogSlowRequest(context, duration);
        }
    }

    public async Task OnExceptionAsync(IPulseRpcContext context, Exception exception)
    {
        // 异常处理和记录
        LogException(context, exception);
    }
}
```

## 序列化配置

### 默认 MemoryPack 序列化器

```csharp
// 服务端配置
serverBuilder.WithSerializer(PulseRPCSerializerProvider.Instance);

// 客户端配置
var client = clientBuilder
    .WithSerializer(PulseRPCSerializerProvider.Instance)
    .Build();
```

### 自定义序列化器选项

```csharp
var serializerOptions = MemoryPackSerializerOptions.Default with
{
    StringEncoding = StringEncoding.Utf8,
    UseCompression = true
};

var customSerializer = PulseRPCSerializerProvider.Instance.WithOptions(serializerOptions);

// 使用自定义序列化器
serverBuilder.WithSerializer(customSerializer);
```

### 自定义序列化器实现

```csharp
public class JsonSerializerProvider : ISerializerProvider
{
    private readonly JsonSerializerOptions _options;

    public JsonSerializerProvider(JsonSerializerOptions options)
    {
        _options = options;
    }

    public ISerializer Create(MethodType methodType, MethodInfo? methodInfo)
    {
        return new JsonSerializer(_options);
    }
}

public class JsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    public void Serialize<T>(IBufferWriter<byte> writer, in T value)
    {
        JsonSerializer.Serialize(writer, value, _options);
    }

    public T Deserialize<T>(in ReadOnlySequence<byte> bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes, _options)!;
    }
}
```

## 性能优化

### 服务端性能优化

1. **启用高性能引擎**

```csharp
serverBuilder
    .UseHighPerformanceEngine(options =>
    {
        options.L1BufferSize = 8192;      // L1 循环缓冲区
        options.L2QueueCapacity = 512;    // L2 批处理队列
        options.L3QueueCapacity = 256;    // L3 响应队列
    })
    .UseTieredMessageProcessor(options =>
    {
        options.FastPath.MessageSizeThreshold = 1024;
        options.FastPath.DedicatedThreads = 4;
        options.BatchPath.BatchSize = 32;
    })
    .UsePriorityScheduler(options =>
    {
        options.CriticalWeight = 60;      // 关键消息权重
        options.NormalWeight = 30;        // 普通消息权重
        options.BulkWeight = 10;          // 批量消息权重
    });
```

2. **内存池配置**

```csharp
// 配置网络缓冲池
serverBuilder.ConfigureServer(options =>
{
    options.MaxConnections = 10000;
    options.BufferPoolSize = 1024 * 1024 * 100; // 100MB 缓冲池
    options.MaxMessageSize = 64 * 1024;          // 最大消息 64KB
});
```

### 客户端性能优化

1. **连接复用**

```csharp
var client = clientBuilder
    .WithConnectionPooling(new ConnectionPoolOptions
    {
        MaxConnections = 20,
        MinConnections = 2,
        IdleTimeout = TimeSpan.FromMinutes(5),
        EnableHealthCheck = true
    })
    .Build();
```

2. **批量操作**

```csharp
// 批量获取用户信息
var userIds = Enumerable.Range(1, 100).ToList();
var users = await userService.GetUsersBatchAsync(userIds);

// 并发调用（注意控制并发数量）
var tasks = userIds.Select(id => userService.GetUserAsync(id));
var results = await Task.WhenAll(tasks);
```

## 最佳实践

### 1. 服务接口设计

- **保持接口简洁**：每个服务专注于单一职责
- **使用异步方法**：所有服务方法都应该是异步的
- **合理的参数设计**：避免过多参数，使用复合对象
- **返回值设计**：统一错误处理和响应格式

```csharp
// ✅ 好的设计
public interface IUserService : IPulseHub
{
    Task<ApiResponse<User>> GetUserAsync(int userId);
    Task<ApiResponse<PagedList<User>>> GetUsersAsync(GetUsersRequest request);
    Task<ApiResponse<bool>> UpdateUserAsync(UpdateUserRequest request);
}

// ❌ 避免的设计
public interface IUserService : IPulseHub
{
    User GetUser(int id); // 非异步
    Task<List<User>> GetAllUsers(); // 可能返回大量数据
    Task<bool> UpdateUser(int id, string name, string email, int age, ...); // 参数过多
}
```

### 2. 错误处理

```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public string? Code { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Fail(string error, string? code = null) =>
        new() { Success = false, Error = error, Code = code };
}

// 服务实现中的错误处理
public async Task<ApiResponse<User>> GetUserAsync(int userId)
{
    try
    {
        if (userId <= 0)
            return ApiResponse<User>.Fail("无效的用户ID", "INVALID_USER_ID");

        var user = await _repository.GetByIdAsync(userId);
        if (user == null)
            return ApiResponse<User>.Fail("用户不存在", "USER_NOT_FOUND");

        return ApiResponse<User>.Ok(user);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "获取用户信息时发生错误: {UserId}", userId);
        return ApiResponse<User>.Fail("服务器内部错误", "INTERNAL_ERROR");
    }
}
```

### 3. 连接管理

```csharp
// 使用 using 语句确保资源释放
public async Task UseServiceAsync()
{
    using var client = clientBuilder.Build();
    await client.InitializeAsync();

    var userService = await client.GetServiceAsync<IUserService>();
    var result = await userService.GetUserAsync(123);

    // 客户端会在 using 语句结束时自动释放
}

// 长期运行的应用中，重用客户端实例
public class GameClient
{
    private readonly IPulseRPCClient _client;

    public GameClient(IPulseRPCClient client)
    {
        _client = client;
    }

    public async Task InitializeAsync()
    {
        await _client.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
```

### 4. 配置管理

```csharp
// 使用配置文件
public class GameServerOptions
{
    public string AppName { get; set; } = "GameServer";
    public int TcpPort { get; set; } = 7000;
    public int KcpPort { get; set; } = 7001;
    public int MaxConnections { get; set; } = 1000;
    public bool EnablePerformanceOptimization { get; set; } = true;
}

// 从配置绑定
var options = new GameServerOptions();
configuration.GetSection("GameServer").Bind(options);

serverBuilder.ConfigureServer(serverOptions =>
{
    serverOptions.AppName = options.AppName;
    serverOptions.MaxConnections = options.MaxConnections;
});
```

## 示例代码

### 完整的聊天应用示例

1. **共享接口定义**

```csharp
// IChatHub.cs
public interface IChatHub : IPulseHub
{
    Task<bool> JoinRoomAsync(JoinRoomRequest request);
    Task<bool> LeaveRoomAsync();
    Task<bool> SendMessageAsync(string message);
}

public interface IChatEventHandler : IPulseEventHandler
{
    void OnUserJoined(string username);
    void OnUserLeft(string username);
    void OnMessageReceived(ChatMessage message);
}

[MemoryPackable]
public partial struct JoinRoomRequest
{
    [MemoryPackOrder(0)]
    public string RoomName { get; set; }

    [MemoryPackOrder(1)]
    public string Username { get; set; }
}

[MemoryPackable]
public partial struct ChatMessage
{
    [MemoryPackOrder(0)]
    public string Username { get; set; }

    [MemoryPackOrder(1)]
    public string Message { get; set; }

    [MemoryPackOrder(2)]
    public DateTime Timestamp { get; set; }
}
```

2. **服务端实现**

```csharp
// ChatHub.cs
public class ChatHub : IChatHub
{
    private readonly IChatRoomManager _roomManager;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IChatRoomManager roomManager, ILogger<ChatHub> logger)
    {
        _roomManager = roomManager;
        _logger = logger;
    }

    public async Task<bool> JoinRoomAsync(JoinRoomRequest request)
    {
        try
        {
            await _roomManager.JoinRoomAsync(request.RoomName, request.Username);
            _logger.LogInformation("用户 {Username} 加入房间 {RoomName}",
                request.Username, request.RoomName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户加入房间失败");
            return false;
        }
    }

    public async Task<bool> LeaveRoomAsync()
    {
        // 实现离开房间逻辑
        return true;
    }

    public async Task<bool> SendMessageAsync(string message)
    {
        // 实现发送消息逻辑
        return true;
    }
}

// 服务端启动
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddPulseRpcServer(builder =>
        {
            builder
                .ConfigureServer(options =>
                {
                    options.AppName = "ChatServer";
                    options.MaxConnections = 1000;
                })
                .AddTcp("TcpChannel", 7000, isDefault: true)
                .AddService<IChatHub, ChatHub>()
                .UseAuthentication()
                .UseMiddleware<LoggingMiddleware>();
        });

        services.AddSingleton<IChatRoomManager, ChatRoomManager>();
    })
    .Build();

await host.RunAsync();
```

3. **客户端实现**

```csharp
// ChatClient.cs
public class ChatClient : IChatEventHandler, IAsyncDisposable
{
    private readonly IPulseRPCClient _client;
    private IChatHub? _chatService;
    private ISubscriptionToken? _subscription;

    public ChatClient()
    {
        var builder = new PulseRPCClientBuilder();
        _client = builder
            .AddTcpConnection("chat", "ChatServer", "localhost", 7000)
            .WithLogging(CreateLoggerFactory())
            .Build();
    }

    public async Task ConnectAsync()
    {
        await _client.InitializeAsync();
        _chatService = await _client.GetServiceAsync<IChatHub>();
        _subscription = await _client.RegisterEventListenerAsync<IChatEventHandler>(this);
    }

    public async Task JoinRoomAsync(string roomName, string username)
    {
        if (_chatService == null) throw new InvalidOperationException("未连接到服务器");

        var request = new JoinRoomRequest { RoomName = roomName, Username = username };
        var result = await _chatService.JoinRoomAsync(request);

        if (result)
            Console.WriteLine($"成功加入房间: {roomName}");
        else
            Console.WriteLine("加入房间失败");
    }

    public async Task SendMessageAsync(string message)
    {
        if (_chatService == null) throw new InvalidOperationException("未连接到服务器");

        await _chatService.SendMessageAsync(message);
    }

    // 事件处理
    public void OnUserJoined(string username)
    {
        Console.WriteLine($"🟢 {username} 加入了聊天室");
    }

    public void OnUserLeft(string username)
    {
        Console.WriteLine($"🔴 {username} 离开了聊天室");
    }

    public void OnMessageReceived(ChatMessage message)
    {
        Console.WriteLine($"[{message.Timestamp:HH:mm:ss}] {message.Username}: {message.Message}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription != null)
            await _subscription.DisposeAsync();

        if (_client != null)
            await _client.DisposeAsync();
    }
}

// 客户端使用
class Program
{
    static async Task Main(string[] args)
    {
        var client = new ChatClient();

        try
        {
            await client.ConnectAsync();
            await client.JoinRoomAsync("general", "Alice");

            // 发送消息
            await client.SendMessageAsync("Hello, everyone!");

            // 保持连接
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
```

### Unity 客户端示例

```csharp
// Unity 中的使用
public class GameNetworkManager : MonoBehaviour
{
    private IPulseRPCClient _client;
    private IGameService _gameService;

    async void Start()
    {
        var builder = new PulseRPCClientBuilder();
        _client = builder
            .AddGameServerSet("production")
            .WithBattleOptimizations()
            .WithConnectionPooling(maxConnections: 10)
            .Build();

        await _client.InitializeAsync();
        _gameService = await _client.GetServiceAsync<IGameService>();

        Debug.Log("Connected to game server");
    }

    public async Task<Player> GetPlayerDataAsync(string playerId)
    {
        try
        {
            var response = await _gameService.GetPlayerAsync(playerId);
            return response.Data;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to get player data: {ex.Message}");
            return null;
        }
    }

    async void OnDestroy()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }
    }
}
```

## 故障排除

### 常见问题和解决方案

1. **连接超时**
   - 检查网络连通性
   - 确认服务端端口正确监听
   - 调整连接超时时间

2. **序列化错误**
   - 确保消息类型标记了 `[MemoryPackable]`
   - 检查字段的 `[MemoryPackOrder]` 标记
   - 验证客户端和服务端使用相同的消息定义

3. **性能问题**
   - 启用高性能引擎配置
   - 检查是否有内存泄漏
   - 监控连接数和消息处理速度

4. **认证失败**
   - 验证认证提供者配置
   - 检查 JWT 密钥和过期时间
   - 确认客户端发送正确的认证信息

---

这份指南涵盖了 PulseRPC 框架的主要使用场景和最佳实践。如需更多详细信息，请参考项目源码和示例。
