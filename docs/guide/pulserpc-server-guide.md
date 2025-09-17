# PulseRPC.Server 使用指南

## 概述

PulseRPC.Server 是 PulseRPC 框架的服务端实现，提供高性能、可扩展的 RPC 服务托管能力。本指南将详细介绍如何配置、开发和部署基于 PulseRPC.Server 的应用程序。

## 快速开始

### 基本服务端配置

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PulseRPC.Server;
using PulseRPC.Abstractions;

// 创建主机构建器
var builder = Host.CreateApplicationBuilder(args);

// 配置 PulseRPC 服务端
builder.Services.AddPulseRPCServer(options =>
{
    // TCP 监听器配置
    options.ListenOn("127.0.0.1", 9090, TransportType.Tcp);

    // KCP 监听器配置（低延迟）
    options.ListenOn("127.0.0.1", 9091, TransportType.Kcp);

    // 基本配置
    options.MaxConcurrentConnections = 1000;
    options.ConnectionTimeout = TimeSpan.FromSeconds(30);
});

// 构建并运行主机
var host = builder.Build();
await host.RunAsync();
```

### 实现 RPC 服务

```csharp
using PulseRPC.Abstractions;
using MemoryPack;

// 定义服务接口
public interface IGameService : IPulseHub
{
    Task<PlayerInfo> GetPlayerAsync(int playerId);
    Task<bool> UpdatePlayerScoreAsync(int playerId, int score);
    Task BroadcastMessageAsync(string message);
}

// 实现服务
public class GameService : IGameService
{
    private readonly ILogger<GameService> _logger;
    private readonly IPlayerRepository _playerRepository;

    public GameService(ILogger<GameService> logger, IPlayerRepository playerRepository)
    {
        _logger = logger;
        _playerRepository = playerRepository;
    }

    public async Task<PlayerInfo> GetPlayerAsync(int playerId)
    {
        _logger.LogInformation("获取玩家信息: {PlayerId}", playerId);

        var player = await _playerRepository.GetByIdAsync(playerId);
        if (player == null)
        {
            throw new ArgumentException($"Player {playerId} not found");
        }

        return new PlayerInfo
        {
            Id = player.Id,
            Name = player.Name,
            Score = player.Score,
            Level = player.Level
        };
    }

    public async Task<bool> UpdatePlayerScoreAsync(int playerId, int score)
    {
        _logger.LogInformation("更新玩家分数: {PlayerId} -> {Score}", playerId, score);

        return await _playerRepository.UpdateScoreAsync(playerId, score);
    }

    public async Task BroadcastMessageAsync(string message)
    {
        _logger.LogInformation("广播消息: {Message}", message);

        // 通过会话管理器广播到所有连接的客户端
        var sessionManager = ServiceProvider.GetRequiredService<IClientSessionManager>();
        var broadcastData = MemoryPackSerializer.Serialize(new BroadcastMessage { Content = message });

        await sessionManager.BroadcastAsync(broadcastData);
    }
}

// 数据传输对象
[MemoryPackable]
public partial class PlayerInfo
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }

    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    [MemoryPackOrder(2)]
    public int Score { get; set; }

    [MemoryPackOrder(3)]
    public int Level { get; set; }
}

[MemoryPackable]
public partial class BroadcastMessage
{
    [MemoryPackOrder(0)]
    public string Content { get; set; } = string.Empty;

