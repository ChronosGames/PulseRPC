# GameApp 技术架构文档

## 概述

GameApp 是一个基于微服务架构的现代游戏服务端系统，采用 .NET 9、PulseRPC、MongoDB、Redis 等技术栈，提供高性能、高可用的游戏服务。

## 系统架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                        客户端层                                  │
├─────────────────────────────────────────────────────────────────┤
│  Unity 客户端  │  Web 客户端  │  移动端客户端  │  第三方客户端    │
└─────────────────────────────────────────────────────────────────┘
                                 │
                              负载均衡
                                 │
┌─────────────────────────────────────────────────────────────────┐
│                        服务网关层                                │
├─────────────────────────────────────────────────────────────────┤
│               Nginx / HAProxy / Kong                           │
└─────────────────────────────────────────────────────────────────┘
                                 │
┌─────────────────────────────────────────────────────────────────┐
│                        应用服务层                                │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────┐  ┌──────────────┐  ┌──────────────┐              │
│ │ AuthServer  │  │ GameServer   │  │ BattleServer │              │
│ │ (HTTP API)  │  │ (PulseRPC)   │  │ (PulseRPC)   │              │
│ │ Port: 5000  │  │ TCP: 7000    │  │ TCP: 8000    │              │
│ │             │  │ KCP: 7001    │  │ KCP: 8001    │              │
│ └─────────────┘  └──────────────┘  └──────────────┘              │
└─────────────────────────────────────────────────────────────────┘
                                 │
┌─────────────────────────────────────────────────────────────────┐
│                        中间件层                                  │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────┐  ┌──────────────┐  ┌──────────────┐              │
│ │   Redis     │  │    Consul    │  │   Message    │              │
│ │   缓存      │  │   服务发现    │  │    Queue     │              │
│ │             │  │              │  │              │              │
│ └─────────────┘  └──────────────┘  └──────────────┘              │
└─────────────────────────────────────────────────────────────────┘
                                 │
┌─────────────────────────────────────────────────────────────────┐
│                        数据存储层                                │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────┐  ┌──────────────┐  ┌──────────────┐              │
│ │  MongoDB    │  │  Elasticsearch│  │  File Store  │              │
│ │  主数据库    │  │   日志搜索    │  │   文件存储   │              │
│ └─────────────┘  └──────────────┘  └──────────────┘              │
└─────────────────────────────────────────────────────────────────┘
                                 │
┌─────────────────────────────────────────────────────────────────┐
│                        监控运维层                                │
├─────────────────────────────────────────────────────────────────┤
│ ┌─────────────┐  ┌──────────────┐  ┌──────────────┐              │
│ │ Prometheus  │  │    Grafana   │  │     ELK      │              │
│ │  指标监控   │  │   数据可视化  │  │   日志分析   │              │
│ └─────────────┘  └──────────────┘  └──────────────┘              │
└─────────────────────────────────────────────────────────────────┘
```

## 核心组件

### 1. AuthServer (认证服务)

**技术栈**: ASP.NET Core 9.0, HTTP REST API, JWT

**职责**:
- 用户注册、登录、认证
- JWT 令牌管理和验证
- 游戏区服管理
- 用户权限控制
- 性能监控和告警

**特性**:
- 支持多种认证方式（用户名/邮箱）
- JWT 令牌自动刷新机制
- 账户安全保护（失败次数限制、账户锁定）
- 分布式会话管理
- 实时性能监控

### 2. GameServer (游戏服务)

**技术栈**: .NET 9, PulseRPC, TCP/KCP, MemoryPack

**职责**:
- 玩家数据管理
- 游戏世界交互
- 实时事件推送
- 负载均衡
- 游戏状态同步

**通信协议**:
- **TCP通道**: 可靠消息传输（玩家数据、交易、重要操作）
- **KCP通道**: 低延迟传输（实时位置、聊天、通知）

### 3. BattleServer (战斗服务)

**技术栈**: .NET 9, PulseRPC, TCP/KCP, MemoryPack

**职责**:
- 战斗匹配系统
- 实时战斗逻辑
- 技能系统管理
- 战斗数据统计
- 反作弊检测

**通信协议**:
- **KCP通道**: 实时战斗数据（位置、技能、伤害）
- **TCP通道**: 重要战斗事件（结算、奖励、统计）

## 数据架构

### 数据库设计

#### MongoDB 集合结构

```javascript
// 用户集合
users: {
  _id: ObjectId,
  username: string,
  email: string,
  passwordHash: string,
  profile: {
    nickname: string,
    avatar: string,
    level: number,
    experience: number
  },
  security: {
    failedLogins: number,
    lockedUntil: Date,
    lastLoginAt: Date,
    loginHistory: Array
  },
  createdAt: Date,
  updatedAt: Date
}

// 玩家数据集合
players: {
  _id: ObjectId,
  userId: ObjectId,
  zoneId: string,
  character: {
    name: string,
    class: string,
    level: number,
    experience: number,
    attributes: object
  },
  inventory: {
    items: Array,
    equipment: object,
    currency: object
  },
  position: {
    mapId: string,
    x: number,
    y: number,
    z: number
  },
  stats: object,
  createdAt: Date,
  updatedAt: Date
}

