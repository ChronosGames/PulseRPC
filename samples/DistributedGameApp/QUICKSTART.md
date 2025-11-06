# DistributedGameApp 快速开始指南

## 概述

这是一个生产级的分布式游戏服务器示例，展示了如何使用 PulseRPC 框架构建一个完整的游戏后端系统。

## 系统要求

- .NET 9.0 SDK
- Docker Desktop（用于运行 MongoDB 和 Consul）
- Visual Studio 2022 或 Rider（可选）

## 快速开始

### 第1步：启动基础设施

使用 Docker Compose 启动 MongoDB 和 Consul：

```bash
cd docker
docker-compose up -d
```

验证服务是否启动成功：

```bash
# 检查容器状态
docker-compose ps

# 测试 MongoDB 连接
docker exec -it distributedgame-mongodb mongosh -u admin -p password123

# 测试 Consul 连接
curl http://localhost:8500/v1/status/leader
```

### 第2步：配置环境

#### MongoDB 配置

默认配置：
- 地址：localhost:27017
- 用户名：admin
- 密码：password123
- 数据库：game_accounts, game_characters, game_social, game_guilds, game_battles

#### Consul 配置

默认配置：
- HTTP API：localhost:8500
- DNS：localhost:8600
- 用于服务注册、发现和健康检查

### 第3步：构建项目

```bash
# 在项目根目录
cd samples/DistributedGameApp

# 还原依赖
dotnet restore

# 构建所有项目
dotnet build
```

### 第4步：运行服务器（按顺序）

#### 4.1 启动 LoginServer（HTTP 登录服务器）

```bash
cd src/DistributedGameApp.LoginServer
dotnet run
```

LoginServer 将在 http://localhost:5000 启动。

API 端点：
- POST http://localhost:5000/api/auth/login - 登录
- POST http://localhost:5000/api/auth/register - 注册
- GET http://localhost:5000/api/server/list - 获取服务器列表
- Swagger UI: http://localhost:5000/swagger

#### 4.2 启动 GameServer（游戏网关服务器）

```bash
# 打开新终端
cd src/DistributedGameApp.GameServer
dotnet run
```

GameServer 将在以下端口启动：
- TCP: 8080（客户端连接）
- KCP: 8081（低延迟连接）

#### 4.3 启动 BattleServer（战斗服务器）

```bash
# 打开新终端
cd src/DistributedGameApp.BattleServer
dotnet run
```

BattleServer 将在以下端口启动：
- TCP: 8100
- KCP: 8101

#### 4.4 启动 BackendServer（后台服务器）

```bash
# 打开新终端
cd src/DistributedGameApp.BackendServer
dotnet run
```

BackendServer 将在 TCP: 8200 启动。

### 第5步：测试系统

#### 使用 Unity 客户端

```csharp
// 1. 登录到 LoginServer
var loginClient = new HttpClient();
var loginRequest = new LoginRequest
{
    Provider = "guest",
    IdToken = "guest-token"
};

var response = await loginClient.PostAsJsonAsync(
    "http://localhost:5000/api/auth/login",
    loginRequest
);

var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();

// 2. 连接到 GameServer
var client = new PulseClient();
await client.ConnectAsync($"tcp://{loginResponse.GameServers[0].Host}:{loginResponse.GameServers[0].TcpPort}");

// 3. 设置 JWT Token（在连接头中）
client.SetAuthToken(loginResponse.JwtToken);

// 4. 调用 GameHub 方法
var gameHub = client.GetProxy<IGameHub>();
var characters = await gameHub.GetCharactersAsync();

// 5. 创建角色
var createRequest = new CreateCharacterRequest
{
    Name = "MyHero",
    Class = "Warrior"
};
var createResponse = await gameHub.CreateCharacterAsync(createRequest);

// 6. 选择角色进入游戏
var character = await gameHub.SelectCharacterAsync(createResponse.Character.CharacterId);
```

#### 使用 REST API 测试（Postman/curl）

```bash
# 登录
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "provider": "guest",
    "idToken": "guest-token"
  }'

# 获取服务器列表
curl -X GET http://localhost:5000/api/server/list
```

## 配置说明

### LoginServer 配置 (appsettings.json)

```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://admin:password123@localhost:27017",
    "Consul": "http://localhost:8500"
  },
  "JWT": {
    "SecretKey": "your-super-secret-key-min-32-chars-long!",
    "Issuer": "DistributedGameApp",
    "Audience": "GameClients",
    "ExpirationMinutes": 60
  },
  "OAuth2": {
    "Google": {
      "ClientId": "your-google-client-id",
      "ClientSecret": "your-google-client-secret"
    }
  }
}
```

