# PulseRPC 框架产品需求文档 (PRD)

**版本**: 1.0.0
**最后更新**: 2025-12-31
**状态**: 分析完成

---

## 1. 产品概述

### 1.1 产品定位

PulseRPC 是一个高性能、基于 Source Generator 的 .NET RPC 框架，专为分布式游戏服务器和实时应用程序设计。框架通过编译时代码生成消除反射开销，实现零分配高性能通信。

### 1.2 核心价值主张

| 特性 | 描述 |
|------|------|
| **编译时性能优化** | 使用 Roslyn Source Generator 在编译时生成代理代码，消除运行时反射 |
| **高性能序列化** | 默认集成 MemoryPack，支持零拷贝序列化 |
| **多传输协议** | 支持 TCP、KCP（可靠 UDP）多种传输层 |
| **双向通信** | 支持服务端主动推送消息到客户端（IPulseReceiver） |
| **分布式架构** | 原生支持服务发现、负载均衡、集群通信 |

### 1.3 目标用户

- 分布式游戏服务器开发者
- 实时应用后端开发者
- 需要高性能 RPC 通信的 .NET 应用程序

---

## 2. 系统架构

### 2.1 整体架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                        PulseRPC 框架                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  ┌─────────────────────┐      ┌─────────────────────────────┐   │
│  │   PulseRPC.Client   │      │      PulseRPC.Server        │   │
│  │   (客户端库)         │◄────►│      (服务端库)              │   │
│  └──────────┬──────────┘      └──────────────┬──────────────┘   │
│             │                                  │                   │
│  ┌──────────▼──────────┐      ┌──────────────▼──────────────┐   │
│  │ Client.SourceGen    │      │   Server.SourceGenerator    │   │
│  │ (客户端代码生成器)    │      │   (服务端代码生成器)         │   │
│  └─────────────────────┘      └─────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  PulseRPC.Abstractions                    │   │
│  │              (共享接口、特性、协议定义)                     │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                   Transport Layer                         │   │
│  │          TCP Transport  │  KCP Transport                  │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 项目模块结构

```
PulseRPC/
├── src/
│   ├── PulseRPC.Abstractions/          # 核心抽象和接口
│   ├── PulseRPC.Client/                 # 客户端运行时库
│   ├── PulseRPC.Client.SourceGenerator/ # 客户端代码生成器
│   ├── PulseRPC.Server/                 # 服务端运行时库
│   ├── PulseRPC.Server.SourceGenerator/ # 服务端代码生成器
│   ├── PulseRPC.Transport.Tcp/          # TCP 传输实现
│   ├── PulseRPC.Transport.Kcp/          # KCP 传输实现
│   └── PulseRPC.ServiceDiscovery.*/     # 服务发现实现
├── samples/
│   └── DistributedGameApp/              # 分布式游戏示例
└── tests/
    └── PulseRPC.Tests/                  # 单元测试
```

---

## 3. 客户端 (PulseRPC.Client)

### 3.1 核心接口

#### 3.1.1 IPulseClient

```csharp
/// <summary>
/// PulseRPC 客户端核心接口
/// </summary>
public interface IPulseClient : IAsyncDisposable, IDisposable
{
    /// <summary>客户端唯一标识</summary>
    string ClientId { get; }

    /// <summary>连接状态</summary>
    ConnectionState State { get; }

    /// <summary>当前连接的服务端地址</summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>连接状态变化事件</summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>建立连接</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开连接</summary>
    Task DisconnectAsync();

    /// <summary>获取服务Hub代理</summary>
    T GetHub<T>() where T : class, IPulseHub;

    /// <summary>注册事件接收器</summary>
    void RegisterReceiver<T>(T receiver) where T : class, IPulseReceiver;
}
```

#### 3.1.2 服务代理机制

客户端通过 Source Generator 自动生成服务代理类：

