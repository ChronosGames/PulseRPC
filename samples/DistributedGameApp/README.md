# DistributedGameApp - 生产级分布式游戏服务器示例

> 文档状态：样例总览。部分“设计理念/扩展指南”代码块是架构伪代码；当前可运行入口以 `src/DistributedGameApp.*` 下的 `Program.cs`、`Infrastructure/Hosting/ServerBootstrapper.cs` 和项目内文档为准。

> **🎉 架构升级 V2.0**
> 这是一个基于 **PulseRPC** 框架的完整分布式游戏服务器架构，包含完整的基础设施集成（MongoDB + Consul + Sentry）和多服务器类型（LoginServer + GameServer + BattleServer + BackendServer）。
>
> 📖 **快速开始**：查看 [QUICKSTART.md](./QUICKSTART.md)
> 🏗️ **完整架构**：查看 [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)
> 📋 **项目状态**：查看 [PROJECT_STATUS.md](./PROJECT_STATUS.md)

## 🌟 V2.0 新特性

### ✨ 完整的微服务架构
- **LoginServer**：HTTP WebAPI + JWT + OAuth2 第三方登录
- **GameServer**：PulseRPC 游戏网关（角色管理、背包、任务）
- **BattleServer**：PulseRPC 战斗服务器（实时战斗、房间管理）
- **BackendServer**：PulseRPC 后台服务（社交、帮派、排行榜、匹配）

### 🏢 基础设施集成
- **MongoDB**：数据持久化（账户、角色、社交、帮派、战斗记录）
- **Consul**：服务注册与发现、配置中心、健康检查
- **Sentry**：日志追踪和错误监控
- **Docker Compose**：一键启动所有基础设施

### 📋 完整的领域模型
- **Accounts**：用户账户、第三方登录、JWT 认证
- **Characters**：角色、背包、装备、属性
- **Battles**：战斗房间、战斗动作、战斗结果
- **Social**：好友、私聊、世界频道
- **Guilds**：帮派、成员、聊天
- **Leaderboards**：排行榜、赛季排行
- **Matchmaking**：匹配系统、队伍组建

### 📚 完善的文档
- [QUICKSTART.md](./QUICKSTART.md) - 5分钟快速开始
- [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) - 完整架构设计
- [PROJECT_STATUS.md](./PROJECT_STATUS.md) - 项目状态
- [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) - 原架构设计文档（保留）
- [PRODUCTION_GUIDE.md](./PRODUCTION_GUIDE.md) - 生产级实施指南（保留）

## 📖 设计理念

本示例基于项目内 [docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md) 中描述的架构理念：

### 核心概念

1. **IPulseService** - 服务实例
   - 每个服务实例由 `ServiceName` + `ServiceId` 唯一标识
   - 相同 `ServiceId` 的所有请求路由到同一线程顺序执行
   - 实现灾难隔离和线程亲和性

2. **IPulseHub** - 客户端可调用的服务方法
   - 定义了客户端可以远程调用的所有方法
   - 每个方法通过协议号进行标识

3. **`[Channel("CLIENT")] : IPulseHub`** - 服务器推送的事件
   - 定义了服务器可以向客户端推送的所有事件
   - 实现实时通知和状态同步

4. **PID (Process Identifier)** - 进程标识符
   - 唯一标识一个服务实例
   - 包含节点信息、服务类型、实例ID等

## 🏗️ 项目结构

```
DistributedGameApp/
├── src/
│   ├── DistributedGameApp.Shared/      # 共享层：协议定义
│   │   ├── Messages/                   # 消息类型（MemoryPack序列化）
│   │   │   ├── PlayerInfo.cs           # 玩家相关消息
│   │   │   └── ChatMessages.cs         # 聊天相关消息
│   │   ├── Hubs/                       # Hub接口（客户端可调用）
│   │   │   ├── IPlayerHub.cs           # 玩家服务接口
│   │   │   └── IChatRoomHub.cs         # 聊天室服务接口
│   │   └── Receivers/                  # Receiver接口（服务器推送）
│   │       ├── IPlayerReceiver.cs      # 玩家事件接收
│   │       └── IChatRoomReceiver.cs    # 聊天室事件接收
│   ├── DistributedGameApp.Server/      # 服务端层：服务实现
│   │   ├── Services/
│   │   │   ├── PlayerService.cs        # 玩家服务实现
│   │   │   └── ChatRoomService.cs      # 聊天室服务实现
│   │   └── Program.cs                  # 服务端启动程序
│   └── DistributedGameApp.Client/      # 客户端层：客户端示例
│       └── Program.cs                  # 客户端程序
├── Directory.Build.props               # 全局构建配置
├── Directory.Packages.props            # 包版本管理
├── DistributedGameApp.sln              # 解决方案文件
└── README.md                           # 本文档
```

