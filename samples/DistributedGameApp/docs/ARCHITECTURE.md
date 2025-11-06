# DistributedGameApp 完整架构设计 V2

## 概述

这是一个生产级分布式游戏服务器架构，使用 PulseRPC 框架构建，集成了现代微服务架构的最佳实践。

## 架构图

```
┌──────────────────────────────────────────────────────────────┐
│                      Unity 客户端                             │
└───────┬──────────────────────────────────────────────────────┘
        │
        │ HTTP/HTTPS (登录)
        ▼
┌──────────────────────┐
│   LoginServer        │  ← 第三方登录 (OAuth2/JWT)
│   (ASP.NET Core)     │
│   Port: 5000         │
└──────────────────────┘
        │
        │ 返回 JWT Token + GameServer 地址
        │
        ▼
┌──────────────────────┐        ┌──────────────────────┐
│   GameServer         │◄──────►│   BattleServer       │
│   (PulseRPC)         │        │   (PulseRPC)         │
│   Port: 8080 (TCP)   │        │   Port: 8100 (TCP)   │
│   Port: 8081 (KCP)   │        │   Port: 8101 (KCP)   │
└──────────┬───────────┘        └──────────┬───────────┘
           │                               │
           │                               │
           └───────────┬───────────────────┘
                       │
                       │ PulseRPC 内部通信
                       │
                       ▼
           ┌──────────────────────┐
           │   BackendServer      │
           │   (PulseRPC)         │
           │   Port: 8200 (TCP)   │
           └──────────┬───────────┘
                      │
        ┌─────────────┼─────────────┐
        │             │             │
        ▼             ▼             ▼
   ┌────────┐   ┌────────┐   ┌────────┐
   │ MongoDB│   │ Consul │   │ Sentry │
   │ :27017 │   │ :8500  │   │        │
   └────────┘   └────────┘   └────────┘
     数据存储    服务发现     日志追踪
```

## 服务器角色

### 1. LoginServer (HTTP 登录服务器)

**技术栈**: ASP.NET Core WebAPI + JWT

**职责**:
- 处理第三方登录（OAuth2: Google, Facebook, Apple 等）
- 用户注册和认证
- 生成 JWT Token
- 返回可用的 GameServer 地址（从 Consul 获取）

**端点**:
- `POST /api/auth/login` - 第三方登录
- `POST /api/auth/register` - 用户注册
- `POST /api/auth/refresh` - 刷新 Token
- `GET /api/server/list` - 获取可用服务器列表

### 2. GameServer (游戏网关服务器)

**技术栈**: PulseRPC Server

**职责**:
- 作为 Unity 客户端的主要连接点
- 处理玩家会话管理
- 角色管理（创建、选择、删除）
- 背包、装备系统
- 任务系统
- 转发请求到其他服务器（BattleServer, BackendServer）

**服务类型**:
- `PlayerSessionService` - 玩家会话
- `CharacterService` - 角色管理
- `InventoryService` - 背包系统
- `QuestService` - 任务系统

### 3. BattleServer (战斗服务器)

**技术栈**: PulseRPC Server

**职责**:
- 处理实时战斗逻辑
- 战斗房间管理
- 技能释放和伤害计算
- 战斗状态同步
- 战斗结算

**服务类型**:
- `BattleRoomService` - 战斗房间
- `BattleLogicService` - 战斗逻辑
- `SkillService` - 技能系统

### 4. BackendServer (后台服务器)

**技术栈**: PulseRPC Server

**职责**:
- 排行榜系统（全服排行、赛季排行）
- 社交系统（好友、聊天）
- 帮派系统（公会管理）
- 匹配系统（PvP 匹配、组队匹配）
- 邮件系统
- 活动系统

**服务类型**:
- `LeaderboardService` - 排行榜
- `SocialService` - 社交系统
- `GuildService` - 帮派系统
- `MatchmakingService` - 匹配系统
- `MailService` - 邮件系统
- `ActivityService` - 活动系统

## 基础设施

### MongoDB (数据库)

**用途**:
- 用户账户数据
- 角色数据
- 背包和装备数据
- 帮派数据
- 社交关系数据
- 邮件数据
- 战斗记录

**数据库设计**:
```
databases:
  - game_accounts      # 账户数据库
  - game_characters    # 角色数据库
  - game_social        # 社交数据库
  - game_guilds        # 帮派数据库
  - game_battles       # 战斗记录
```

### Consul (服务注册与发现)

**用途**:
- 服务注册（GameServer, BattleServer, BackendServer）
- 节点健康检查
- 配置中心（KV Store）
- 分布式锁
- DNS 服务发现

**服务注册结构**:
```json
{
  "Service": {
    "Name": "game-server",
    "ID": "game-server-node-1",
    "Address": "10.0.1.10",
    "Port": 8080,
    "Tags": ["tcp", "kcp"],
    "Meta": {
      "load": "230",
      "maxConnections": "5000"
    },
    "Check": {
      "TCP": "10.0.1.10:8080",
      "Interval": "10s",
      "Timeout": "2s"
    }
  }
}
```

