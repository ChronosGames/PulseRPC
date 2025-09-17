# PulseRPC 传输层架构说明

## 概述

PulseRPC 是一个基于 TCP/KCP 的现代 RPC 框架，采用三层抽象架构设计，支持 .NET 和 Unity 平台。本文档详细描述了 PulseRPC 的传输层架构结构、各层职责以及核心设计原理。

## 架构总览

PulseRPC 传输层采用三层抽象架构模式，每层职责明确，实现了从底层网络传输到高级业务功能的完整抽象：

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

##### TCP 传输实现
- **类**: `TcpServerTransport`, `TcpServerListener`
- **特性**:
  - 基于可靠的 TCP 协议
  - 支持大包分片传输
  - 连接状态管理
  - 活动时间跟踪

##### KCP 传输实现
- **类**: `KcpServerTransport`, `KcpServerListener`
- **特性**:
  - 基于 UDP 的可靠传输
  - 低延迟优化
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
- 在传输连接基础上提供会话抽象
- 管理认证上下文和用户身份
- 提供会话属性存储和管理
- 桥接传输层和应用层，隐藏传输细节

#### 核心接口

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

##### 会话通道基础类
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
- 提供高级业务功能接口
- 实现 RPC 服务调用和 Hub 方法
- 管理客户端会话和业务逻辑
- 提供完整的 RPC 编程模型

#### 核心接口

##### 客户端会话接口
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

##### 客户端会话适配器
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

### 数据流向
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

### 事件流向
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
services.AddPulseRPCServer(options =>
{
    options.ListenOn("127.0.0.1", 9090, TransportType.Tcp);
    options.ListenOn("127.0.0.1", 9091, TransportType.Kcp);
    options.MaxConcurrentConnections = 1000;
    options.Authentication.RequireAuthentication = true;
});
```

### 客户端配置
```csharp
var sessionManager = new ClientSessionManager();
var transport = new TcpServerTransport("client-1", socket);
var channel = new ServerTransportChannel(transport);
var session = new ClientSessionAdapter(channel, sessionManager);

await session.InvokeAsync<IMyService, string>("GetData", new object[] { "param1" });
```

## 总结

PulseRPC 的三层抽象架构通过清晰的职责分离实现了高性能、可扩展的 RPC 通信框架。该架构的主要优势包括：

1. **模块化设计**: 各层职责明确，便于维护和扩展
2. **协议无关**: 支持多种传输协议，易于适配新协议
3. **业务友好**: 提供完整的 RPC 编程模型
4. **高性能**: 通过内存优化和异步编程实现高性能
5. **类型安全**: 强类型接口设计保证代码质量

这种分层架构为构建高质量的分布式应用提供了坚实的基础，能够满足从简单 RPC 调用到复杂实时通信的各种需求。 