// 战斗数据集合
battles: {
  _id: ObjectId,
  battleId: string,
  participants: Array,
  result: object,
  statistics: {
    duration: number,
    playerStats: object
  },
  createdAt: Date,
  completedAt: Date
}
```

#### 索引策略

```javascript
// 用户索引
db.users.createIndex({ "username": 1 }, { unique: true })
db.users.createIndex({ "email": 1 }, { unique: true })
db.users.createIndex({ "security.lastLoginAt": -1 })

// 玩家索引
db.players.createIndex({ "userId": 1, "zoneId": 1 })
db.players.createIndex({ "character.name": 1, "zoneId": 1 }, { unique: true })
db.players.createIndex({ "position.mapId": 1 })

// 战斗索引
db.battles.createIndex({ "battleId": 1 }, { unique: true })
db.battles.createIndex({ "participants.playerId": 1 })
db.battles.createIndex({ "createdAt": -1 })
```

### Redis 缓存策略

```
# 会话缓存
session:{userId}:{sessionId} -> {accessToken, refreshToken, expiresAt}
TTL: 1小时

# 用户缓存
user:{userId} -> {basic user info}
TTL: 30分钟

# 玩家在线状态
online_players:{zoneId} -> Set<playerId>
TTL: 5分钟

# 区服状态
zone:{zoneId} -> {currentPlayers, maxPlayers, status}
TTL: 1分钟

# 战斗房间
battle_room:{battleId} -> {participants, status, created_at}
TTL: 1小时

# 性能指标
performance:metric:{name}:{hour} -> List<{value, timestamp}>
TTL: 24小时
```

## 网络通信

### HTTP REST API (AuthServer)

```
认证流程:
1. 客户端 -> AuthServer: POST /api/auth/login
2. AuthServer -> 数据库: 验证用户凭据
3. AuthServer -> Redis: 存储会话信息
4. AuthServer -> 客户端: 返回 JWT Token
```

### PulseRPC 通信 (GameServer/BattleServer)

```
连接流程:
1. 客户端 -> AuthServer: 获取游戏票据
2. 客户端 -> GameServer: TCP/KCP 连接
3. 客户端 -> GameServer: 票据验证
4. GameServer -> 客户端: 连接确认

消息流程:
1. 客户端调用: await gameService.GetPlayerInfoAsync()
2. 序列化: MemoryPack 序列化请求
3. 传输: TCP 可靠传输
4. 服务端处理: 业务逻辑处理
5. 响应: MemoryPack 序列化响应
6. 客户端接收: 反序列化响应
```

## 安全架构

### 认证与授权

```
1. JWT 令牌机制
   - HS256 签名算法
   - 包含用户 ID、角色、权限声明
   - 1小时有效期，支持自动刷新

2. 游戏票据机制
   - 时效性票据（5分钟有效期）
   - 一次性使用
   - 包含区服信息和权限

3. 权限控制
   - 基于角色的访问控制（RBAC）
   - API 级别权限验证
   - 资源级别权限检查
```

### 数据安全

```
1. 传输加密
   - HTTPS/TLS 1.3 用于 HTTP API
   - PulseRPC 支持 TLS 加密
   - 敏感数据端到端加密

2. 存储安全
   - 密码使用 BCrypt 哈希
   - 敏感数据字段加密
   - 数据库访问权限控制

3. 防护措施
   - SQL 注入防护
   - XSS 攻击防护
   - CSRF 令牌验证
   - 请求频率限制
```

## 性能架构

### 性能优化策略

```
1. 缓存策略
   - 多级缓存：内存 -> Redis -> 数据库
   - 缓存预热和失效策略
   - 分布式缓存一致性

2. 数据库优化
   - 索引优化和查询优化
   - 读写分离
   - 分库分表策略
   - 连接池管理

3. 应用优化
   - 异步编程模型
   - 对象池和内存管理
   - JIT 编译优化
   - 垃圾回收调优
```

### 性能监控

```
1. 实时监控
   - 响应时间监控
   - 吞吐量监控
   - 错误率监控
   - 资源使用监控

2. 性能分析
   - APM 工具集成
   - 分布式链路追踪
   - 性能瓶颈分析
   - 容量规划

3. 告警机制
   - 基于阈值的告警
   - 智能异常检测
   - 多通道通知
   - 告警降噪
```

## 可扩展性架构

### 水平扩展

```
1. 服务扩展
   - 无状态服务设计
   - 容器化部署
   - 自动扩缩容
   - 负载均衡

2. 数据库扩展
   - MongoDB 分片集群
   - Redis 集群模式
   - 读写分离
   - 数据分区策略

3. 缓存扩展
   - 分布式缓存
   - 缓存分片
   - 缓存一致性哈希
```

### 高可用性

```
1. 服务容错
   - 熔断器模式
   - 重试机制
   - 降级策略
   - 超时控制

2. 数据冗余
   - 数据库主从复制
   - 多数据中心备份
   - 自动故障转移
   - 数据一致性保证