## 🎯 核心示例

### 1. PlayerService - 玩家服务

每个玩家是一个独立的服务实例，具有唯一的 `ServiceId`：

```csharp
public class PlayerService : IPlayerHub, IPulseService
{
    public string ServiceName => "Player";
    public string ServiceId { get; }  // 例如："Player:player1"

    public PlayerService(string playerId, ILogger<PlayerService> logger)
    {
        ServiceId = $"Player:{playerId}";
        // ... 初始化玩家数据
    }

    // 实现 IPlayerHub 的方法
    public Task<PlayerInfo?> GetPlayerInfoAsync() { /* ... */ }
    public Task<MoveResult> MoveAsync(MoveRequest request) { /* ... */ }
    public Task<PlayerInfo?> LevelUpAsync() { /* ... */ }
    public Task<PlayerInfo?> AddExpAsync(long exp) { /* ... */ }
}
```

**关键特性**：
- 同一个玩家的所有请求都在同一个线程上顺序执行
- 无需加锁，简化并发控制
- 自动实现灾难隔离

### 2. ChatRoomService - 聊天室服务

每个聊天室是一个独立的服务实例：

```csharp
public class ChatRoomService : IChatRoomHub, IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }  // 例如："ChatRoom:room1"

    public ChatRoomService(string roomId, string roomName, int maxMembers, ILogger<ChatRoomService> logger)
    {
        ServiceId = $"ChatRoom:{roomId}";
        // ... 初始化聊天室
    }

    // 实现 IChatRoomHub 的方法
    public Task<bool> JoinRoomAsync(string playerId, string playerName) { /* ... */ }
    public Task<bool> LeaveRoomAsync(string playerId) { /* ... */ }
    public Task<SendMessageResult> SendMessageAsync(SendMessageRequest request) { /* ... */ }
}
```

**关键特性**：
- 同一个聊天室的所有消息顺序处理
- 保证消息顺序性
- 支持房间级别的隔离

## 🚀 快速开始

### 环境要求

- .NET 9.0 SDK
- Visual Studio 2022 或 Rider

### 构建项目

```bash
# 进入项目目录
cd samples/DistributedGameApp

# 还原依赖
dotnet restore

# 构建所有项目
dotnet build

# 运行服务端
dotnet run --project src/DistributedGameApp.Server

# 运行客户端（新开终端）
dotnet run --project src/DistributedGameApp.Client
```

### 配置说明

服务器使用 `appsettings.json` 配置文件，支持以下配置：

```json
{
  "ServerConfiguration": {
    "NodeId": 1,              // 节点ID (1-65535)
    "ClusterId": 1,           // 集群ID (1-65535)
    "NodeName": "GameNode01", // 节点名称
    "ExternalEndpoint": {     // 对外端点（客户端连接）
      "TcpPort": 8080,        // TCP 端口
      "KcpPort": 8081         // KCP 端口（低延迟）
    },
    "InternalEndpoint": {     // 对内端点（节点间通信）
      "TcpPort": 9080         // TCP 端口
    },
    "MaxConnections": 10000,  // 最大连接数
    "EnablePerformanceMonitoring": true
  },
  "PulseServer": {
    "Transports": [
      {
        "Name": "ExternalTcp",
        "Type": "Tcp",
        "Port": 8080,
        "IsDefault": true,
        "Host": "0.0.0.0"
      },
      {
        "Name": "ExternalKcp",
        "Type": "Kcp",
        "Port": 8081,
        "IsDefault": false,
        "Host": "0.0.0.0"
      },
      {
        "Name": "InternalTcp",
        "Type": "Tcp",
        "Port": 9080,
        "IsDefault": false,
        "Host": "0.0.0.0"
      }
    ]
  }
}
```

