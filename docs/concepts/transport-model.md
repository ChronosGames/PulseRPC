# PulseRPC 传输层架构说明

## 概述

PulseRPC 是一个基于 TCP/KCP 的现代 RPC 框架，采用三层抽象架构设计，支持 .NET 和 Unity 平台。本文档详细描述了 PulseRPC 的传输层架构结构、各层职责以及核心设计原理，涵盖服务端和客户端的完整架构设计。

## 架构总览

PulseRPC 传输层采用三层抽象架构模式，每层职责明确，实现了从底层网络传输到高级业务功能的完整抽象：

### 服务端架构
```
┌─────────────────────────────────────────────────────────────┐
│                   应用层 (Application Layer)                 │
│                RPC 服务调用、Hub方法、业务逻辑处理                │
│              IClientSession, IClientSessionManager          │
├─────────────────────────────────────────────────────────────┤
│                    会话层 (Session Layer)                    │
│              认证管理、会话状态、属性存储、传输与应用的桥接           │
│           ISessionChannel, IServerChannel, 认证上下文         │
├─────────────────────────────────────────────────────────────┤
│                   传输层 (Transport Layer)                   │
│             协议抽象、连接管理、状态机、TCP/KCP具体实现             │
│         ITransportConnection, IServerTransport, 监听器       │
└─────────────────────────────────────────────────────────────┘
```

### 客户端架构
```
┌─────────────────────────────────────────────────────────────┐
│                   应用层 (Application Layer)                 │
│          IPulseClient、服务代理、事件处理器、连接池管理             │
│       IPulseClient, IConnectionRouter, IServiceDiscovery     │
├─────────────────────────────────────────────────────────────┤
│                    会话层 (Session Layer)                    │
│        连接上下文、状态管理、请求-响应映射、服务代理缓存              │
│        IConnectionContext, IConnection, SimpleConnection     │
├─────────────────────────────────────────────────────────────┤
│                   传输层 (Transport Layer)                   │
│             协议抽象、连接建立、数据传输、TCP/KCP客户端             │
│            IClientTransport, ITransportConnection            │
└─────────────────────────────────────────────────────────────┘
```

## 分层详细设计

### 1. 传输层 (Transport Layer)

#### 职责
- 提供协议无关的传输连接抽象
- 管理底层网络连接状态和生命周期
- 实现 TCP/KCP 等具体网络协议
- 处理连接建立、数据传输和连接关闭

#### 核心接口

##### 传输连接基础接口
```csharp
public interface ITransportConnection : IDisposable
{
    string ConnectionId { get; }
    ConnectionState State { get; }
    EndPoint RemoteEndPoint { get; }
    EndPoint LocalEndPoint { get; }
    DateTime ConnectedAt { get; }
    DateTime LastActivityAt { get; }
    TransportType TransportType { get; }
    bool IsConnected { get; }

    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);

    event EventHandler<ConnectionStateChangedEventArgs> StateChanged;
    event EventHandler<TransportDataEventArgs> DataReceived;
}
```

`SendAsync` 的完成语义是“本地传输已完成该帧的写出/交付给协议栈”：TCP 会等待底层 `NetworkStream.WriteAsync` 完成，KCP 会等待帧被 KCP 栈接收。它不表示远端业务方法已执行；需要业务执行结果时必须使用 Ask/响应或节点 wire v2 ACK。发送队列满时调用会等待、被取消或返回失败，不能把“成功进入队列”误报为发送完成。

当前 TCP/KCP wire 压缩和加密尚未实现，`TransportOptions.UseCompression` / `UseEncryption` 设为 `true` 会在创建传输时明确抛出 `NotSupportedException`。线路安全应由受信网络边界及 TLS/mTLS 层提供。

`BatchedTransport` 的 reader、周期刷新和释放排空属于同一可追踪生命周期；`DisposeAsync` 会等待所有已接受请求得到发送结果。背压支持 `Block`、`DropNewest` 和 `Reject`；`DropOldest` 因无法在当前目标框架上可靠通知被淘汰请求，仍为实验枚举值并在构造时 fail-fast。

客户端 `StopAsync` / `DisconnectAsync` 的 `graceful=false` 为真正的 abortive 路径：立即取消自动重连和通道后台任务，TCP 以 linger=0 关闭 socket，KCP 直接关闭 UDP socket；不发送 KCP 断开帧，也不排空待发队列。