### GameServer 配置 (appsettings.json)

```json
{
  "ServerConfiguration": {
    "NodeId": 1,
    "NodeName": "GameServer-1",
    "ServiceType": "GameServer",
    "TcpPort": 8080,
    "KcpPort": 8081,
    "MaxConnections": 5000
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://admin:password123@localhost:27017",
    "Consul": "http://localhost:8500"
  }
}
```

## 架构图

```
┌──────────────┐
│Unity 客户端  │
└──────┬───────┘
       │
       │ HTTP (登录)
       ▼
┌──────────────┐          ┌──────────────┐
│LoginServer   │          │Consul        │
│Port: 5000    │◄────────►│Port: 8500    │
└──────────────┘          └──────────────┘
       │
       │ 返回 JWT + GameServer 地址
       │
       ▼
┌──────────────┐          ┌──────────────┐
│GameServer    │◄────────►│BattleServer  │
│Port: 8080/81 │          │Port: 8100/01 │
└──────┬───────┘          └──────────────┘
       │
       │
       ▼
┌──────────────┐          ┌──────────────┐
│BackendServer │          │MongoDB       │
│Port: 8200    │◄────────►│Port: 27017   │
└──────────────┘          └──────────────┘
```

## 功能特性

### 已实现

- ✅ 完整的架构设计
- ✅ 领域模型定义（Accounts, Characters, Battles, Social, Guilds）
- ✅ Hub 和 Receiver 接口
- ✅ Docker Compose 基础设施配置
- ✅ 项目结构和依赖管理

### 待实现（需要继续开发）

- ⏳ LoginServer 实现（JWT + OAuth2）
- ⏳ GameServer 服务实现
- ⏳ BattleServer 服务实现
- ⏳ BackendServer 服务实现
- ⏳ MongoDB Repository 实现
- ⏳ etcd 服务注册与发现
- ⏳ Sentry 集成

## 开发指南

### 添加新的服务

1. 在 Shared 层定义接口和消息类型
2. 在对应的 Server 项目中实现服务
3. 注册服务到 DI 容器
4. 更新 appsettings.json 配置

### 添加新的 Hub 方法

```csharp
// 1. 在 Shared/Hubs/IGameHub.cs 添加方法
public interface IGameHub : IPulseHub
{
    Task<MyResponse> MyNewMethodAsync(MyRequest request);
}

// 2. 在 GameServer/Services/MyService.cs 实现
public class MyService : IGameHub, IPulseService
{
    public async Task<MyResponse> MyNewMethodAsync(MyRequest request)
    {
        // 实现逻辑
    }
}
```

### 数据库访问

```csharp
// 使用 Repository 模式
public class CharacterRepository
{
    private readonly IMongoCollection<Character> _collection;

    public async Task<Character?> GetByIdAsync(string characterId)
    {
        return await _collection.Find(c => c.CharacterId == characterId)
            .FirstOrDefaultAsync();
    }
}
```

## 故障排除

### MongoDB 连接失败

```bash
# 检查 MongoDB 是否运行
docker ps | grep mongodb

# 查看 MongoDB 日志
docker logs distributedgame-mongodb

# 重启 MongoDB
docker-compose restart mongodb
```

### Consul 连接失败

```bash
# 检查 Consul 是否运行
docker ps | grep consul

# 查看 Consul 日志
docker logs distributedgame-consul

# 测试 Consul 连接
curl http://localhost:8500/v1/status/leader

# 访问 Consul UI
# 浏览器打开: http://localhost:8500
```

### 端口冲突

如果端口被占用，修改 docker-compose.yml 或 appsettings.json 中的端口配置。

## 性能监控

- 使用 Sentry 进行错误追踪
- 使用 Consul 监控服务健康状态
- 使用 MongoDB 性能分析工具

## 下一步

1. 阅读完整架构文档：`docs/ARCHITECTURE.md`
2. 查看实施总结：`docs/IMPLEMENTATION_SUMMARY.md`
3. 开始实现你的游戏逻辑！

## 相关资源

- [PulseRPC 文档](../../README.md)
- [MongoDB 文档](https://www.mongodb.com/docs/)
- [Consul 文档](https://www.consul.io/docs/)
- [Unity PulseRPC 集成示例](../ChatApp/)

## 许可证

MIT License
