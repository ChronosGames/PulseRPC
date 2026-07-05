# 游戏服务器节点架构详解

> 文档状态：GameApp 架构蓝图，部分章节保留早期设计选项。当前 `samples/GameApp/src` 中服务端项目目标框架为 `net9.0`，共享库为 `net9.0;netstandard2.1`；AuthServer 使用 ASP.NET Core HTTP API，GameServer/BattleServer 使用 PulseRPC + MemoryPack。下文提到 SignalR、DotNetty、MessagePack 的位置属于历史备选方案，不代表当前 GameApp 源码依赖。

## 整体架构概览

```
Unity客户端 ──→ 负载均衡器 ──→ 各服务节点
                                ↓
                            基础设施层
                    Redis + MongoDB + etcd
```

## 服务器节点详细说明

### 1. 认证服务器 (Authentication Server) - HTTP + JSON

**节点职责：**
- 用户账号验证与授权 (HTTP API)
- JWT Token生成与验证
- GameServer访问票据颁发
- 登录状态管理
- 账号安全策略执行
- 第三方登录集成 (可选)

**技术栈：**
```csharp
// 框架：ASP.NET Core 9.0 Web API
// 协议：HTTP/HTTPS + JSON
// 主要库：
- Microsoft.AspNetCore.Authentication.JwtBearer
- BCrypt.Net (密码加密)
- FluentValidation (输入验证)
- Swashbuckle.AspNetCore (Swagger文档)
- Serilog (日志记录)

// 核心API控制器
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IGameServerTicketService _ticketService;
    private readonly IRedisCache _redisCache;
    
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        // 用户密码验证
        var user = await _userService.ValidateUserAsync(request.Username, request.Password);
        if (user == null)
            return Unauthorized(new { message = "Invalid credentials" });
            
        // 生成JWT Token
        var token = await _jwtTokenService.GenerateTokenAsync(user);
        
        // 生成GameServer访问票据
        var ticket = await _ticketService.GenerateTicketAsync(user.UserId);
        
        return Ok(new LoginResponse
        {
            Token = token,
            GameTicket = ticket,
            User = user.ToDto(),
            ExpiresIn = 3600
        });
    }
    
    [HttpPost("verify-ticket")]
    [Authorize] // GameServer调用此接口验证票据
    public async Task<ActionResult<TicketValidationResponse>> VerifyTicket([FromBody] TicketValidationRequest request)
    {
        var validation = await _ticketService.ValidateTicketAsync(request.Ticket);
        return Ok(validation);
    }
    
    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshResponse>> RefreshToken([FromBody] RefreshRequest request)
    {
        // Token刷新逻辑
    }
}

// 数据模型
public class LoginRequest
{
    [Required]
    public string Username { get; set; }
    
    [Required]
    public string Password { get; set; }
    
    public string DeviceId { get; set; }
}

public class LoginResponse
{
    public string Token { get; set; }           // JWT访问令牌
    public string GameTicket { get; set; }      // GameServer访问票据
    public UserDto User { get; set; }
    public int ExpiresIn { get; set; }
}

public class GameServerTicket
{
    public string TicketId { get; set; }
    public uint UserId { get; set; }
    public string Username { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string ServerGroup { get; set; }     // 允许访问的服务器组
}
```

**HTTP API接口设计：**
```csharp
// 客户端认证接口
POST /api/auth/login              // 用户登录
POST /api/auth/logout             // 用户登出  
POST /api/auth/refresh            // Token刷新
GET  /api/auth/profile            // 获取用户信息

// GameServer验证接口
POST /api/auth/verify-ticket      // 验证游戏票据
POST /api/auth/validate-token     // 验证JWT Token
GET  /api/auth/server-list        // 获取授权服务器列表
```

**数据存储：**
- MongoDB: 用户基本信息、登录历史、票据记录
- Redis: JWT Token缓存、GameServer票据、登录限制、黑名单

---

### 2. 区服管理服务器 (Zone Management Server)

**节点职责：**
- 区服列表维护与分发
- 区服状态监控 (在线/维护/拥挤)
- 区服负载均衡策略
- 新区开服管理
- 区服配置动态更新

**技术栈：**
```csharp
// 框架：ASP.NET Core 9.0 + HTTP API（SignalR 为历史备选）
// 主要库：
- Microsoft.AspNetCore.SignalR (历史备选，当前源码未接入)
- Quartz.NET (定时任务)
- Microsoft.Extensions.Hosting (后台服务)

// 核心组件
public class ZoneManagementService : BackgroundService
{
    private readonly IEtcdClient _etcdClient;
    private readonly IMongoRepository<ZoneInfo> _zoneRepository;
    private readonly IHubContext<ZoneStatusHub> _hubContext;
}
```