历史上已公开但当前不生效的配置均带有编译期废弃提示：应用层 `SmallPacketThreshold` / `ChunkSize` 分片、自定义 `KeepAliveInterval`、重复的 `TcpTransportOptions.ConnectTimeout` 和自适应批处理开关。TCP linger、系统 KeepAlive、缓冲区和 `ConnectionTimeout` 则会真实应用到 socket/连接流程。

##### 客户端传输接口
```csharp
public interface IClientTransport : ITransportConnection
{
    string Name { get; }
    TransportType Type { get; }

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
```

##### 服务端传输接口
```csharp
public interface IServerTransport : ITransportConnection
{
    string Name { get; }
    TransportType Type { get; }
}
```

##### 服务端监听器接口
```csharp
public interface IServerListener : IDisposable
{
    string Name { get; }
    TransportType Type { get; }
    EndPoint LocalEndPoint { get; }
    bool IsListening { get; }

    event EventHandler<ServerConnectionEventArgs> ConnectionAccepted;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
```

#### 具体实现

##### 客户端传输实现
- **TCP 客户端**: `TcpClientTransport`
  - 基于可靠的 TCP 协议
  - 主动连接到服务器
  - 连接状态管理和重连机制
  - 活动时间跟踪

- **KCP 客户端**: `KcpClientTransport`
  - 基于 UDP 的可靠传输
  - 低延迟优化连接
  - 自定义拥塞控制
  - wire v2 握手携带显式协议版本；确认包只接受协商服务器端点
  - 握手接收使用整体超时窗口，不遗留超时的异步 UDP 接收
  - 自动重连复用已绑定 UDP socket；`MaxReconnectAttempts = 0` 表示无限重试
  - 丢包、乱序和 30 秒网络中断使用确定性故障注入验证重传恢复
  - 握手尚无可认证的 rebinding token，因此同 conversation ID 从新端点出现时 fail-closed，不自动接受 NAT rebinding

##### 服务端传输实现
- **TCP 服务端**: `TcpServerTransport`, `TcpServerListener`
  - 基于可靠的 TCP 协议
  - 单帧收发不超过 `MaxPacketSize`；已停用的 legacy chunk 帧会被拒绝
  - 连接状态管理
  - 活动时间跟踪

- **KCP 服务端**: `KcpServerTransport`, `KcpServerListener`
  - 基于 UDP 的可靠传输
  - 低延迟优化
  - 按完整消息大小接收，并以 `MaxPacketSize` 作为明确上限
  - 仅接受 `conv + wire version` 的 v2 握手，破坏性拒绝旧 4 字节握手
  - 自定义拥塞控制
  - 时间戳同步机制

```csharp
// KCP 核心配置
public class KcpTransportOptions
{
    public bool NoDelay { get; set; } = true;
    public int Interval { get; set; } = 10;
    public int Resend { get; set; } = 2;
    public bool DisableFlowControl { get; set; } = false;
    public int SendWindow { get; set; } = 128;
    public int RecvWindow { get; set; } = 128;
}
```

### 2. 会话层 (Session Layer)

#### 职责
**服务端会话层**：
- 在传输连接基础上提供会话抽象
- 管理认证上下文和用户身份
- 提供会话属性存储和管理
- 桥接传输层和应用层，隐藏传输细节

**客户端会话层**：
- 管理连接上下文和生命周期
- 维护请求-响应映射机制
- 提供服务代理缓存管理
- 实现连接状态监控和统计

#### 核心接口

##### 客户端连接上下文接口
```csharp
public interface IConnectionContext : IDisposable
{
    string Id { get; }
    ConnectionConfig Config { get; }
    ConnectionDescriptor Descriptor { get; }
    EndpointAddress Endpoint { get; }
    ExtendedConnectionState State { get; }
    ConnectionStatistics Statistics { get; }
    EndPoint? RemoteEndPoint { get; }
    EndPoint? LocalEndPoint { get; }
    DateTime CreatedAt { get; }
    DateTime LastActivityAt { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<T> GetServiceAsync<T>() where T : class, IPulseHub;
    Task<T> InvokeAsync<T>(RpcRequest request, CancellationToken cancellationToken = default);
    Task InvokeAsync(RpcRequest request, CancellationToken cancellationToken = default);

    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
}
```

##### 客户端连接接口
```csharp
public interface IConnection : IDisposable
{
    string Id { get; }
    ConnectionDescriptor Descriptor { get; }
    ExtendedConnectionState State { get; }

    Task<T> GetServiceAsync<T>() where T : class, IPulseHub;
    Task<T> SendAsync<T>(RpcRequest request, CancellationToken cancellationToken = default);
    Task SendAsync(RpcRequest request, CancellationToken cancellationToken = default);
}
```