### 运行示例

服务端启动后会显示配置信息和监听端口：

```
╔══════════════════════════════════════════════════════════════╗
║    DistributedGameApp Server - 分布式游戏服务器示例          ║
║    基于 PulseRPC 框架构建                                    ║
╚══════════════════════════════════════════════════════════════╝

=== 服务器配置 ===
节点信息:
  - 集群ID: 1
  - 节点ID: 1
  - 节点名称: GameNode01
网络配置:
  - 对外端口: TCP=8080, KCP=8081
  - 对内端口: TCP=9080
性能配置:
  - 最大连接数: 10000
  - 性能监控: 启用

正在启动 PulseRPC 服务器...
服务器启动成功!
对外端口:
  - TCP: 8080 (客户端连接)
  - KCP: 8081 (客户端连接, 低延迟)
对内端口:
  - TCP: 9080 (节点间通信)

按 Ctrl+C 停止服务器
```

## 📚 核心设计模式

### 1. 服务隔离模式

```
每个服务实例独立运行，互不影响：

Player:player1 ──> 专属线程1 ──> 顺序处理玩家1的所有请求
Player:player2 ──> 专属线程2 ──> 顺序处理玩家2的所有请求
ChatRoom:room1 ──> 专属线程3 ──> 顺序处理房间1的所有消息
ChatRoom:room2 ──> 专属线程4 ──> 顺序处理房间2的所有消息
```

### 2. 协议定义模式

**Shared层定义三种类型**：
- **Messages** - 使用 MemoryPack 序列化的数据类型
- **Hubs** - 客户端可调用的服务方法接口
- **Receivers** - 服务器可推送的事件接口

```csharp
// 1. 定义消息类型
[MemoryPackable]
public partial class PlayerInfo
{
    public string PlayerId { get; set; }
    public string PlayerName { get; set; }
    public int Level { get; set; }
}

// 2. 定义Hub接口（客户端调用）
public interface IPlayerHub : IPulseHub
{
    Task<PlayerInfo?> GetPlayerInfoAsync();
}

// 3. 定义Receiver接口（服务器推送，标注 [Channel("CLIENT")] 表示由客户端实现）
[Channel("CLIENT")]
public interface IPlayerReceiver : IPulseHub
{
    Task OnPlayerLevelUpAsync(PlayerInfo playerInfo);
}
```

### 3. 分布式部署模式

```
客户端1 ──┐
客户端2 ──┼──> 节点1 (NodeId: 1) ──> Player:player1, ChatRoom:room1
客户端3 ──┤                          Player:player2
客户端4 ──┘
          │
客户端5 ──┐
客户端6 ──┼──> 节点2 (NodeId: 2) ──> Player:player3, ChatRoom:room2
客户端7 ──┤                          Player:player4
客户端8 ──┘
```

通过一致性哈希，服务实例可以分布在不同的节点上。

## 🔧 扩展指南

### 添加新的服务类型

1. **在 Shared 层定义协议**：
   ```csharp
   // Messages/BattleMessages.cs
   [MemoryPackable]
   public partial class BattleInfo { /* ... */ }

   // Hubs/IBattleHub.cs
   public interface IBattleHub : IPulseHub
   {
       Task<BattleInfo?> GetBattleInfoAsync();
   }

   // Receivers/IBattleReceiver.cs
   [Channel("CLIENT")]
   public interface IBattleReceiver : IPulseHub
   {
       Task OnBattleStartedAsync(BattleInfo info);
   }
   ```

2. **在 Server 层实现服务**：
   ```csharp
   public class BattleService : IBattleHub, IPulseService
   {
       public string ServiceName => "Battle";
       public string ServiceId { get; }

       public BattleService(string battleId)
       {
           ServiceId = $"Battle:{battleId}";
       }

       // 实现接口方法...
   }
   ```