**数据存储：**
- etcd: 区服实时状态、服务发现
- MongoDB: 区服配置信息、历史数据
- Redis: 区服负载缓存、用户分布

---

### 3. 资源管理服务器 (Resource Management Server)

**节点职责：**
- 游戏资源版本管理
- 增量更新包生成
- CDN资源分发协调
- 资源完整性验证
- 下载统计与监控

**技术栈：**
```csharp
// 框架：ASP.NET Core 9.0
// 主要库：
- System.IO.Compression (资源压缩)
- System.Security.Cryptography (文件校验)
- Microsoft.Extensions.Caching.Memory
- Hangfire (后台任务处理)

// 核心组件
public class ResourceController : ControllerBase
{
    private readonly IResourceVersionService _versionService;
    private readonly ICdnService _cdnService;
    private readonly IFileHashService _hashService;
}
```

**数据存储：**
- MongoDB: 资源版本信息、文件元数据
- Redis: 热门资源缓存、下载统计
- 文件系统: 实际资源文件存储

---

### 4. 配置管理服务器 (Configuration Server)

**节点职责：**
- 游戏配置集中管理
- 配置热更新推送
- 环境配置隔离
- 配置版本控制
- A/B测试配置支持

**技术栈：**
```csharp
// 框架：ASP.NET Core 9.0
// 主要库：
- Microsoft.Extensions.Configuration
- Microsoft.AspNetCore.SignalR (历史备选，当前源码未接入)
- System.Text.Json (配置序列化)

// 核心组件
public class ConfigurationService : IConfigurationService
{
    private readonly IEtcdClient _etcdClient;
    private readonly IMongoRepository<GameConfig> _configRepository;
    private readonly IMemoryCache _cache;
}
```

**数据存储：**
- etcd: 实时配置、配置变更通知
- MongoDB: 配置历史版本、审计日志

---

### 5. 游戏网关服务器 (Game Gateway Server)

**节点职责：**
- 客户端连接接入
- 负载均衡与路由
- 协议转换与适配
- 连接状态管理
- 心跳检测

**技术栈：**
```csharp
// 框架：历史网关方案；当前 GameServer/BattleServer 直接使用 PulseRPC TCP/KCP
// 主要库：
- PulseRPC TCP/KCP (当前样例服务端)
- MemoryPack (序列化)
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging

// 核心组件
public class GameGatewayServer
{
    private readonly IMultithreadEventLoopGroup _bossGroup;
    private readonly IMultithreadEventLoopGroup _workerGroup;
    private readonly IServiceProvider _serviceProvider;
}

public class GameChannelHandler : ChannelHandlerAdapter
{
    private readonly IPlayerSessionManager _sessionManager;
    private readonly IGameServerManager _gameServerManager;
}
```

**数据存储：**
- Redis: 玩家会话信息、连接状态
- etcd: 游戏服务器节点发现

---

### 6. 游戏服务器 (GameServer) - 有状态

**节点职责：**
- 主城/世界地图逻辑处理
- 玩家基础数据管理 (等级、装备、背包等)
- 任务系统处理
- 成就系统管理
- 商店交易逻辑
- 玩家状态持久化
- 跨服数据同步

**技术栈：**
```csharp
// 框架：.NET 9.0 Console Application
// 主要库：
- PulseRPC TCP/KCP (网络通信)
- Microsoft.Extensions.Hosting (服务生命周期)
- MemoryPack (高效序列化)
- System.Collections.Concurrent (线程安全集合)
- Timer (定时持久化)

// 核心组件
public class GameServer : BackgroundService
{
    private readonly ConcurrentDictionary<uint, PlayerSession> _onlinePlayers;
    private readonly IGameWorldManager _worldManager;
    private readonly IPlayerStateManager _stateManager;
    private readonly IDataPersistenceService _persistenceService;
}

public class PlayerSession
{
    public uint PlayerId { get; set; }
    public PlayerData PlayerData { get; set; }
    public DateTime LastActiveTime { get; set; }
    public bool IsDirty { get; set; }  // 数据是否需要持久化
    
    public void MarkDirty() => IsDirty = true;
}

public class PlayerStateManager : IPlayerStateManager
{
    private readonly Timer _persistenceTimer;
    private readonly IMongoRepository<PlayerData> _playerRepository;
    
    // 定期持久化脏数据
    private async void PersistDirtyData(object state) { ... }
}
```

**状态管理策略：**
- 内存状态: 玩家完整数据常驻内存
- 定时持久化: 每30秒持久化脏数据
- 优雅下线: 服务器关闭时保存所有状态
- 故障恢复: 启动时从MongoDB恢复状态

