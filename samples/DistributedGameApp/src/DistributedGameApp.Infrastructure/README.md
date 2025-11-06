# DistributedGameApp.Infrastructure

基础设施层，提供 MongoDB、Consul 和 Sentry 集成。

## 功能

### 1. MongoDB 集成

提供完整的数据持久化支持。

#### 使用方法

```csharp
// 在 Program.cs 中
using DistributedGameApp.Infrastructure.MongoDB.Extensions;

builder.Services.AddMongoDb(builder.Configuration);
```

#### 配置（appsettings.json）

```json
{
  "MongoDB": {
    "ConnectionString": "mongodb://admin:password123@localhost:27017",
    "AccountsDatabase": "game_accounts",
    "CharactersDatabase": "game_characters",
    "SocialDatabase": "game_social",
    "GuildsDatabase": "game_guilds",
    "BattlesDatabase": "game_battles",
    "LeaderboardsDatabase": "game_leaderboards"
  }
}
```

#### 使用 Repository

```csharp
public class MyService
{
    private readonly AccountRepository _accountRepo;

    public MyService(AccountRepository accountRepo)
    {
        _accountRepo = accountRepo;
    }

    public async Task<Account?> GetAccountAsync(string userId)
    {
        return await _accountRepo.GetByUserIdAsync(userId);
    }
}
```

#### 可用的 Repository

- `AccountRepository` - 账户管理
- `CharacterRepository` - 角色管理
- `GuildRepository` - 帮派管理
- `GuildMemberRepository` - 帮派成员管理
- `FriendRepository` - 好友管理
- `ChatMessageRepository` - 聊天消息
- `LeaderboardRepository` - 排行榜
- `BattleRecordRepository` - 战斗记录

### 2. Consul 集成

提供服务注册与发现功能。

#### 使用方法

```csharp
// 在 Program.cs 中
using DistributedGameApp.Infrastructure.Consul.Extensions;

builder.Services.AddConsul(builder.Configuration);
```

#### 配置（appsettings.json）

```json
{
  "Consul": {
    "Address": "http://localhost:8500",
    "ServiceBasePath": "pulserpc",
    "HealthCheckInterval": 10,
    "HealthCheckTimeout": 3,
    "DeregisterCriticalServiceAfter": 30
  }
}
```

#### 注册服务

```csharp
public class GameServerStartup
{
    private readonly ConsulServiceRegistry _registry;

    public async Task StartAsync()
    {
        var registration = new ServiceRegistration
        {
            ServiceId = "game-server-1",
            ServiceType = "GameServer",
            NodeId = 1,
            NodeName = "GameServer-Node1",
            Host = "10.0.1.10",
            TcpPort = 8080,
            KcpPort = 8081,
            CurrentLoad = 0,
            MaxCapacity = 5000,
            Status = "Online"
        };

        await _registry.RegisterServiceAsync(registration);
    }
}
```

#### 发现服务

```csharp
public class LoginService
{
    private readonly ConsulServiceDiscovery _discovery;

    public async Task<ServiceRegistration?> GetBestGameServerAsync()
    {
        // 获取负载最低的游戏服务器
        return await _discovery.DiscoverBestServiceAsync("GameServer");
    }

    public async Task<List<ServiceRegistration>> GetAllGameServersAsync()
    {
        return await _discovery.GetServicesAsync("GameServer");
    }
}
```

#### 监听服务变更（Blocking Query）

使用 Consul 阻塞查询实时监听服务变更：