### 集成 PulseRPC 网络层

实际部署时需要集成 PulseRPC 的网络层：

```csharp
// 服务端
var server = new PulseServer();
server.RegisterService<IPlayerHub, PlayerService>();
server.RegisterService<IChatRoomHub, ChatRoomService>();
await server.StartAsync("tcp://0.0.0.0:8080");

// 客户端
var client = new PulseClient();
await client.ConnectAsync("tcp://localhost:8080");
var playerHub = client.GetProxy<IPlayerHub>();
var info = await playerHub.GetPlayerInfoAsync();
```

## 🎯 生产级特性详解

### 1. Actor 模型（服务隔离）

```csharp
public class PlayerSessionService : BaseService, IGameHub, IPulseService
{
    // ✅ 无需加锁 - Actor 模型保证单线程
    private PlayerInfo _player;
    private OnlineStatus _status;

    [RequirePermission("player.login")]
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        // 所有操作严格有序，无并发问题
        _player = await LoadFromDatabase();
        _status = OnlineStatus.Online;
        return new LoginResponse { Success = true };
    }
}
```

### 2. 性能指标

| 指标 | 数值 | 优化 |
|------|------|------|
| 消息吞吐 | 150K+ msg/s | 三层处理架构 |
| P99 延迟 | <7ms | 零拷贝设计 |
| 方法调用 | ~10ns | 表达式树编译 (50x) |
| 协议号查找 | ~10ns | O(1) 字典 (15x) |
| 带宽占用 | -87.5% | 2字节协议号 |

### 3. 服务架构

```
GameServer:5000    BattleServer:5100    ChatServer:5200
     │                   │                      │
  ┌──┴──┐            ┌───┴───┐            ┌────┴────┐
  │会话 │            │战斗室 │            │聊天室   │
  │匹配 │            │逻辑   │            │私聊     │
  │角色 │            │结算   │            │广播     │
  └─────┘            └───────┘            └─────────┘
```

## 📖 相关文档

### 本项目文档
- [ARCHITECTURE.md](./docs/ARCHITECTURE.md) - 完整架构设计文档
- [PRODUCTION_GUIDE.md](./PRODUCTION_GUIDE.md) - 生产级实施指南

### PulseRPC 核心文档
- [Service-Based-Messaging-Architecture](../../docs/architecture/Service-Based-Messaging-Architecture.md) - Actor 模型设计
- [PulseRPC.Server README](../../src/PulseRPC.Server/README.md) - 服务器架构
- [统一 IPulseHub 集群架构设计](../../docs/架构设计与分析/统一 IPulseHub 全链路寻址与集群架构设计.md) - 框架理念

## 💡 设计亮点

1. **服务隔离与线程亲和性**
   - 每个服务实例在独立线程上运行
   - 相同 ServiceId 的请求顺序执行
   - 无需加锁，简化并发控制

2. **灾难隔离与自动恢复**
   - 单个服务实例故障不影响其他实例
   - 自动隔离和恢复机制

3. **协议自动生成**
   - 通过源生成器自动生成协议号
   - 客户端和服务端自动保持一致

4. **高性能序列化**
   - 使用 MemoryPack 进行二进制序列化
   - 极致性能和最小内存分配

5. **分布式部署支持**
   - 支持一致性哈希分布
   - 支持节点动态扩容和缩容

## 🎯 适用场景

- **MMORPG** - 大型多人在线游戏
- **实时对战游戏** - 需要低延迟和高并发
- **聊天系统** - 需要消息顺序性保证
- **房间制游戏** - 每个房间独立隔离
- **分布式服务** - 需要水平扩展的游戏后端

## 📝 注意事项

这是一个**框架示例**，展示了核心架构和设计模式。在实际生产环境中，还需要考虑：

1. **持久化** - 集成数据库（MongoDB、Redis等）
2. **服务发现** - 集成 Consul 服务注册与发现
3. **负载均衡** - 实现一致性哈希和路由策略
4. **监控告警** - 实现健康检查和性能监控
5. **安全认证** - 实现 JWT、游戏票据等认证机制

## 📄 许可证

MIT License - 本示例代码可自由使用和修改