3. 监控告警
   - 健康检查
   - 故障自愈
   - 实时告警
   - 运维自动化
```

## 技术选型

### 后端技术栈

| 组件 | 技术选型 | 版本 | 选型理由 |
|------|----------|------|----------|
| 运行时 | .NET | 9.0 | 高性能、跨平台、现代化 |
| Web框架 | ASP.NET Core | 9.0 | 企业级、高性能、丰富生态 |
| RPC框架 | PulseRPC | 0.6.2 | 高性能、类型安全、现代化 |
| 序列化 | MemoryPack | 最新 | 极高性能、类型安全 |
| 数据库 | MongoDB | 7.0+ | 文档型、高可用、易扩展 |
| 缓存 | Redis | 7.0+ | 高性能、丰富数据结构 |
| 服务发现 | Consul | 1.15+ | 服务网格、配置管理 |

### 前端技术栈

| 组件 | 技术选型 | 版本 | 选型理由 |
|------|----------|------|----------|
| 游戏引擎 | Unity | 2022.3 LTS | 跨平台、生态丰富 |
| 网络通信 | UnityWebRequest | 内置 | HTTP API 调用 |
| RPC客户端 | PulseRPC.Client | 0.6.2 | 与后端对应 |
| JSON序列化 | Unity JsonUtility | 内置 | 轻量级、性能好 |

### 基础设施

| 组件 | 技术选型 | 版本 | 选型理由 |
|------|----------|------|----------|
| 容器化 | Docker | 24.0+ | 标准化部署 |
| 编排 | Docker Compose | 2.0+ | 本地开发环境 |
| 负载均衡 | Nginx | 1.24+ | 高性能、稳定 |
| 监控 | Prometheus | 2.45+ | 开源、生态丰富 |
| 可视化 | Grafana | 10.0+ | 强大的可视化 |
| 日志 | ELK Stack | 8.8+ | 日志收集分析 |

## 部署架构

### 开发环境

```yaml
# docker-compose.yml
services:
  mongodb-dev:
    image: mongo:7.0
    environment:
      MONGO_INITDB_ROOT_USERNAME: admin
      MONGO_INITDB_ROOT_PASSWORD: dev_password
    ports:
      - "27017:27017"

  redis-dev:
    image: redis:7.0-alpine
    command: redis-server --requirepass dev_password
    ports:
      - "6379:6379"

  consul-dev:
    image: hashicorp/consul:1.15
    ports:
      - "8500:8500"
    command: agent -server -ui -node=server-1 -bootstrap-expect=1 -client=0.0.0.0

  authserver-dev:
    build: ./src/GameApp.AuthServer
    ports:
      - "5000:5000"
    depends_on:
      - mongodb-dev
      - redis-dev
      - consul-dev
```

### 生产环境

```
┌─────────────────┐
│   Load Balancer │
│   (Nginx/HAProxy)│
└─────────────────┘
         │
┌─────────────────┐
│  AuthServer     │
│  (Multiple)     │
└─────────────────┘
         │
┌─────────────────┐
│  GameServer     │
│  (Cluster)      │
└─────────────────┘
         │
┌─────────────────┐
│  BattleServer   │
│  (Cluster)      │
└─────────────────┘
         │
┌─────────────────┐
│ MongoDB Cluster │
│ Redis Cluster   │
│ Consul Cluster  │
└─────────────────┘
```

## 最佳实践

### 代码规范

```csharp
// 服务接口定义
[Channel("TcpChannel")]
public interface IPlayerService : IPulseService
{
    Task<PlayerInfoResponse> GetPlayerInfoAsync(string playerId);
    Task<UpdatePlayerResponse> UpdatePlayerAsync(UpdatePlayerRequest request);
}

// 事件处理接口
public interface IPlayerEvents : IPulseEventHandler
{
    Task OnPlayerLevelUpAsync(PlayerLevelUpEvent eventData);
    Task OnPlayerLogoutAsync(PlayerLogoutEvent eventData);
}

// DTO 定义
[MemoryPackable]
public partial class PlayerInfoResponse
{
    public string PlayerId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public int Level { get; set; }
    public long Experience { get; set; }
}
```

### 错误处理

```csharp
// 全局异常处理中间件
public class ExceptionHandlingMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (UnauthorizedException ex)
        {
            await HandleUnauthorizedExceptionAsync(context, ex);
        }
        catch (Exception ex)
        {
            await HandleGenericExceptionAsync(context, ex);
        }
    }
}
```

### 配置管理

```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://admin:password@localhost:27017/gameapp?authSource=admin",
    "Redis": "localhost:6379"
  },
  "JwtOptions": {
    "SecretKey": "${JWT_SECRET_KEY}",
    "Issuer": "GameAppAuthServer",
    "Audience": "GameAppClients",
    "AccessTokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 7
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## 总结

GameApp 采用现代化的微服务架构，通过合理的技术选型和架构设计，实现了高性能、高可用、易扩展的游戏服务端系统。系统具备完善的监控、日志、安全和运维能力，能够满足大规模游戏应用的需求。