**KV Store 结构**:
```
services/
  game-servers/
    node-1 → { "host": "10.0.1.10", "port": 8080, "load": 230 }
    node-2 → { "host": "10.0.1.11", "port": 8080, "load": 150 }
  battle-servers/
    node-1 → { "host": "10.0.2.10", "port": 8100, "load": 80 }
  backend-servers/
    node-1 → { "host": "10.0.3.10", "port": 8200, "load": 120 }
```

### Sentry (日志和错误追踪)

**用途**:
- 实时错误追踪
- 性能监控
- 用户行为追踪
- 告警通知

## 技术选型

### 序列化
- **MemoryPack** - 高性能二进制序列化
- **System.Text.Json** - HTTP API JSON 序列化

### 传输协议
- **TCP** - 可靠传输（GameServer, BattleServer）
- **KCP** - 低延迟传输（战斗场景）
- **HTTP/HTTPS** - 登录服务器

### 认证授权
- **JWT** - 无状态身份认证
- **OAuth2** - 第三方登录

### 数据库
- **MongoDB** - 文档数据库，适合游戏数据存储
- **MongoDB.Driver** - 官方 C# 驱动

### 服务发现
- **Consul** - HashiCorp Consul 官方 .NET 客户端
- **健康检查** - ASP.NET Core Health Checks 集成 Consul

### 日志
- **Sentry.AspNetCore** - Sentry SDK
- **Microsoft.Extensions.Logging** - 统一日志接口

## 数据流

### 登录流程

```
1. Unity 客户端 → LoginServer (HTTP POST /api/auth/login)
   - 请求: { "provider": "google", "token": "google_id_token" }

2. LoginServer → 验证第三方 Token

3. LoginServer → MongoDB (查询/创建账户)

4. LoginServer → Consul (获取可用 GameServer)

5. LoginServer → Unity 客户端
   - 响应: {
       "jwt": "eyJ...",
       "gameServer": { "host": "10.0.1.10", "port": 8080 }
     }

6. Unity 客户端 → GameServer (PulseRPC 连接，携带 JWT)

7. GameServer → 验证 JWT → 建立会话
```

### 战斗流程

```
1. Unity 客户端 → GameServer (请求匹配)

2. GameServer → BackendServer.MatchmakingService (匹配请求)

3. MatchmakingService → 匹配成功，创建战斗房间

4. BackendServer → BattleServer (创建 BattleRoom)

5. BattleServer → GameServer → Unity 客户端 (战斗开始通知)

6. Unity 客户端 → BattleServer (战斗操作)

7. BattleServer → Unity 客户端 (状态同步)

8. BattleServer → MongoDB (保存战斗记录)
```

## 项目结构

```
DistributedGameApp/
├── src/
│   ├── DistributedGameApp.Shared/
│   │   ├── Domain/                       # 领域模型
│   │   │   ├── Accounts/
│   │   │   │   ├── Account.cs
│   │   │   │   └── LoginRequest.cs
│   │   │   ├── Characters/
│   │   │   │   ├── Character.cs
│   │   │   │   └── CharacterCreateRequest.cs
│   │   │   ├── Battles/
│   │   │   │   ├── BattleRoom.cs
│   │   │   │   └── BattleAction.cs
│   │   │   ├── Social/
│   │   │   │   ├── Friend.cs
│   │   │   │   └── ChatMessage.cs
│   │   │   ├── Guilds/
│   │   │   │   ├── Guild.cs
│   │   │   │   └── GuildMember.cs
│   │   │   └── Leaderboards/
│   │   │       └── LeaderboardEntry.cs
│   │   ├── Hubs/                         # PulseRPC Hub 接口
│   │   │   ├── IGameHub.cs
│   │   │   ├── IBattleHub.cs
│   │   │   ├── ISocialHub.cs
│   │   │   ├── IGuildHub.cs
│   │   │   └── IMatchmakingHub.cs
│   │   └── Receivers/                    # PulseRPC Receiver 接口
│   │       ├── IGameReceiver.cs
│   │       ├── IBattleReceiver.cs
│   │       └── ISocialReceiver.cs
│   │
│   ├── DistributedGameApp.Infrastructure/
│   │   ├── MongoDB/
│   │   │   ├── MongoDbContext.cs
│   │   │   ├── Repositories/
│   │   │   │   ├── AccountRepository.cs
│   │   │   │   ├── CharacterRepository.cs
│   │   │   │   └── GuildRepository.cs
│   │   │   └── Extensions/
│   │   │       └── MongoDbServiceCollectionExtensions.cs
│   │   ├── Consul/
│   │   │   ├── ConsulServiceRegistry.cs
│   │   │   ├── ConsulServiceDiscovery.cs
│   │   │   └── Extensions/
│   │   │       └── ConsulServiceCollectionExtensions.cs
│   │   └── Sentry/
│   │       └── Extensions/
│   │           └── SentryServiceCollectionExtensions.cs
│   │
│   ├── DistributedGameApp.LoginServer/
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs
│   │   │   └── ServerController.cs
│   │   ├── Services/
│   │   │   ├── OAuth2Service.cs
│   │   │   └── JwtService.cs
│   │   ├── appsettings.json
│   │   └── Program.cs
│   │
│   ├── DistributedGameApp.GameServer/
│   │   ├── Services/
│   │   │   ├── PlayerSessionService.cs
│   │   │   ├── CharacterService.cs
│   │   │   ├── InventoryService.cs
│   │   │   └── QuestService.cs
│   │   ├── appsettings.json
│   │   └── Program.cs
│   │
│   ├── DistributedGameApp.BattleServer/
│   │   ├── Services/
│   │   │   ├── BattleRoomService.cs
│   │   │   ├── BattleLogicService.cs
│   │   │   └── SkillService.cs
│   │   ├── appsettings.json
│   │   └── Program.cs
│   │
│   ├── DistributedGameApp.BackendServer/
│   │   ├── Services/
│   │   │   ├── LeaderboardService.cs
│   │   │   ├── SocialService.cs
│   │   │   ├── GuildService.cs
│   │   │   ├── MatchmakingService.cs
│   │   │   ├── MailService.cs
│   │   │   └── ActivityService.cs
│   │   ├── appsettings.json
│   │   └── Program.cs
│   │
│   └── DistributedGameApp.Client/
│       ├── UnityClient/
│       │   └── GameClient.cs
│       └── Program.cs
│
├── docker/
│   ├── docker-compose.yml
│   ├── mongodb/
│   │   └── init.js
│   └── consul/
│       └── config.json
│
├── docs/
│   ├── ARCHITECTURE.md                 # 本文档
│   ├── DEPLOYMENT.md                   # 部署指南
│   ├── API.md                          # API 文档
│   └── DATABASE_SCHEMA.md              # 数据库设计
│
├── scripts/
│   ├── init-mongodb.sh
│   ├── init-consul.sh
│   └── start-all.sh
│
└── Directory.Packages.props
```