```csharp
public class ServiceMonitor : BackgroundService
{
    private readonly ConsulServiceDiscovery _discovery;
    private readonly ILogger<ServiceMonitor> _logger;
    private readonly ConcurrentDictionary<string, ServiceRegistration> _services = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _discovery.WatchServicesAsync(
            serviceType: "GameServer",
            callback: (changeType, service) =>
            {
                switch (changeType)
                {
                    case ServiceChangeType.Added:
                        _services.TryAdd(service.ServiceId, service);
                        _logger.LogInformation("服务上线: {ServiceId} at {Host}:{Port}",
                            service.ServiceId, service.Host, service.TcpPort);
                        break;

                    case ServiceChangeType.Modified:
                        _services[service.ServiceId] = service;
                        _logger.LogInformation("服务更新: {ServiceId}, 负载: {Load}/{Max}",
                            service.ServiceId, service.CurrentLoad, service.MaxCapacity);
                        break;

                    case ServiceChangeType.Removed:
                        _services.TryRemove(service.ServiceId, out _);
                        _logger.LogWarning("服务下线: {ServiceId}", service.ServiceId);
                        break;
                }
            },
            cancellationToken: stoppingToken);
    }
}
```

**Consul 优势：**
- ✅ **健康检查**：自动检测服务健康状态，失效自动下线
- ✅ **阻塞查询**：长轮询机制，实时检测服务变更
- ✅ **多数据中心**：原生支持跨数据中心服务发现
- ✅ **DNS 接口**：支持通过 DNS 查询服务
- ✅ **KV 存储**：内置分布式键值存储

### 3. Sentry 集成

提供错误追踪和性能监控。

#### 使用方法

```csharp
// 在 Program.cs 中
using DistributedGameApp.Infrastructure.Sentry.Extensions;

builder.Services.AddSentryLogging(builder.Configuration);
```

#### 配置（appsettings.json）

```json
{
  "Sentry": {
    "Dsn": "https://your-sentry-dsn",
    "Environment": "Production",
    "Enabled": true,
    "SampleRate": 1.0,
    "TracesSampleRate": 0.1,
    "SendDefaultPii": false,
    "MaxBreadcrumbs": 100,
    "AttachStacktrace": true
  }
}
```

#### 使用日志

```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }

    public void DoSomething()
    {
        try
        {
            // 业务逻辑
            _logger.LogInformation("Operation completed successfully");
        }
        catch (Exception ex)
        {
            // 错误会自动发送到 Sentry
            _logger.LogError(ex, "Operation failed");
            throw;
        }
    }
}
```

## 完整示例

```csharp
using DistributedGameApp.Infrastructure.MongoDB.Extensions;
using DistributedGameApp.Infrastructure.Consul.Extensions;
using DistributedGameApp.Infrastructure.Sentry.Extensions;

var builder = WebApplication.CreateBuilder(args);

// 添加所有基础设施服务
builder.Services.AddMongoDb(builder.Configuration);
builder.Services.AddConsul(builder.Configuration);
builder.Services.AddSentryLogging(builder.Configuration);

var app = builder.Build();

// 注册服务到 Consul
var registry = app.Services.GetRequiredService<ConsulServiceRegistry>();
await registry.RegisterServiceAsync(new ServiceRegistration
{
    ServiceId = "my-service-1",
    ServiceType = "GameServer",
    Host = "localhost",
    TcpPort = 8080
});

app.Run();
```

## 测试

### 1. 测试 MongoDB 连接

```bash
# 启动 MongoDB
docker-compose up -d mongodb

# 在代码中测试
var context = app.Services.GetRequiredService<MongoDbContext>();
var accountRepo = app.Services.GetRequiredService<AccountRepository>();

var account = new Account
{
    UserId = Guid.NewGuid().ToString(),
    Email = "test@example.com",
    Username = "testuser"
};

await accountRepo.InsertAsync(account);
```

### 2. 测试 Consul 服务注册

```bash
# 启动 Consul
docker-compose up -d consul

# 测试注册
var registry = app.Services.GetRequiredService<ConsulServiceRegistry>();
await registry.RegisterServiceAsync(registration);

# 使用 Consul UI 或 API 验证
# UI: http://localhost:8500/ui
# API: curl http://localhost:8500/v1/catalog/services
```

### 3. 测试 Sentry

```csharp
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogError("Test error message");
// 检查 Sentry Dashboard
```

## 依赖

- MongoDB.Driver 3.4.0
- Consul 1.7.14.7
- Sentry 5.16.2
- Sentry.Extensions.Logging 5.16.2

## 许可证

MIT License