```csharp
// 用户定义：在任意类上标记需要生成的服务代理
[PulseClientGeneration(typeof(IGameHub))]
[PulseClientGeneration(typeof(IBattleHub))]
[PulseClientGeneration(typeof(IGameReceiver))]
public partial class GameClient { }

// 生成后可直接使用
var gameHub = client.GetHub<IGameHub>();
var result = await gameHub.LoginAsync(request);
```

### 3.2 Client Source Generator

#### 3.2.1 生成器架构

```
PulseRPC.Client.SourceGenerator/
├── ServiceProxyGenerator.cs              # 主生成器入口
├── Generators/
│   ├── ProtocolIdGenerator.cs            # 协议号生成
│   ├── ReceiverDispatcherGenerator.cs    # 接收器分发生成
│   ├── SmartEventHandlerGenerator.cs     # 事件处理器生成
│   ├── PulseClientExtensionsGenerator.cs # 客户端扩展方法
│   └── ClientChannelGenericExtensionsGenerator.cs
├── Models/
│   ├── ServiceModel.cs                   # 服务模型
│   ├── MethodModel.cs                    # 方法模型
│   └── ParameterModel.cs                 # 参数模型
└── Helpers/
    └── SourceGeneratorHelper.cs          # 辅助工具
```

#### 3.2.2 生成内容

| 生成文件 | 描述 |
|---------|------|
| `{Service}.Proxy.g.cs` | 服务代理实现类 |
| `ProtocolIdMapping.g.cs` | 协议号映射表 |
| `ReceiverDispatcher.g.cs` | 消息接收分发器 |
| `PulseClientExtensions.g.cs` | GetHub 扩展方法 |

#### 3.2.3 协议号生成策略

使用 FNV-1a 哈希算法基于方法签名生成唯一协议号：

```csharp
// 签名格式: {InterfaceFullName}.{MethodName}({ParamTypes})
var signature = $"{service.InterfaceFullName}.{method.MethodName}({params})";

// FNV-1a 哈希
const uint FnvPrime = 0x01000193;
const uint FnvOffsetBasis = 0x811C9DC5;
var hash = FnvOffsetBasis;
foreach (var c in signature)
{
    hash ^= c;
    hash *= FnvPrime;
}
return (ushort)(hash & 0xFFFF);
```

支持手动指定协议号：
```csharp
public interface IGameHub : IPulseHub
{
    [Protocol(0x1001)]  // 手动指定
    Task<LoginResponse> LoginAsync(LoginRequest request);

    // 自动生成协议号
    Task<CharacterInfo[]> GetCharacterListAsync();
}
```

### 3.3 客户端配置

```csharp
var client = new PulseClientBuilder()
    .WithClientId("game-client-001")
    .WithEndPoint("127.0.0.1", 5000)
    .WithTransport<TcpTransport>()
    .WithSerializer<MemoryPackSerializer>()
    .WithReconnection(options =>
    {
        options.MaxRetries = 5;
        options.InitialDelay = TimeSpan.FromSeconds(1);
        options.MaxDelay = TimeSpan.FromSeconds(30);
    })
    .Build();
```

---

## 4. 服务端 (PulseRPC.Server)

### 4.1 核心接口

#### 4.1.1 IPulseHub

```csharp
/// <summary>
/// 服务Hub基础接口 - 所有RPC服务必须实现此接口
/// </summary>
public interface IPulseHub
{
    // 标记接口，无方法定义
}
```

#### 4.1.2 IPulseReceiver

```csharp
/// <summary>
/// 客户端消息接收器接口 - 用于服务端推送消息
/// </summary>
public interface IPulseReceiver
{
    // 标记接口，无方法定义
}
```

#### 4.1.3 IPulseServer