    [MemoryPackOrder(1)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

### 注册服务

```csharp
// 在 Program.cs 中注册服务
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();

// 注册 PulseRPC 服务端并配置服务
builder.Services.AddPulseRPCServer(options =>
{
    options.ListenOn("0.0.0.0", 9090, TransportType.Tcp);
    options.MaxConcurrentConnections = 2000;
})
.AddHub<IGameService>() // 注册 RPC 服务
.AddSerializer<MemoryPackSerializer>(); // 配置序列化器
```

## 高级配置

### 传输层配置

```csharp
builder.Services.AddPulseRPCServer(options =>
{
    // TCP 配置
    options.Tcp.NoDelay = true;
    options.Tcp.KeepAlive = true;
    options.Tcp.KeepAliveInterval = TimeSpan.FromSeconds(30);
    options.Tcp.ReceiveBufferSize = 64 * 1024;
    options.Tcp.SendBufferSize = 64 * 1024;

    // KCP 配置（适用于游戏等低延迟场景）
    options.Kcp.NoDelay = true;
    options.Kcp.Interval = 10; // 更新间隔（毫秒）
    options.Kcp.Resend = 2; // 快速重传参数
    options.Kcp.DisableFlowControl = false;
    options.Kcp.SendWindow = 128;
    options.Kcp.RecvWindow = 128;

    // 连接管理
    options.MaxConcurrentConnections = 5000;
    options.ConnectionTimeout = TimeSpan.FromSeconds(60);
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.MaxIdleTime = TimeSpan.FromMinutes(5);
});
```

### 性能优化配置

```csharp
builder.Services.AddPulseRPCServer(options =>
{
    // 消息处理配置
    options.MessageProcessing.MaxConcurrentMessages = Environment.ProcessorCount * 2;
    options.MessageProcessing.MessageQueueCapacity = 10000;
    options.MessageProcessing.ProcessingTimeout = TimeSpan.FromSeconds(30);

    // 响应处理配置
    options.ResponseProcessing.ProcessorThreadCount = Math.Max(1, Environment.ProcessorCount / 2);
    options.ResponseProcessing.ChannelCapacity = 10000;
    options.ResponseProcessing.ResponseTimeout = TimeSpan.FromSeconds(30);

    // 内存管理
    options.Memory.UseMemoryPool = true;
    options.Memory.MaxBufferSize = 1024 * 1024; // 1MB
    options.Memory.MaxRetainedBuffers = 1000;
})
.ConfigureMemoryManagement(memory =>
{
    memory.EnableBufferPooling = true;
    memory.MaxPoolSize = 100 * 1024 * 1024; // 100MB
    memory.PreallocateBuffers = true;
});
```

## 认证与安全

### 实现自定义认证

```csharp
public class TokenAuthenticationHandler : IAuthenticationHandler
{
    private readonly ITokenValidator _tokenValidator;
    private readonly ILogger<TokenAuthenticationHandler> _logger;

    public TokenAuthenticationHandler(
        ITokenValidator tokenValidator,
        ILogger<TokenAuthenticationHandler> logger)
    {
        _tokenValidator = tokenValidator;
        _logger = logger;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        ISessionChannel session,
        AuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = request.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                return AuthenticationResult.Failed("Token is required");
            }

            var claims = await _tokenValidator.ValidateAsync(token, cancellationToken);
            if (claims == null)
            {
                return AuthenticationResult.Failed("Invalid token");
            }

            var authContext = new AuthenticationContext
            {
                UserId = claims.GetUserId(),
                UserName = claims.GetUserName(),
                Roles = claims.GetRoles(),
                IsAuthenticated = true
            };

            session.SetAuthentication(authContext);

            _logger.LogInformation("用户认证成功: {UserId}", authContext.UserId);

            return AuthenticationResult.Success(authContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "认证过程中发生错误");
            return AuthenticationResult.Failed("Authentication failed");
        }
    }
}

// 注册认证处理器
builder.Services.AddScoped<IAuthenticationHandler, TokenAuthenticationHandler>();
builder.Services.AddScoped<ITokenValidator, JwtTokenValidator>();

builder.Services.AddPulseRPCServer(options =>
{
    options.Authentication.RequireAuthentication = true;
    options.Authentication.AuthenticationTimeout = TimeSpan.FromSeconds(10);
    options.Authentication.AllowAnonymousHubs = new[] { "IHealthService" };
});
```

### 授权配置

```csharp
public class RoleBasedAuthorizationHandler : IAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(
        ISessionChannel session,
        string serviceName,
        string methodName,
        CancellationToken cancellationToken = default)
    {
        var authContext = session.AuthenticationContext;
        if (authContext?.IsAuthenticated != true)
        {
            return Task.FromResult(false);
        }

        // 基于角色的授权逻辑
        return serviceName switch
        {
            "IAdminHub" => Task.FromResult(authContext.HasRole("Admin")),
            "IGameService" => Task.FromResult(authContext.HasRole("Player", "Admin")),
            _ => Task.FromResult(true)
        };
    }
}

// 注册授权处理器
builder.Services.AddScoped<IAuthorizationHandler, RoleBasedAuthorizationHandler>();
```

## 中间件和拦截器

### 实现请求拦截器

```csharp
public class LoggingInterceptor : IRequestInterceptor
{
    private readonly ILogger<LoggingInterceptor> _logger;

    public LoggingInterceptor(ILogger<LoggingInterceptor> logger)
    {
        _logger = logger;
    }

    public async Task<InterceptorResult> InterceptAsync(
        ISessionChannel session,
        CallContext callContext,
        Func<Task<object?>> next,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("开始处理请求: {Service}.{Method} from {ConnectionId}",
            callContext.ServiceName, callContext.MethodName, session.ConnectionId);

        try
        {
            var result = await next();
            stopwatch.Stop();

            _logger.LogInformation("请求处理完成: {Service}.{Method}, 耗时: {ElapsedMs}ms",
                callContext.ServiceName, callContext.MethodName, stopwatch.ElapsedMilliseconds);

            return InterceptorResult.Success(result);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "请求处理失败: {Service}.{Method}, 耗时: {ElapsedMs}ms",
                callContext.ServiceName, callContext.MethodName, stopwatch.ElapsedMilliseconds);

            return InterceptorResult.Failed(ex);
        }
    }
}

// 注册拦截器
builder.Services.AddScoped<IRequestInterceptor, LoggingInterceptor>();
builder.Services.AddScoped<IRequestInterceptor, MetricsInterceptor>();
builder.Services.AddScoped<IRequestInterceptor, ValidationInterceptor>();
```

### 限流中间件

```csharp
public class RateLimitingInterceptor : IRequestInterceptor
{
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<RateLimitingInterceptor> _logger;

    public RateLimitingInterceptor(
        IRateLimiter rateLimiter,
        ILogger<RateLimitingInterceptor> logger)
    {
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<InterceptorResult> InterceptAsync(
        ISessionChannel session,
        CallContext callContext,
        Func<Task<object?>> next,
        CancellationToken cancellationToken = default)
    {
        var key = $"{session.ConnectionId}:{callContext.ServiceName}.{callContext.MethodName}";

        if (!await _rateLimiter.TryAcquireAsync(key, cancellationToken))
        {
            _logger.LogWarning("请求被限流: {Key}", key);
            throw new InvalidOperationException("Request rate limit exceeded");
        }

        return await next();
    }
}
```

## Hub 实时通信

### 实现 Hub 服务

```csharp
public interface IGameHub : IPulseHub
{
    Task JoinRoomAsync(string roomId);
    Task LeaveRoomAsync(string roomId);
    Task SendMessageToRoomAsync(string roomId, string message);
}

public class GameHub : IPulseHub, IGameHub
{
    private readonly IRoomManager _roomManager;
    private readonly IClientSessionManager _sessionManager;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        IRoomManager roomManager,
        IClientSessionManager sessionManager,
        ILogger<GameHub> logger)
    {
        _roomManager = roomManager;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task JoinRoomAsync(string roomId)
    {
        var session = GetCurrentSession();
        await _roomManager.AddPlayerToRoomAsync(roomId, session.SessionId);

        _logger.LogInformation("玩家 {SessionId} 加入房间 {RoomId}", session.SessionId, roomId);

        // 通知房间内其他玩家
        await NotifyRoomPlayersAsync(roomId, new PlayerJoinedEvent
        {
            PlayerId = session.SessionId,
            RoomId = roomId
        }, excludeSessionId: session.SessionId);
    }

    public async Task LeaveRoomAsync(string roomId)
    {
        var session = GetCurrentSession();
        await _roomManager.RemovePlayerFromRoomAsync(roomId, session.SessionId);

        _logger.LogInformation("玩家 {SessionId} 离开房间 {RoomId}", session.SessionId, roomId);

        // 通知房间内其他玩家
        await NotifyRoomPlayersAsync(roomId, new PlayerLeftEvent
        {
            PlayerId = session.SessionId,
            RoomId = roomId
        });
    }

    public async Task SendMessageToRoomAsync(string roomId, string message)
    {
        var session = GetCurrentSession();

        _logger.LogInformation("玩家 {SessionId} 向房间 {RoomId} 发送消息", session.SessionId, roomId);

        await NotifyRoomPlayersAsync(roomId, new RoomMessageEvent
        {
            SenderId = session.SessionId,
            RoomId = roomId,
            Message = message,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task NotifyRoomPlayersAsync<T>(string roomId, T eventData, string? excludeSessionId = null)
    {
        var playerIds = await _roomManager.GetRoomPlayersAsync(roomId);
        var data = MemoryPackSerializer.Serialize(eventData);

        foreach (var playerId in playerIds)
        {
            if (playerId == excludeSessionId) continue;

            var session = _sessionManager.GetSession(playerId);
            if (session != null)
            {
                await session.SendAsync(data);
            }
        }
    }

    private IClientSession GetCurrentSession()
    {
        // 从当前上下文获取会话（需要框架支持）
        return Context.Session;
    }
}

// 注册 Hub
builder.Services.AddScoped<IGameHub, GameHub>();
builder.Services.AddPulseRPCServer(options => { /* 配置 */ })
    .AddHub<IGameHub>();
```

## 监控和诊断

### 健康检查

```csharp
public class PulseRPCHealthCheck : IHealthCheck
{
    private readonly IClientSessionManager _sessionManager;
    private readonly IServerListener[] _listeners;

    public PulseRPCHealthCheck(
        IClientSessionManager sessionManager,
        IEnumerable<IServerListener> listeners)
    {
        _sessionManager = sessionManager;
        _listeners = listeners.ToArray();
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["active_sessions"] = _sessionManager.ActiveSessionCount,
            ["listeners"] = _listeners.Select(l => new
            {
                name = l.Name,
                type = l.Type.ToString(),
                endpoint = l.LocalEndPoint?.ToString(),
                listening = l.IsListening
            }).ToArray()
        };

        var allListenersHealthy = _listeners.All(l => l.IsListening);

        return Task.FromResult(allListenersHealthy
            ? HealthCheckResult.Healthy("PulseRPC Server is healthy", data)
            : HealthCheckResult.Unhealthy("Some listeners are not running", data: data));
    }
}

// 注册健康检查
builder.Services.AddHealthChecks()
    .AddCheck<PulseRPCHealthCheck>("pulserpc");

// 配置健康检查端点
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### 性能指标

```csharp
public class PulseRPCMetrics
{
    private readonly IMeterFactory _meterFactory;
    private readonly Meter _meter;

    public Counter<long> RequestCount { get; }
    public Histogram<double> RequestDuration { get; }
    public UpDownCounter<int> ActiveConnections { get; }
    public Counter<long> ErrorCount { get; }

    public PulseRPCMetrics(IMeterFactory meterFactory)
    {
        _meterFactory = meterFactory;
        _meter = _meterFactory.Create("PulseRPC.Server");

        RequestCount = _meter.CreateCounter<long>("pulserpc_requests_total", description: "Total number of RPC requests");
        RequestDuration = _meter.CreateHistogram<double>("pulserpc_request_duration_seconds", description: "RPC request duration in seconds");
        ActiveConnections = _meter.CreateUpDownCounter<int>("pulserpc_connections_active", description: "Number of active connections");
        ErrorCount = _meter.CreateCounter<long>("pulserpc_errors_total", description: "Total number of errors");
    }
}

// 注册指标
builder.Services.AddSingleton<PulseRPCMetrics>();
builder.Services.AddOpenTelemetry()
    .WithMetrics(builder => builder.AddMeter("PulseRPC.Server"));
```

## 部署和运维

### Docker 部署

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 9090
EXPOSE 9091

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["GameServer.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GameServer.dll"]
```

```yaml
# docker-compose.yml
version: '3.8'
services:
  gameserver:
    build: .
    ports:
      - "9090:9090"  # TCP
      - "9091:9091"  # KCP
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__Database=Server=db;Database=GameDB;
    depends_on:
      - db
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3

  db:
    image: postgres:15
    environment:
      POSTGRES_DB: GameDB
      POSTGRES_PASSWORD: password
    volumes:
      - db_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"

volumes:
  db_data:
```

### 生产环境配置

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "PulseRPC": "Warning",
      "Microsoft": "Warning"
    }
  },
  "PulseRPC": {
    "Server": {
      "Listeners": [
        {
          "Type": "Tcp",
          "Address": "0.0.0.0",
          "Port": 9090
        },
        {
          "Type": "Kcp",
          "Address": "0.0.0.0",
          "Port": 9091
        }
      ],
      "MaxConcurrentConnections": 10000,
      "ConnectionTimeout": "00:01:00",
      "HeartbeatInterval": "00:00:30",
      "Performance": {
        "UseMemoryPool": true,
        "MaxBufferSize": 1048576,
        "ProcessorThreadCount": 4,
        "ChannelCapacity": 50000
      }
    }
  },
  "ConnectionStrings": {
    "Database": "Server=localhost;Database=GameDB;Integrated Security=true;",
    "Redis": "localhost:6379"
  }
}
```

## 故障排除

### 常见问题

1. **连接超时**
   ```csharp
   // 增加连接超时时间
   options.ConnectionTimeout = TimeSpan.FromMinutes(2);

   // 启用心跳检测
   options.HeartbeatInterval = TimeSpan.FromSeconds(30);
   ```

2. **序列化错误**
   ```csharp
   // 确保使用 MemoryPack 特性
   [MemoryPackable]
   public partial class MyData
   {
       [MemoryPackOrder(0)]
       public string Value { get; set; }
   }
   ```

3. **性能问题**
   ```csharp
   // 启用性能优化
   options.Memory.UseMemoryPool = true;
   options.ResponseProcessing.ProcessorThreadCount = Environment.ProcessorCount;

   // 监控指标
   app.UseMetrics();
   ```

### 日志配置

```csharp
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddFile("logs/app.log");
    logging.SetMinimumLevel(LogLevel.Information);

    // PulseRPC 特定日志级别
    logging.AddFilter("PulseRPC.Server.Transport", LogLevel.Warning);
    logging.AddFilter("PulseRPC.Server.Sessions", LogLevel.Information);
});
```

## 最佳实践

### 1. 服务设计
- 保持服务接口简洁，避免过度复杂的参数
- 使用 MemoryPack 序列化，确保高性能
- 实现适当的错误处理和验证

### 2. 性能优化
- 启用内存池减少 GC 压力
- 合理配置线程池大小
- 使用异步编程模式

### 3. 安全考虑
- 实现强认证和授权机制
- 启用 TLS 加密（生产环境）
- 限制并发连接数

### 4. 监控运维
- 实现全面的健康检查
- 收集关键性能指标
- 设置适当的告警阈值

这份指南涵盖了 PulseRPC.Server 的主要使用场景和最佳实践。根据具体需求，您可以选择相应的功能进行实现和配置。