---

### 7. 战斗服务器 (BattleServer) - 有状态

**节点职责：**
- 实时战斗逻辑处理
- 战斗房间管理
- 技能释放与效果计算
- 伤害计算与结算
- 战斗状态同步
- 战斗结果统计
- PVP/PVE战斗支持

**技术栈：**
```csharp
// 框架：.NET 9.0 Console Application
// 主要库：
- PulseRPC KCP/TCP (低延迟网络通信)
- System.Numerics (向量计算)
- Microsoft.Extensions.ObjectPool (对象池)
- System.Threading.Channels (高性能队列)

// 核心组件
public class BattleServer : BackgroundService
{
    private readonly ConcurrentDictionary<uint, BattleRoom> _activeBattles;
    private readonly IBattleLogicEngine _battleEngine;
    private readonly ISkillSystem _skillSystem;
    private readonly Channel<BattleCommand> _commandChannel;
}

public class BattleRoom
{
    public uint RoomId { get; set; }
    public List<BattleUnit> Units { get; set; }
    public BattleState State { get; set; }
    public DateTime StartTime { get; set; }
    public BattleConfig Config { get; set; }
    
    private readonly object _stateLock = new object();
    
    public void ProcessCommand(BattleCommand command)
    {
        lock (_stateLock)
        {
            // 处理战斗指令
        }
    }
}

public class BattleLogicEngine : IBattleLogicEngine
{
    public void UpdateBattle(BattleRoom room, float deltaTime)
    {
        // 战斗逻辑更新 (60fps)
        ProcessSkills(room, deltaTime);
        ProcessMovement(room, deltaTime);
        ProcessDamage(room);
        CheckBattleEnd(room);
    }
}
```

**实时性保障：**
- 高频更新: 60fps战斗逻辑更新
- 指令队列: 异步处理战斗指令
- 状态同步: 实时向客户端推送状态
- 预测回滚: 支持客户端预测和服务器权威回滚

---

### 8. 社交服务器 (SocialServer) - 有状态

**节点职责：**
- 好友关系管理
- 公会/帮派系统
- 聊天系统处理
- 邮件系统管理
- 排行榜维护
- 社交活动组织
- 在线状态管理

**技术栈：**
```csharp
// 框架：社交服务历史方案，当前 GameApp 源码未实现独立 SocialServer
// 主要库：
- PulseRPC TCP/KCP (游戏协议通信)
- System.Collections.Concurrent (线程安全集合)
- Microsoft.Extensions.Caching.Memory (内存缓存)

// 核心组件
public class SocialServer : BackgroundService
{
    private readonly ConcurrentDictionary<uint, PlayerSocialData> _playerSocialCache;
    private readonly ConcurrentDictionary<uint, Guild> _guilds;
    private readonly IChatChannelManager _chatManager;
    private readonly IMailSystem _mailSystem;
}

public class PlayerSocialData
{
    public uint PlayerId { get; set; }
    public HashSet<uint> Friends { get; set; }
    public uint? GuildId { get; set; }
    public PlayerOnlineStatus Status { get; set; }
    public DateTime LastSeenTime { get; set; }
    
    public void UpdateOnlineStatus(PlayerOnlineStatus status)
    {
        Status = status;
        LastSeenTime = DateTime.UtcNow;
        // 通知好友状态变更
    }
}

public class Guild
{
    public uint GuildId { get; set; }
    public string GuildName { get; set; }
    public ConcurrentDictionary<uint, GuildMember> Members { get; set; }
    public GuildWarehouse Warehouse { get; set; }
    
    public void BroadcastMessage(ChatMessage message)
    {
        // 向所有在线公会成员广播消息
    }
}

public class ChatChannelManager : IChatChannelManager
{
    private readonly ConcurrentDictionary<string, ChatChannel> _channels;
    
    public async Task BroadcastToChannel(string channelId, ChatMessage message)
    {
        if (_channels.TryGetValue(channelId, out var channel))
        {
            await channel.BroadcastAsync(message);
        }
    }
}
```

**社交功能特色：**
- 实时聊天: 当前源码未接入独立 SignalR/WebSocket 社交服务
- 好友状态: 实时更新在线状态
- 公会活动: 支持大型公会活动组织
- 跨服社交: 支持跨服务器好友和公会

---

## 有状态服务器架构特点

### 🎯 **状态管理策略**