```csharp
/// <summary>
/// PulseRPC 服务器核心接口
/// </summary>
public interface IPulseServer : IAsyncDisposable, IDisposable
{
    /// <summary>服务器唯一标识</summary>
    string ServerId { get; }

    /// <summary>当前运行状态</summary>
    ServerState State { get; }

    /// <summary>启动服务器</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>停止服务器</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>获取活动会话数量</summary>
    int ActiveSessionCount { get; }
}
```

### 4.2 Hub 实现模式

#### 4.2.1 基础 Hub 实现

```csharp
/// <summary>
/// 游戏服务 Hub 实现
/// </summary>
public class GameHub : PulseHubBase, IGameHub
{
    private readonly IServiceAccessor<CharacterService> _characterService;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        IServiceAccessor<CharacterService> characterService,
        ILogger<GameHub> logger)
    {
        _characterService = characterService;
        _logger = logger;
    }

    // 继承自 PulseHubBase 的上下文属性
    // - ConnectionId: 当前连接标识
    // - UserId: 认证后的用户标识
    // - IsAuthenticated: 是否已认证

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        // 业务逻辑实现
    }

    public async Task<CharacterInfo[]> GetCharacterListAsync()
    {
        return await _characterService.ExecuteWithUserId(
            (service, userId) => service.GetCharactersAsync(userId));
    }
}
```

#### 4.2.2 服务访问模式

```csharp
// Singleton 服务通过 IServiceAccessor 安全访问
public interface IServiceAccessor<TService> where TService : class
{
    /// <summary>执行服务方法</summary>
    Task<TResult> Execute<TResult>(Func<TService, Task<TResult>> action);

    /// <summary>带用户上下文执行服务方法</summary>
    Task<TResult> ExecuteWithUserId<TResult>(
        Func<TService, string, Task<TResult>> action);
}
```

### 4.3 Server Source Generator

#### 4.3.1 生成器架构

```
PulseRPC.Server.SourceGenerator/
├── PulseRPCSourceGenerator.cs            # 主生成器 (IIncrementalGenerator)
├── Analyzers/
│   └── ServiceAnalyzer.cs                # 服务接口分析
├── Generators/
│   ├── ServiceProxyGenerator.cs          # 服务代理生成
│   ├── RoutingTableGenerator.cs          # 路由表生成
│   ├── ProtocolIdGenerator.cs            # 协议号映射生成
│   ├── ResponseSerializerGenerator.cs    # 响应序列化器生成
│   └── ReceiverProxyGenerator.cs         # 接收器代理生成
├── Models/
│   ├── ServiceModel.cs                   # 服务元数据模型
│   ├── MethodModel.cs                    # 方法元数据模型
│   ├── ReceiverModel.cs                  # 接收器模型
│   └── AuthorizationModel.cs             # 授权模型
└── Helpers/
    └── AuthorizationHelper.cs            # 授权辅助
```

#### 4.3.2 生成触发机制

```csharp
// 方式: 实现 IPulseHub 接口自动识别
public interface IGameHub : IPulseHub { }
```

#### 4.3.3 生成内容

| 生成文件 | 描述 |
|---------|------|
| `{Service}.Proxy.g.cs` | 服务代理（反序列化、方法调用） |
| `ServiceRoutingTable.g.cs` | 全局路由表 |
| `ProtocolIdMapping.g.cs` | 协议号映射表 |
| `ResponseSerializers.g.cs` | 响应序列化器 |
| `{Receiver}.ReceiverProxy.g.cs` | 推送代理实现 |
| `ReceiverProtocolIdMapping.g.cs` | 接收器协议号映射 |
| `PulseReceiverServiceExtensions.g.cs` | DI 扩展方法 |

#### 4.3.4 路由表生成示例