##### 会话通道基础接口
```csharp
public interface ISessionChannel : ITransportConnection
{
    string SessionId => ConnectionId;
    IAuthenticationContext? AuthenticationContext { get; }
    bool IsAuthenticated { get; }
    IDictionary<string, object> Properties { get; }

    void SetAuthentication(IAuthenticationContext authContext);
    void ClearAuthentication();

    event EventHandler<AuthenticationChangedEventArgs> AuthenticationChanged;
}
```

##### 传输通道接口
```csharp
public interface ITransportChannel : ISessionChannel
{
    IServerTransport Transport { get; }
    DateTime ConnectedTime => ConnectedAt;
}
```

##### 服务端通道接口
```csharp
public interface IServerChannel : ITransportChannel
{
    // 服务端特定的通道功能（当前为空接口，用于类型标识）
}
```

#### 具体实现

##### 客户端会话层实现

###### 连接上下文实现
- **类**: `ConnectionContext`
- **特性**:
  - 管理IClientTransport实例
  - 维护连接状态机
  - 实现请求-响应映射机制
  - 提供服务代理缓存
  - 支持连接统计和监控

###### 简单连接包装器
- **类**: `SimpleConnection`
- **特性**:
  - 包装IConnectionContext为IConnection
  - 提供轻量级连接表示
  - 委托调用到底层连接上下文
  - 支持优雅的连接释放

##### 服务端会话层实现

###### 会话通道基础类
```csharp
public abstract class SessionChannelBase : ISessionChannel
{
    protected IAuthenticationContext? _authenticationContext;
    protected readonly Dictionary<string, object> _properties = new();

    public virtual bool IsAuthenticated => _authenticationContext?.IsAuthenticated == true;
    public virtual IDictionary<string, object> Properties => _properties;

    public virtual void SetAuthentication(IAuthenticationContext authContext);
    public virtual void ClearAuthentication();
}
```

##### 服务端传输通道
- **类**: `ServerTransportChannel`
- **特性**:
  - 包装 `IServerTransport` 提供会话功能
  - 管理认证状态和属性
  - 转发传输层事件到会话层
  - 提供连接生命周期管理

#### 认证集成
- **认证上下文**: 存储用户身份和认证信息
- **属性管理**: 支持自定义会话属性存储
- **事件通知**: 认证状态变化的事件通知

### 3. 应用层 (Application Layer)

#### 职责
**服务端应用层**：
- 提供高级业务功能接口
- 实现 RPC 服务调用和 Hub 方法
- 管理客户端会话和业务逻辑
- 提供完整的 RPC 编程模型

**客户端应用层**：
- 提供统一的客户端API接口
- 实现连接管理和路由策略
- 支持服务发现和负载均衡
- 提供Source Generator生成的服务代理

#### 核心接口

##### 客户端核心接口
```csharp
public interface IPulseClient : IDisposable
{
    IConnectionManager Connections { get; }
    IConnectionRouter Router { get; }
    IServiceDiscovery ServiceDiscovery { get; }
    IConnectionRegistry Registry { get; }
    IConnectionLifecycleManager Lifecycle { get; }
    ILoadBalancer LoadBalancer { get; }
    ClientState State { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(bool graceful = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    Task<IConnection> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);
    Task<IConnection> ConnectToServiceAsync(string serviceName, ServiceConnectionOptions? options = null, CancellationToken cancellationToken = default);
    Task<T> GetServiceAsync<T>(ServiceProxyOptions? options = null, CancellationToken cancellationToken = default) where T : class, IPulseHub;
    Task DisconnectAsync(string connectionId, bool graceful = true, CancellationToken cancellationToken = default);

    event EventHandler<ClientStateChangedEventArgs> StateChanged;
}
```

##### 连接管理器接口
```csharp
public interface IConnectionManager : IDisposable
{
    Task<IConnectionContext> ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default);
    Task<IConnectionContext> ConnectAsync(ConnectionDescriptor descriptor, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string connectionId, CancellationToken cancellationToken = default);
    IConnectionContext? GetConnection(string connectionId);
    IReadOnlyList<IConnectionContext> GetAllConnections();
    int Count { get; }
}
```