## 配置示例

### LoginServer appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017",
    "Consul": "http://localhost:8500"
  },
  "Sentry": {
    "Dsn": "https://your-sentry-dsn",
    "Environment": "production"
  },
  "OAuth2": {
    "Google": {
      "ClientId": "your-google-client-id",
      "ClientSecret": "your-google-client-secret"
    },
    "Facebook": {
      "AppId": "your-facebook-app-id",
      "AppSecret": "your-facebook-app-secret"
    }
  },
  "JWT": {
    "SecretKey": "your-super-secret-key-min-32-chars",
    "Issuer": "DistributedGameApp",
    "Audience": "GameClients",
    "ExpirationMinutes": 60
  }
}
```

### GameServer appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "ServerConfiguration": {
    "NodeId": 1,
    "NodeName": "GameServer-1",
    "ServiceType": "GameServer",
    "TcpPort": 8080,
    "KcpPort": 8081,
    "MaxConnections": 5000
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017",
    "Consul": "http://localhost:8500"
  },
  "Sentry": {
    "Dsn": "https://your-sentry-dsn",
    "Environment": "production"
  },
  "ServiceDiscovery": {
    "RegisterInterval": 10,
    "HeartbeatInterval": 5,
    "Ttl": 15
  }
}
```

## 扩展性

### 水平扩展

每种服务器类型都可以独立扩展：

```bash
# 启动多个 GameServer 实例
docker-compose up --scale game-server=3

# 启动多个 BattleServer 实例
docker-compose up --scale battle-server=5

# 启动多个 BackendServer 实例
docker-compose up --scale backend-server=2
```

### 负载均衡

- **LoginServer**: 使用 Nginx/HAProxy 进行负载均衡
- **GameServer**: 客户端从 Consul 获取最低负载的服务器
- **BattleServer**: 通过一致性哈希分配战斗房间
- **BackendServer**: 通过服务名称和 ServiceId 路由

## 性能指标

### 预期性能

- **LoginServer**: 1000+ 登录/秒
- **GameServer**: 5000+ 并发连接/节点
- **BattleServer**: 500+ 战斗房间/节点
- **消息延迟**: P99 < 10ms
- **吞吐量**: 100K+ 消息/秒/节点

## 监控和告警

### 关键指标

- 在线玩家数
- 服务器 CPU/内存使用率
- 消息处理延迟
- 错误率
- 数据库响应时间

### Sentry 集成

- 自动捕获未处理异常
- 性能追踪（APM）
- 用户操作追踪
- 实时告警

## 安全考虑

1. **传输加密**: 使用 TLS/SSL
2. **数据加密**: 敏感数据（密码、支付信息）加密存储
3. **JWT 验证**: 所有 PulseRPC 连接验证 JWT
4. **防刷**: 请求频率限制
5. **SQL 注入防护**: 使用参数化查询
6. **XSS 防护**: 输入验证和输出编码

## 下一步

1. 实现共享层（Domain Models, Hubs, Receivers）
2. 实现基础设施层（MongoDB, Consul, Sentry）
3. 实现 LoginServer
4. 实现 GameServer
5. 实现 BattleServer
6. 实现 BackendServer
7. 创建 Docker Compose 配置
8. 编写部署文档