```csharp
// 生成的 ServiceRoutingTable.g.cs
public static class ServiceRoutingTable
{
    private static readonly Dictionary<ProtocolId, Func<...>> _handlers = new()
    {
        [0x1001] = (ctx, buffer) => GameHubProxy.HandleLoginAsync(ctx, buffer),
        [0x1002] = (ctx, buffer) => GameHubProxy.HandleGetCharacterListAsync(ctx, buffer),
        // ...
    };

    public static async ValueTask<bool> RouteAsync(
        RequestContext ctx,
        ReadOnlyMemory<byte> buffer)
    {
        if (_handlers.TryGetValue(ctx.ProtocolId, out var handler))
        {
            await handler(ctx, buffer);
            return true;
        }
        return false;
    }
}
```

### 4.4 服务模型定义

```csharp
/// <summary>服务元数据模型</summary>
public sealed class ServiceModel
{
    public string InterfaceName { get; set; }
    public string InterfaceFullName { get; set; }
    public string Namespace { get; set; }
    public string ChannelName { get; set; }        // 通道名称
    public string? ServiceName { get; set; }       // 服务名称
    public List<MethodModel> Methods { get; set; }
    public AuthorizationModel? Authorization { get; set; }  // 授权信息
}

/// <summary>方法元数据模型</summary>
public sealed class MethodModel
{
    public string MethodName { get; set; }
    public string ReturnTypeName { get; set; }
    public List<ParameterModel> Parameters { get; set; }
    public ushort ProtocolId { get; set; }         // 协议号
    public string? ResponseTypeFullName { get; set; }
    public bool IsAsync { get; set; }
    public AuthorizationModel? Authorization { get; set; }
}

/// <summary>授权元数据模型</summary>
public sealed class AuthorizationModel
{
    public bool AllowAnonymous { get; set; }
    public string? AuthType { get; set; }    // Client, Service, Internal, Any
    public string? Role { get; set; }
    public string? Roles { get; set; }
    public string? Policy { get; set; }
    public string[]? Scopes { get; set; }
}
```

---

## 5. 消息推送机制 (IPulseReceiver)

### 5.1 接口定义

```csharp
/// <summary>
/// 游戏事件接收器 - 由客户端实现，服务端调用
/// </summary>
public interface IGameReceiver : IPulseReceiver
{
    Task OnMatchFoundAsync(MatchFoundNotification notification);
    Task OnServerNotificationAsync(string message);
    Task OnCharacterLevelUpAsync(CharacterInfo characterInfo);
    Task OnKickedAsync(string reason);
}
```

### 5.2 服务端推送

```csharp
// 服务端通过 IHubContext 推送消息
public class MatchmakingService
{
    private readonly IHubContext<IGameReceiver> _hubContext;

    public async Task NotifyMatchFoundAsync(string userId, MatchFoundNotification notification)
    {
        // 推送给指定用户
        await _hubContext.Clients.User(userId).OnMatchFoundAsync(notification);

        // 推送给用户组
        await _hubContext.Clients.Group("lobby").OnServerNotificationAsync("新匹配已创建");

        // 广播给所有连接
        await _hubContext.Clients.All.OnServerNotificationAsync("系统公告");
    }
}
```

### 5.3 客户端接收

```csharp
// 客户端实现接收器接口
public class GameEventHandler : IGameReceiver
{
    public Task OnMatchFoundAsync(MatchFoundNotification notification)
    {
        Console.WriteLine($"匹配成功: {notification.BattleId}");
        return Task.CompletedTask;
    }

    public Task OnServerNotificationAsync(string message)
    {
        Console.WriteLine($"[系统通知] {message}");
        return Task.CompletedTask;
    }
    // ...
}

// 注册接收器
client.RegisterReceiver<IGameReceiver>(new GameEventHandler());
```

---

## 6. 通道与多服务支持

### 6.1 通道特性

```csharp
/// <summary>
/// 指定服务所属通道
/// </summary>
[Channel("game", ServiceName = "GameService")]
public interface IGameHub : IPulseHub { }

[Channel("battle", ServiceName = "BattleService")]
public interface IBattleHub : IPulseHub { }

[Channel("backend", ServiceName = "BackendService")]
public interface IBackendHub : IPulseHub { }
```