##### 连接路由器接口
```csharp
public interface IConnectionRouter
{
    void RegisterRule(RoutingRule rule);
    bool RemoveRule(string ruleId);
    Task<IConnection> RouteAsync(string routingKey, RoutingContext? context = null, CancellationToken cancellationToken = default);
    IReadOnlyList<IConnection> GetMatchingConnections(string routingKey, RoutingContext? context = null);
}
```

##### 服务发现接口
```csharp
public interface IServiceDiscovery : IDisposable
{
    Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<IServiceWatcher> WatchAsync(string serviceName, Action<ServiceChangeEvent> callback, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetServicesAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string serviceName, CancellationToken cancellationToken = default);
    Task RefreshAsync(string? serviceName = null, CancellationToken cancellationToken = default);
}
```

##### 服务端客户端会话接口
```csharp
public interface IClientSession : IDisposable
{
    string SessionId { get; }
    bool IsConnected { get; }
    bool IsAuthenticated { get; }
    ISessionStatistics? Statistics { get; }

    // RPC 服务调用
    Task<TResult> InvokeAsync<TService, TResult>(
        string methodName, object?[] args, CancellationToken cancellationToken = default)
        where TService : class, IPulseService;

    // Hub 方法调用
    Task<TResult> InvokeAsync<THub, TResult>(
        string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub;

    // 事件订阅
    Task SubscribeToHubEventsAsync<THub>(CancellationToken cancellationToken = default)
        where THub : class, IPulseHub;

    // 数据发送
    Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // 会话管理
    Task CloseAsync(CancellationToken cancellationToken = default);

    // 事件
    event EventHandler<SessionEventArgs>? SessionStateChanged;
    event EventHandler<SessionEventArgs>? SessionDisconnected;
}
```

##### 会话管理器接口
```csharp
public interface IClientSessionManager : IDisposable
{
    int ActiveSessionCount { get; }
    IReadOnlyCollection<string> SessionIds { get; }

    bool AddSession(IClientSession session);
    IClientSession? GetSession(string sessionId);
    Task<bool> RemoveSessionAsync(string sessionId);
    Task<IReadOnlyCollection<IClientSession>> GetAllSessionsAsync();
    Task<bool> BroadcastAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    event EventHandler<SessionEventArgs>? SessionAdded;
    event EventHandler<SessionEventArgs>? SessionRemoved;
}
```

#### 具体实现

##### 客户端应用层实现

###### PulseRPC客户端核心
- **类**: `PulseClient`
- **特性**:
  - 统一的客户端入口点
  - 集成连接管理、路由、服务发现
  - 支持连接池和负载均衡
  - 提供完整的生命周期管理

###### 连接管理器
- **类**: `ConnectionManager`
- **特性**:
  - 管理所有客户端连接
  - 支持连接复用和池化
  - 提供连接健康检查
  - 实现连接的优雅关闭

###### 服务代理生成器
- **Source Generator**: `ServiceProxyGenerator`
- **特性**:
  - 编译时生成强类型服务代理
  - 避免运行时反射开销
  - 支持IPulseHub接口的自动实现
  - 集成连接调用链

###### 事件处理器生成器
- **Source Generator**: `EventHandlerGenerator`
- **特性**:
  - 生成高性能事件处理器
  - 支持批量事件处理(BatchProcessor)
  - 提供性能监控包装器
  - 实现智能订阅管理

##### 服务端应用层实现

###### 客户端会话适配器
- **类**: `ClientSessionAdapter`
- **特性**:
  - 适配 `IServerChannel` 为 `IClientSession`
  - 提供完整的业务层功能
  - 集成会话统计和健康检查
  - 支持 RPC 和 Hub 方法调用

##### 会话管理器
- **类**: `ClientSessionManager`
- **特性**:
  - 管理所有活跃的客户端会话
  - 提供会话查找和批量操作
  - 实现会话生命周期管理
  - 支持广播和会话清理

## 数据流和层间交互

### 客户端数据流向
```
Client Application
     ↓
Application Layer (IPulseClient → ServiceProxy)
     ↓ (服务代理调用)
Session Layer (IConnection → IConnectionContext)
     ↓ (请求-响应映射)
Transport Layer (IClientTransport)
     ↓ (协议处理)
Network (TCP/KCP)
     ↓
Server
```

### 服务端数据流向
```
Client Request
     ↓
Application Layer (IClientSession)
     ↓ (业务逻辑处理)
Session Layer (ISessionChannel)
     ↓ (认证和会话管理)
Transport Layer (ITransportConnection)
     ↓ (协议处理)
Network (TCP/KCP)
```