#### 数据分层设计
```csharp
public class StateManagerBase<T> where T : class, IEntity
{
    protected readonly ConcurrentDictionary<uint, T> _memoryCache;
    protected readonly IMongoRepository<T> _repository;
    protected readonly IRedisDatabase _redisCache;
    
    // 三级缓存：内存 -> Redis -> MongoDB
    public async Task<T> GetAsync(uint id)
    {
        // 1. 优先从内存获取
        if (_memoryCache.TryGetValue(id, out var entity))
            return entity;
            
        // 2. 从Redis获取
        var cached = await _redisCache.GetAsync<T>($"entity:{id}");
        if (cached != null)
        {
            _memoryCache.TryAdd(id, cached);
            return cached;
        }
        
        // 3. 从MongoDB获取
        var persistent = await _repository.GetByIdAsync(id);
        if (persistent != null)
        {
            await _redisCache.SetAsync($"entity:{id}", persistent, TimeSpan.FromMinutes(30));
            _memoryCache.TryAdd(id, persistent);
        }
        
        return persistent;
    }
}
```

#### 服务器亲和性
```csharp
public class ServerAffinityService : IServerAffinityService
{
    private readonly IEtcdClient _etcdClient;
    
    public async Task<string> GetPlayerServerAsync(uint playerId)
    {
        // 从etcd获取玩家所在服务器
        var key = $"player_server/{playerId}";
        var response = await _etcdClient.GetAsync(key);
        return response.Value;
    }
    
    public async Task SetPlayerServerAsync(uint playerId, string serverId)
    {
        // 设置玩家服务器绑定
        var key = $"player_server/{playerId}";
        await _etcdClient.PutAsync(key, serverId);
    }
}
```

### 🔄 **故障恢复机制**

#### 状态迁移服务
```csharp
public class StateTransferService : IStateTransferService
{
    public async Task TransferPlayerStateAsync(uint playerId, string fromServer, string toServer)
    {
        // 1. 从源服务器序列化状态
        var playerState = await GetPlayerStateFromServer(fromServer, playerId);
        
        // 2. 传输到目标服务器
        await SendPlayerStateToServer(toServer, playerState);
        
        // 3. 更新路由信息
        await UpdatePlayerRouting(playerId, toServer);
        
        // 4. 通知客户端重连
        await NotifyClientReconnect(playerId, toServer);
    }
}
```

### 📊 **性能监控**

```csharp
public class ServerMetrics
{
    public int OnlinePlayerCount { get; set; }
    public double MemoryUsage { get; set; }
    public double CpuUsage { get; set; }
    public int ActiveBattleCount { get; set; }  // BattleServer专用
    public int ActiveGuildCount { get; set; }   // SocialServer专用
}

public class MetricsCollectionService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var metrics = CollectMetrics();
            await ReportToMonitoring(metrics);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

这种有状态服务器架构提供了更好的性能和用户体验，同时通过合理的状态管理和故障恢复机制确保了系统的可靠性。

---

## 基础设施配置

### Redis 配置示例
```csharp
public class RedisConfiguration
{
    public string ConnectionString { get; set; }
    public int Database { get; set; } = 0;
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(30);
}

// 使用
services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConfig.ConnectionString;
});
```

### MongoDB 配置示例
```csharp
public class MongoDbConfiguration
{
    public string ConnectionString { get; set; }
    public string DatabaseName { get; set; }
    public bool EnableRetryWrites { get; set; } = true;
}

// 使用
services.AddSingleton<IMongoClient>(provider =>
    new MongoClient(mongoConfig.ConnectionString));
```

### etcd 配置示例
```csharp
public class EtcdConfiguration
{
    public string[] Endpoints { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}

// 使用 dotnet-etcd 库
services.AddSingleton<IEtcdClient>(provider =>
    new EtcdClient(etcdConfig.Endpoints));
```

## 部署建议

### 容器化部署 (Docker)
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY . .
EXPOSE 80
ENTRYPOINT ["dotnet", "GameServer.dll"]
```

### 服务编排 (docker-compose)
```yaml
version: '3.8'
services:
  auth-server:
    build: ./AuthServer
    environment:
      - Redis__ConnectionString=redis:6379
      - MongoDB__ConnectionString=mongodb://mongo:27017
  
  game-gateway:
    build: ./GameGateway
    ports:
      - "8080:80"
    depends_on:
      - redis
      - mongodb
      - etcd
```

## 监控与日志

### 统一日志配置
```csharp
public static void ConfigureLogging(this IServiceCollection services)
{
    services.AddSerilog((serviceProvider, loggerConfig) =>
        loggerConfig
            .WriteTo.Console()
            .WriteTo.MongoDB(mongoUrl, collectionName: "logs")
            .WriteTo.Redis(redisConfiguration));
}
```

这个架构设计确保了高可用性、可扩展性，并且所有组件都基于C#生态系统，便于开发团队维护和扩展。