### 6.2 MSBuild 配置

```xml
<!-- 在 .csproj 中配置服务端通道 -->
<PropertyGroup>
  <PulseRPC_ServerChannels>game;battle;backend</PulseRPC_ServerChannels>
</PropertyGroup>
```

---

## 7. 序列化与协议

### 7.1 消息格式

```
┌────────────────────────────────────────────────────┐
│                  消息帧格式                         │
├────────────────┬───────────────────────────────────┤
│ 消息长度 (4字节) │ VarInt 编码的总长度               │
├────────────────┼───────────────────────────────────┤
│ 消息类型 (1字节) │ Request/Response/Push/Heartbeat  │
├────────────────┼───────────────────────────────────┤
│ 协议号 (2字节)   │ ushort，方法唯一标识              │
├────────────────┼───────────────────────────────────┤
│ 请求ID (4字节)   │ 请求-响应匹配标识                 │
├────────────────┼───────────────────────────────────┤
│ 载荷            │ MemoryPack 序列化的参数/结果      │
└────────────────┴───────────────────────────────────┘
```

### 7.2 序列化约定

```csharp
// 消息类型需标记 [MemoryPackable]
[MemoryPackable]
public partial class LoginRequest
{
    public string? DeviceId { get; set; }
    public string? Ticket { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

[MemoryPackable]
public partial class LoginResponse
{
    public bool Success { get; set; }
    public string? PlayerId { get; set; }
    public string? AccessToken { get; set; }
    public int ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
```

---

## 8. 典型使用场景

### 8.1 分布式游戏架构示例

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Client    │────►│ LoginServer │────►│   MongoDB   │
│  (Unity)    │     │   (HTTP)    │     │             │
└──────┬──────┘     └─────────────┘     └─────────────┘
       │
       │ PulseRPC (TCP)
       ▼
┌──────────────────────────────────────────────────────┐
│                    GameServer                        │
│  ┌──────────┐  ┌───────────┐  ┌──────────────────┐  │
│  │ GameHub  │  │ PlayerHub │  │ GameReceiver     │  │
│  │          │  │           │  │ (推送到客户端)    │  │
│  └──────────┘  └───────────┘  └──────────────────┘  │
└──────────────────────┬───────────────────────────────┘
                       │ PulseRPC (内部通信)
       ┌───────────────┼───────────────┐
       ▼               ▼               ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ BackendServer│ │ BattleServer │ │ ChatServer   │
│ (匹配/社交)   │ │ (战斗逻辑)   │ │ (聊天系统)   │
└──────────────┘ └──────────────┘ └──────────────┘
```

### 8.2 客户端使用示例

```csharp
// 1. 定义客户端生成标记
[PulseClientGeneration(typeof(IGameHub))]
[PulseClientGeneration(typeof(IGameReceiver))]
public partial class GameClient { }

// 2. 创建并连接客户端
var client = new PulseClientBuilder()
    .WithEndPoint("game.server.com", 5000)
    .WithTransport<TcpTransport>()
    .Build();

await client.ConnectAsync();

// 3. 注册事件接收器
client.RegisterReceiver<IGameReceiver>(new GameEventHandler());

// 4. 调用服务方法
var gameHub = client.GetHub<IGameHub>();
var loginResult = await gameHub.LoginAsync(new LoginRequest { Ticket = token });

if (loginResult.Success)
{
    var characters = await gameHub.GetCharacterListAsync();
}
```

### 8.3 服务端使用示例

```csharp
// 1. 定义服务接口
public interface IGameHub : IPulseHub
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<CharacterInfo[]> GetCharacterListAsync();
}

// 2. 实现服务
public class GameHub : PulseHubBase, IGameHub
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        // 业务逻辑
    }
}