### 客户端事件流向
```
Network Event (Connection/Data)
     ↓
Transport Layer (IClientTransport.StateChanged/DataReceived)
     ↓ (事件转发和响应匹配)
Session Layer (IConnectionContext.StateChanged/请求完成)
     ↓ (状态通知和服务代理更新)
Application Layer (IPulseClient.StateChanged/服务调用完成)
```

### 服务端事件流向
```
Network Event (Connection/Data)
     ↓
Transport Layer (StateChanged/DataReceived)
     ↓ (事件转发)
Session Layer (认证状态更新)
     ↓ (业务事件)
Application Layer (会话状态通知)
```

## 架构优势

### 1. 清晰的职责分离
- **传输层**: 专注于网络协议和连接管理
- **会话层**: 专注于认证和会话状态管理
- **应用层**: 专注于业务逻辑和 RPC 功能

### 2. 良好的扩展性
- 新传输协议易于添加
- 认证机制可插拔
- 业务功能可独立扩展

### 3. 事件驱动设计
- 松耦合的组件交互
- 异步编程模型
- 状态变化的及时通知

### 4. 类型安全
- 强类型接口设计
- 泛型方法支持
- 编译时类型检查

## 关键设计特性

### 1. 内存管理优化
- 使用 `ReadOnlyMemory<byte>` 减少拷贝
- 活动时间跟踪优化连接管理
- 事件驱动减少轮询开销

### 2. 异步编程
- 全面的 `async/await` 支持
- `CancellationToken` 取消机制
- `ValueTask` 性能优化

### 3. 协议优化
- TCP 的可靠性保证
- KCP 的低延迟特性
- 连接状态的高效管理

## 配置示例

### 服务端配置
```csharp
services.AddPulseServer(options =>
{
    options.ListenOn("127.0.0.1", 9090, TransportType.Tcp);
    options.ListenOn("127.0.0.1", 9091, TransportType.Kcp);
    options.MaxConcurrentConnections = 1000;
    options.Authentication.RequireAuthentication = true;
});
```

### 客户端配置
```csharp
// 方式1: 使用 PulseClient 统一接口
var client = new PulseClientBuilder()
    .AddConnection(new ConnectionDescriptor
    {
        Name = "primary",
        Host = "127.0.0.1",
        Port = 9090,
        Transport = TransportType.Tcp
    })
    .WithServiceDiscovery(new ConsulServiceDiscovery())
    .WithLoadBalancing(LoadBalancingStrategy.RoundRobin)
    .Build();

await client.InitializeAsync();

// 获取服务代理（自动路由）
var myService = await client.GetServiceAsync<IMyService>();
var result = await myService.GetDataAsync("param1");

// 方式2: 直接连接管理
var connectionManager = new ConnectionManager();
var connection = await connectionManager.ConnectAsync(new ConnectionDescriptor
{
    Name = "direct-connection",
    Host = "127.0.0.1",
    Port = 9090,
    Transport = TransportType.Tcp
});

var service = await connection.GetServiceAsync<IMyService>();
var data = await service.GetDataAsync("param1");
```

## 总结

PulseRPC 的三层抽象架构通过清晰的职责分离实现了高性能、可扩展的 RPC 通信框架。该架构涵盖了完整的客户端和服务端设计，主要优势包括：

### 架构优势
1. **模块化设计**: 各层职责明确，便于维护和扩展
2. **协议无关**: 支持多种传输协议，易于适配新协议
3. **业务友好**: 提供完整的 RPC 编程模型
4. **高性能**: 通过内存优化和异步编程实现高性能
5. **类型安全**: 强类型接口设计保证代码质量

### 客户端特色
1. **Source Generator集成**: 编译时生成强类型服务代理，避免运行时反射
2. **智能连接管理**: 支持连接池、负载均衡、自动重连
3. **服务发现**: 集成多种服务发现机制
4. **请求-响应映射**: 高效的异步请求处理机制
5. **事件处理优化**: 批量事件处理和性能监控

### 服务端特色
1. **会话管理**: 完整的客户端会话生命周期管理
2. **认证集成**: 可插拔的认证机制
3. **多协议支持**: TCP/KCP等多种传输协议
4. **高并发**: 支持大量并发连接和请求处理

这种分层架构为构建高质量的分布式应用提供了坚实的基础，能够满足从简单 RPC 调用到复杂实时通信的各种需求。通过客户端和服务端的对称设计，开发者可以轻松构建可靠、高性能的分布式系统。 