// 3. 配置服务器
var server = new PulseServerBuilder()
    .WithServerId("game-server-001")
    .WithEndPoint(IPAddress.Any, 5000)
    .WithTransport<TcpTransport>()
    .AddHub<IGameHub, GameHub>()
    .Build();

await server.StartAsync();
```

---

## 9. 性能优化特性

### 9.1 编译时优化

| 优化项 | 描述 |
|-------|------|
| **零反射** | 所有服务代理在编译时生成，无运行时反射 |
| **直接方法调用** | 生成的路由表直接调用目标方法 |
| **优化的序列化** | 预生成序列化器，避免运行时类型检查 |

### 9.2 运行时优化

| 优化项 | 描述 |
|-------|------|
| **内存池化** | `NetworkBufferPool` 实现缓冲区复用 |
| **引用计数** | `ReferenceCountedBuffer` 精确管理内存生命周期 |
| **异步管道** | 基于 `System.IO.Pipelines` 的高效 I/O |
| **消息队列** | 服务级队列隔离，支持优先级和并发控制 |

### 9.3 诊断信息

生成器提供编译时诊断：

| 诊断ID | 级别 | 描述 |
|--------|-----|------|
| PULSE001 | Info | 未找到 PulseService 接口 |
| PULSE002 | Warning | PulseServerGeneration 配置但未找到服务 |
| PULSE003 | Error | 协议号冲突 |
| PULSE100 | Info | 生成成功统计 |
| PULSE101 | Info | 接收器生成成功 |
| PULSE999 | Error | 生成失败 |

---

## 10. 扩展点

### 10.1 自定义传输层

```csharp
public interface ITransport
{
    Task<ITransportChannel> ConnectAsync(EndPoint endPoint, CancellationToken ct);
    Task<ITransportServer> ListenAsync(EndPoint endPoint, CancellationToken ct);
}
```

### 10.2 自定义序列化器

```csharp
public interface IPulseRPCSerializer
{
    void Serialize<T>(IBufferWriter<byte> writer, T value);
    T Deserialize<T>(ReadOnlySequence<byte> buffer);
}
```

### 10.3 服务发现集成

支持的服务发现提供程序：
- Consul
- Etcd
- Kubernetes

---

## 11. 版本兼容性

### 11.1 代码生成约定

| 生成器 | 目标语言版本 |
|-------|------------|
| Client.SourceGenerator | C# 9.0 及以下（Unity 兼容） |
| Server.SourceGenerator | C# 14.0 及以下 |

### 11.2 .NET 版本支持

- PulseRPC.Client: .NET Standard 2.0+, .NET 6+
- PulseRPC.Server: .NET 8+, .NET 9+

---

## 12. 安全特性

### 12.1 认证授权

```csharp
// 接口级授权
[Authorize(AuthType = "Client")]
public interface IGameHub : IPulseHub { }

// 方法级授权
public interface IAdminHub : IPulseHub
{
    [Authorize(Roles = "Admin,GM")]
    Task<bool> BanPlayerAsync(string playerId);

    [AllowAnonymous]
    Task<ServerStatus> GetStatusAsync();
}
```

### 12.2 会话管理

- 基于 ConnectionId 的会话跟踪
- JWT Token 认证支持
- 用户-连接映射（IUserConnectionMapping）

---

## 13. 总结

### 13.1 核心优势

1. **高性能**: 编译时代码生成，消除反射开销
2. **类型安全**: 强类型接口定义，编译时错误检查
3. **双向通信**: 原生支持服务端推送
4. **易于使用**: 声明式接口定义，自动生成代理
5. **可扩展**: 模块化设计，支持自定义传输和序列化

### 13.2 适用场景

- 实时游戏服务器
- 高频交易系统
- IoT 设备通信
- 微服务内部通信

### 13.3 技术栈

- .NET 8/9
- Roslyn Source Generator
- MemoryPack
- System.IO.Pipelines
- System.Threading.Channels

---

*本文档基于 PulseRPC 框架源代码分析生成*
