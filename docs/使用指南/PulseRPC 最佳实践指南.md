# PulseRPC 最佳实践指南

本文档提供使用 PulseRPC 框架的最佳实践建议，帮助您构建高性能、可维护和可扩展的分布式应用程序。

## 📋 目录

- [核心概念](#核心概念)
- [架构设计](#架构设计)
- [Hub 与 Service 分离架构](#hub-与-service-分离架构)
- [客户端最佳实践](#客户端最佳实践)
- [服务端推送消息](#服务端推送消息)
- [服务端进程间通信](#服务端进程间通信)
- [广播消息模式](#广播消息模式)
- [服务端最佳实践](#服务端最佳实践)
- [序列化优化](#序列化优化)
- [传输层优化](#传输层优化)
- [Unity 集成实践](#unity-集成实践)
- [性能优化](#性能优化)
- [错误处理](#错误处理)
- [命名约定与边界](#命名约定与边界)
- [注意事项](#注意事项)
- [监控与调试](#监控与调试)
- [部署实践](#部署实践)
- [快速参考](#快速参考)

---

## 核心概念

### 🎯 接口类型

PulseRPC 定义了两种核心标记接口：

| 接口 | 方向 | 用途 |
|------|------|------|
| `IPulseHub` | 客户端 → 服务端 | 定义客户端可调用的服务端方法（RPC 契约） |
| `IPulseReceiver` | 服务端 → 客户端 | 定义服务端可推送给客户端的事件 |

### 🏛️ 架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                         Client                              │
│  ┌─────────────────┐         ┌─────────────────────────┐   │
│  │ IGameHubStub    │────────▶│ IGameReceiver (实现)     │   │
│  │ (自动生成)       │         │ (客户端实现)             │   │
│  └─────────────────┘         └─────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                    ▲                      │
                    │  Request/Response    │ Push Event
                    │                      ▼
┌─────────────────────────────────────────────────────────────┐
│                         Server                              │
│  ┌─────────────────┐         ┌─────────────────────────┐   │
│  │ IGameHub (实现)  │         │ IHubContext<IGameReceiver>│  │
│  │ 无状态 Singleton │         │ (推送代理)               │   │
│  └────────┬────────┘         └─────────────────────────┘   │
│           │                                                 │
│           ▼                                                 │
│  ┌─────────────────────────────────────────────────────┐   │
│  │ UnifiedPulseServiceBase (有状态，多实例)              │   │
│  │ 通过 IServiceAccessor<T> 获取                        │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 📨 消息模式对比

#### 客户端向服务端发送消息

| 特性 | 有返回值 (Request) | 无返回值 (Notify) |
|------|-------------------|------------------|
| 返回类型 | `Task<TResult>` | `Task` |
| 网络往返 | 需要等待响应 | 发送后立即返回 |
| 可靠性 | 确认送达并处理完成 | Fire-and-forget |
| 适用场景 | 查询、需要确认的操作 | 心跳、位置同步、日志 |

```csharp
// 定义 Hub 接口
public interface IGameHub : IPulseHub
{
    // 有返回值：等待服务端响应
    Task<PlayerInfo> GetPlayerInfoAsync();

    // 无返回值：发送后立即返回
    Task HeartbeatAsync();
    Task MoveAsync(Vector3 position);
}

// 客户端调用
var playerInfo = await gameHub.GetPlayerInfoAsync();  // 等待响应
await gameHub.HeartbeatAsync();  // 立即返回
```

---

## 架构设计

### 🏗️ 服务边界设计

#### 1. 基于领域的服务划分

```csharp
// ✅ 推荐：按业务领域划分 Hub 接口
public interface IChatHub : IPulseHub
{
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request);
    Task<GetMessagesResponse> GetMessagesAsync(GetMessagesRequest request);
    Task<JoinRoomResponse> JoinRoomAsync(JoinRoomRequest request);
}

public interface IUserHub : IPulseHub
{
    Task<GetUserResponse> GetUserAsync(GetUserRequest request);
    Task<UpdateUserResponse> UpdateUserAsync(UpdateUserRequest request);
    Task<GetUserListResponse> GetUserListAsync(GetUserListRequest request);
}

// ❌ 避免：过于宽泛的服务接口
public interface IBusinessHub : IPulseHub
{
    Task<object> ProcessAsync(object request); // 太模糊
    Task<dynamic> HandleAsync(dynamic data);   // 类型不安全
}
```

#### 2. 合理的接口粒度

```csharp
// ✅ 推荐：适中的接口粒度
public interface IChatHub : IPulseHub
{
    // 核心聊天功能
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request);
    Task<BroadcastMessageResponse> BroadcastMessageAsync(BroadcastMessageRequest request);

    // 房间管理
    Task<JoinRoomResponse> JoinRoomAsync(JoinRoomRequest request);
    Task<LeaveRoomResponse> LeaveRoomAsync(LeaveRoomRequest request);
    Task<GetRoomInfoResponse> GetRoomInfoAsync(GetRoomInfoRequest request);

    // 用户状态
    Task<GetOnlineUsersResponse> GetOnlineUsersAsync(GetOnlineUsersRequest request);
}

// ❌ 避免：过度细化的接口
public interface IMessageSender : IPulseHub
{
    Task<SendMessageResponse> SendAsync(SendMessageRequest request);
}

public interface IRoomManager : IPulseHub
{
    Task<JoinRoomResponse> JoinAsync(JoinRoomRequest request);
}
```

### 🔗 依赖管理原则

```csharp
// ✅ 推荐：清晰的分层架构
namespace MyApp.Services
{
    // 应用层 - 依赖领域层和基础设施层
    public class ChatApplicationService
    {
        private readonly IChatDomainService _chatDomainService;
        private readonly IUserRepository _userRepository;
        private readonly IMessageRepository _messageRepository;
        
        // 处理应用级逻辑，协调领域服务
    }

    // 领域层 - 包含核心业务逻辑
    public class ChatDomainService : IChatDomainService
    {
        // 纯业务逻辑，不依赖外部系统
        public async Task<Message> CreateMessage(User user, string content)
        {
            // 领域规则验证
            if (string.IsNullOrWhiteSpace(content))
                throw new DomainException("消息内容不能为空");

            return new Message(user.Id, content, DateTime.UtcNow);
        }
    }
}
```

---

## Hub 与 Service 分离架构

PulseRPC 采用 **Hub + Service 分离架构**，将请求路由与业务状态管理解耦：

| 组件 | 职责 | 生命周期 | 状态 |
|------|------|---------|------|
| **Hub** | 路由、验证、参数处理 | Singleton | 无状态 |
| **Service** | 业务逻辑、状态管理 | 按需创建/多实例 | 有状态 |

### 🚪 Hub 实现（无状态 Singleton）

```csharp
// Server/Hubs/ChatRoomHub.cs
public class ChatRoomHub : IChatRoomHub
{
    private readonly IServiceAccessor<ChatRoomService> _roomService;
    private readonly ILogger<ChatRoomHub> _logger;

    // 从 PulseContext 获取当前请求的上下文信息
    private string? CurrentUserId => PulseContext.CurrentUserId;
    private string? CurrentRoomId => PulseContext.Current?.Properties["RoomId"] as string;

    public ChatRoomHub(
        IServiceAccessor<ChatRoomService> roomService,
        ILogger<ChatRoomHub> logger)
    {
        _roomService = roomService;
        _logger = logger;
    }

    public async Task<JoinRoomResult> JoinRoomAsync(string roomId)
    {
        // 1. 参数验证
        if (string.IsNullOrWhiteSpace(roomId))
            return JoinRoomResult.Failed("RoomId is required");

        // 2. 身份验证
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId))
            return JoinRoomResult.Failed("Not authenticated");

        // 3. 获取 Service 实例（按 roomId 获取对应实例）
        var service = await _roomService.GetAsync(roomId);

        // 4. 确保服务已启动
        if (service.State == ServiceLifecycleState.Created)
            await service.StartAsync();

        // 5. 在服务队列中执行业务逻辑（线程安全）
        return await service.EnqueueAsync(() => service.JoinAsync(userId));
    }
}
```

### 🏠 Service 实现（有状态，多实例）

```csharp
// Server/Services/ChatRoomService.cs
[PulseService(
    Scenario = ServiceScenario.Actor,           // 单线程顺序执行
    StartupType = ServiceStartupType.OnDemand,  // 按需创建
    InstanceScope = ServiceInstanceScope.MultiInstance)]  // 每个房间一个实例
public class ChatRoomService : UnifiedPulseServiceBase
{
    // 状态只在队列中访问，无需加锁
    private readonly HashSet<string> _members = new();
    private readonly List<ChatMessage> _messages = new();

    public string RoomId => ServiceId;

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
        : base("ChatRoom", roomId, logger)  // serviceType, serviceId
    {
    }

    // ========== 生命周期钩子 ==========

    public override Task OnStartingAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("ChatRoom {RoomId} starting", RoomId);
        return Task.CompletedTask;
    }

    // ========== 业务方法（通过 EnqueueAsync 调用）==========

    public Task<JoinRoomResult> JoinAsync(string userId)
    {
        if (_members.Contains(userId))
            return Task.FromResult(JoinRoomResult.Failed("Already in room"));

        _members.Add(userId);
        return Task.FromResult(JoinRoomResult.Ok(RoomId, _members.Count));
    }
}
```

### ⚙️ Service 配置选项

#### ServiceScenario（执行场景）

| 场景 | 描述 | 适用场景 |
|------|------|---------|
| `Actor` | 单线程顺序执行，保证线程安全 | 游戏房间、聊天室、玩家状态 |
| `StatefulIO` | 有状态但包含大量 I/O 操作 | 玩家服务、订单处理、会话管理 |
| `StatelessIO` | 无状态 I/O 密集，可并发执行 | 数据库查询、HTTP 网关、缓存读取 |
| `StatelessCPU` | 无状态 CPU 密集 | 寻路计算、加密解密、数据处理 |
| `HighFrequency` | 高频更新，利用 CPU 缓存局部性 | 实时数据聚合、热点数据处理 |

#### ServiceStartupType（启动类型）

| 类型 | 描述 |
|------|------|
| `OnDemand` | 首次访问时创建 |
| `AutoStart` | 应用启动时自动创建 |

#### ServiceInstanceScope（实例范围）

| 范围 | 描述 |
|------|------|
| `Singleton` | 全局单例 |
| `MultiInstance` | 每个 ServiceId 一个实例 |

### 🔧 IServiceAccessor 扩展方法

```csharp
// 方式 1: 手动获取和入队
var service = await _roomService.GetAsync(roomId);
var result = await service.EnqueueAsync(() => service.JoinAsync(userId));

// 方式 2: 使用扩展方法（更简洁）
var result = await _roomService.ExecuteAsync(roomId, s => s.JoinAsync(userId));

// 方式 3: Singleton 服务简化调用
await _guildService.Execute(s => s.CreateGuildAsync(request));

// 方式 4: 使用当前用户 ID
await _playerService.ExecuteForCurrentUser(s => s.GetInfoAsync());
```

---

## 客户端最佳实践

### 🔧 客户端配置

```csharp
// ✅ 推荐：使用 PulseClientBuilder 创建客户端
public class ChatClient
{
    private readonly IPulseClient _client;
    private IChatHub? _chatHub;

    public ChatClient()
    {
        // 创建连接配置
        var connectionConfig = ConnectionConfig.Tcp(
            name: "ChatServer",
            host: "localhost",
            port: 8000);

        // 使用 Builder 创建客户端
        _client = new PulseClientBuilder()
            .AddConnection(connectionConfig.ToDescriptor())
            .WithLogging(LoggerFactory.Create(builder => builder.AddConsole()))
            .Build();
    }

    public async Task InitializeAsync()
    {
        // 初始化连接
        await _client.InitializeAsync();

        // 获取 Hub 代理（由源生成器生成的扩展方法）
        _chatHub = await _client.GetChatHubAsync();
    }

    public async Task<bool> SendMessageAsync(string message)
    {
        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        try
        {
            return await _chatHub.SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            // 处理 RPC 异常
            Console.WriteLine($"RPC 调用失败: {ex.Message}");
            throw;
        }
    }
}
```

### 🔄 连接管理

```csharp
// ✅ 推荐：实现 IDisposable 模式
public class ChatClientManager : IDisposable
{
    private readonly IPulseClient _client;
    private IChatHub? _chatHub;
    private bool _disposed = false;

    public ChatClientManager(string host, int port)
    {
        var connectionConfig = ConnectionConfig.Tcp("ChatServer", host, port);
        _client = new PulseClientBuilder()
            .AddConnection(connectionConfig.ToDescriptor())
            .Build();
    }

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await _client.InitializeAsync();
        _chatHub = await _client.GetChatHubAsync();
    }

    public async Task<T> CallHubAsync<T>(Func<IChatHub, Task<T>> hubCall)
    {
        ThrowIfDisposed();
        if (_chatHub == null)
            throw new InvalidOperationException("客户端未初始化");

        return await hubCall(_chatHub);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChatClientManager));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client?.Dispose();
            _disposed = true;
        }
    }
}
```

### 📦 批量操作优化

```csharp
// ✅ 推荐：使用批量接口减少网络往返
public interface IUserHub : IPulseHub
{
    // 单个操作
    Task<GetUserResponse> GetUserAsync(GetUserRequest request);

    // 批量操作 - 减少网络开销
    Task<GetMultipleUsersResponse> GetMultipleUsersAsync(GetMultipleUsersRequest request);
}

public class UserClient
{
    private IUserHub? _userHub;

    public async Task<List<User>> GetUsersAsync(List<int> userIds)
    {
        if (_userHub == null)
            throw new InvalidOperationException("客户端未初始化");

        // ✅ 批量获取而非多次单独调用
        var request = new GetMultipleUsersRequest { UserIds = userIds };
        var response = await _userHub.GetMultipleUsersAsync(request);
        return response.Users;
    }
}
```

---

## 服务端推送消息

### 📤 定义 Receiver 接口（共享契约）

```csharp
// Shared/Receivers/IGameReceiver.cs
using PulseRPC;

/// <summary>
/// 游戏事件接收器 - 服务端向客户端推送事件
/// </summary>
public interface IGameReceiver : IPulseReceiver
{
    /// <summary>
    /// 匹配成功通知
    /// </summary>
    Task OnMatchFoundAsync(MatchFoundNotification notification);

    /// <summary>
    /// 好友上线通知
    /// </summary>
    Task OnFriendOnlineAsync(string friendId, string friendName);

    /// <summary>
    /// 系统公告
    /// </summary>
    Task OnSystemAnnouncementAsync(string message);

    /// <summary>
    /// 踢出通知
    /// </summary>
    Task OnKickedAsync(string reason);
}
```

### 📡 服务端推送（使用 IHubContext）

```csharp
// Server/Services/GameNotificationService.cs
public class GameNotificationService
{
    private readonly IHubContext<IGameReceiver> _hubContext;

    public GameNotificationService(IHubContext<IGameReceiver> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <summary>
    /// 向单个用户推送
    /// </summary>
    public async Task NotifyMatchFoundAsync(string userId, MatchFoundNotification notification)
    {
        await _hubContext.Clients.User(userId).OnMatchFoundAsync(notification);
    }

    /// <summary>
    /// 向多个用户推送
    /// </summary>
    public async Task NotifyFriendsOnlineAsync(IReadOnlyList<string> friendIds, string onlineUserId)
    {
        await _hubContext.Clients.Users(friendIds)
            .OnFriendOnlineAsync(onlineUserId, "PlayerName");
    }

    /// <summary>
    /// 广播给所有在线用户
    /// </summary>
    public async Task BroadcastAnnouncementAsync(string message)
    {
        await _hubContext.Clients.All.OnSystemAnnouncementAsync(message);
    }
}
```

### 📱 客户端接收（实现 Receiver 接口）

```csharp
// Client/Receivers/GameReceiver.cs
public class GameReceiver : IGameReceiver
{
    public Task OnMatchFoundAsync(MatchFoundNotification notification)
    {
        Console.WriteLine($"匹配成功！战斗ID: {notification.BattleId}");
        return Task.CompletedTask;
    }

    public Task OnFriendOnlineAsync(string friendId, string friendName)
    {
        Console.WriteLine($"好友 {friendName} 上线了");
        return Task.CompletedTask;
    }

    public Task OnSystemAnnouncementAsync(string message)
    {
        Console.WriteLine($"[系统公告] {message}");
        return Task.CompletedTask;
    }

    public Task OnKickedAsync(string reason)
    {
        Console.WriteLine($"您已被踢出: {reason}");
        return Task.CompletedTask;
    }
}

// 客户端注册接收器（使用源生成器生成的扩展方法）
await client.InitializeAsync();
var subscription = await client.RegisterEventListenerAsync<IGameReceiver>(
    new GameReceiver(),
    cancellationToken);
```

---

## 服务端进程间通信

### 🔗 场景：GameServer 通过 BackendServer 进行匹配

```
┌─────────────┐        ┌──────────────┐        ┌─────────────┐
│ GameServer  │───────▶│ BackendServer│───────▶│BattleServer │
│ (玩家连接)   │        │ (匹配服务)    │        │ (战斗服务)   │
└─────────────┘        └──────────────┘        └─────────────┘
       ▲                      │
       │                      │ 匹配成功回调
       └──────────────────────┘
```

### 📝 定义内部 RPC 接口

```csharp
// Shared/Hubs/IGameServerInternalHub.cs
[Channel("GameServerInternal")]
[Authorize(Role = RoleTypes.Internal)]  // 仅内部服务可调用
public interface IGameServerInternalHub : IPulseHub
{
    /// <summary>
    /// 匹配成功回调 - BackendServer 调用
    /// </summary>
    Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification);

    /// <summary>
    /// 匹配取消回调
    /// </summary>
    Task<bool> OnMatchCancelledAsync(string playerId, string reason);
}
```

### 🖥️ GameServer 实现（接收回调）

```csharp
// GameServer/Hubs/GameServerInternalHub.cs
[PulseService(
    Scenario = ServiceScenario.Actor,
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.Singleton)]
public class GameServerInternalHub : UnifiedPulseServiceBase, IGameServerInternalHub
{
    private readonly IHubContext<IGameReceiver> _gameReceiverContext;
    private readonly IUserConnectionMapping _userConnectionMapping;

    public GameServerInternalHub(
        IHubContext<IGameReceiver> gameReceiverContext,
        IUserConnectionMapping userConnectionMapping,
        ILogger<GameServerInternalHub> logger)
        : base("GameServerInternalHub", "Global", logger)
    {
        _gameReceiverContext = gameReceiverContext;
        _userConnectionMapping = userConnectionMapping;
    }

    public async Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification)
    {
        // 1. 检查玩家是否在线
        if (!_userConnectionMapping.IsUserOnline(playerId))
        {
            Logger.LogWarning("Player not connected: {PlayerId}", playerId);
            return false;
        }

        // 2. 转发通知给客户端
        await _gameReceiverContext.Clients.User(playerId).OnMatchFoundAsync(notification);

        return true;
    }
}
```

### 📞 BackendServer 调用 GameServer

```csharp
// BackendServer/Services/MatchmakingService.cs
public class MatchmakingService
{
    private readonly IPulseClient _internalClient;  // 用于服务间通信

    public async Task NotifyMatchFoundAsync(string gameServerId, string playerId, MatchFoundNotification notification)
    {
        // 获取到目标 GameServer 的连接
        var channel = await _internalClient.Connections.RouteAsync("GameServerInternal");

        if (channel == null)
        {
            _logger.LogError("Cannot connect to GameServer");
            return;
        }

        // 调用 GameServer 的内部接口
        var hub = new IGameServerInternalHubStub(channel);
        var success = await hub.OnMatchFoundAsync(playerId, notification);

        if (!success)
        {
            _logger.LogWarning("Failed to notify player {PlayerId}", playerId);
        }
    }
}
```

---

## 广播消息模式

### 📢 IHubClients 选择器 API

```csharp
private readonly IHubContext<IGameReceiver> _hubContext;

// ========== 单播 ==========

// 向单个连接发送
await _hubContext.Clients.Single(connectionId).OnEventAsync(data);

// 向单个用户发送（可能有多个连接）
await _hubContext.Clients.User(userId).OnEventAsync(data);

// ========== 多播 ==========

// 向多个连接发送
await _hubContext.Clients.Only(connectionIds).OnEventAsync(data);

// 向多个用户发送
await _hubContext.Clients.Users(userIds).OnEventAsync(data);

// ========== 广播 ==========

// 向所有已认证客户端广播
await _hubContext.Clients.All.OnEventAsync(data);

// 向所有客户端广播，排除指定连接
await _hubContext.Clients.Except(excludedConnectionId).OnEventAsync(data);

// 向所有客户端广播，排除多个连接
await _hubContext.Clients.Except(excludedConnectionIds).OnEventAsync(data);

// ========== 组广播 ==========

// 向组内所有成员发送
await _hubContext.Clients.Group("room-123").OnEventAsync(data);

// 向多个组发送
await _hubContext.Clients.Groups(groupNames).OnEventAsync(data);

// 向组内发送，排除指定连接
await _hubContext.Clients.GroupExcept("room-123", excludedConnectionId).OnEventAsync(data);
```

### 👥 使用 IGroup 进行组管理

```csharp
public class GameRoomHub : PulseHubBase<IGameReceiver>, IGameRoomHub
{
    private readonly IGroupProvider<IGameReceiver> _groupProvider;
    private IGroup<IGameReceiver>? _room;

    public GameRoomHub(
        IHubContext<IGameReceiver> hubContext,
        IGroupProvider<IGameReceiver> groupProvider)
    {
        HubContext = hubContext;  // 设置基类属性
        _groupProvider = groupProvider;
    }

    public async Task JoinRoomAsync(string roomName)
    {
        // 加入房间（如果不存在则创建）
        _room = await _groupProvider.AddAsync(roomName);

        // 通知房间内其他人（排除自己）
        _room.Except(RequireConnectionId).OnPlayerJoinedAsync(playerInfo);
    }

    public async Task SendMessageAsync(string message)
    {
        // 向房间内所有人发送
        _room?.All.OnChatMessageAsync(UserId, message);
    }

    public async Task LeaveRoomAsync()
    {
        if (_room != null)
        {
            // 通知其他人
            _room.Except(RequireConnectionId).OnPlayerLeftAsync(UserId);

            // 离开房间
            await _room.RemoveAsync(RequireConnectionId);
            _room = null;
        }
    }
}
```

---

## 服务端最佳实践

### 🚀 服务实现

```csharp
// ✅ 推荐：良好的服务实现结构
public class ChatService : IChatService
{
    private readonly ILogger<ChatService> _logger;
    private readonly IChatDomainService _domainService;
    private readonly IMessageRepository _messageRepository;
    private readonly IConnectionManager _connectionManager;

    public ChatService(
        ILogger<ChatService> logger,
        IChatDomainService domainService,
        IMessageRepository messageRepository,
        IConnectionManager connectionManager)
    {
        _logger = logger;
        _domainService = domainService;
        _messageRepository = messageRepository;
        _connectionManager = connectionManager;
    }

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        // 参数验证
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("消息内容不能为空", nameof(request.Message));

        try
        {
            _logger.LogInformation("处理发送消息请求: {UserId} -> {RoomId}", 
                request.UserId, request.RoomId);

            // 领域逻辑处理
            var message = await _domainService.CreateMessageAsync(
                request.UserId, request.RoomId, request.Message);

            // 持久化
            await _messageRepository.SaveAsync(message);

            // 广播给房间内其他用户
            await _connectionManager.BroadcastToRoomAsync(
                request.RoomId, message, excludeUserId: request.UserId);

            _logger.LogInformation("消息发送成功: {MessageId}", message.Id);

            return new SendMessageResponse
            {
                Success = true,
                MessageId = message.Id,
                Timestamp = message.CreatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败: {UserId} -> {RoomId}", 
                request.UserId, request.RoomId);
            throw;
        }
    }
}
```

### 🔒 输入验证和安全

```csharp
// ✅ 推荐：分层验证策略
[MemoryPackable]
public partial class SendMessageRequest
{
    [MemoryPackOrder(0)]
    public int UserId { get; set; }
    
    [MemoryPackOrder(1)]
    public int RoomId { get; set; }
    
    [MemoryPackOrder(2)]
    public string Message { get; set; } = string.Empty;
    
    // 客户端验证
    public bool IsValid(out string errorMessage)
    {
        if (UserId <= 0)
        {
            errorMessage = "用户ID无效";
            return false;
        }
        
        if (RoomId <= 0)
        {
            errorMessage = "房间ID无效";
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(Message))
        {
            errorMessage = "消息内容不能为空";
            return false;
        }
        
        if (Message.Length > 1000)
        {
            errorMessage = "消息内容过长";
            return false;
        }
        
        errorMessage = string.Empty;
        return true;
    }
}

// 服务端验证
public class ChatService : IChatService
{
    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        // 服务端重复验证（防止客户端绕过）
        if (!request.IsValid(out var errorMessage))
        {
            throw new ArgumentException($"请求参数无效: {errorMessage}");
        }
        
        // 权限检查
        if (!await _authorizationService.CanSendMessageAsync(request.UserId, request.RoomId))
        {
            throw new UnauthorizedAccessException("用户无权限在该房间发送消息");
        }
        
        // 内容安全检查
        if (await _contentModerationService.IsInappropriateAsync(request.Message))
        {
            throw new ArgumentException("消息内容不合规");
        }
        
        // 业务逻辑处理...
    }
}
```

## 序列化优化

### 📦 MemoryPack 最佳实践

```csharp
// ✅ 推荐：正确使用 MemoryPack 特性
[MemoryPackable]
public partial class ChatMessage
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public int SenderId { get; set; }
    
    [MemoryPackOrder(2)]
    public int RoomId { get; set; }
    
    [MemoryPackOrder(3)]
    public string Content { get; set; } = string.Empty;
    
    [MemoryPackOrder(4)]
    public DateTime CreatedAt { get; set; }
    
    // MemoryPack 要求无参构造函数
    public ChatMessage() { }
    
    public ChatMessage(int senderId, int roomId, string content)
    {
        SenderId = senderId;
        RoomId = roomId;
        Content = content;
        CreatedAt = DateTime.UtcNow;
    }
}

// ✅ 推荐：使用 Union 处理多态
[MemoryPackable]
[MemoryPackUnion(0, typeof(TextMessage))]
[MemoryPackUnion(1, typeof(ImageMessage))]
[MemoryPackUnion(2, typeof(FileMessage))]
public abstract partial class BaseMessage
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public int SenderId { get; set; }
    
    [MemoryPackOrder(2)]
    public DateTime CreatedAt { get; set; }
}

[MemoryPackable]
public partial class TextMessage : BaseMessage
{
    [MemoryPackOrder(10)]
    public string Content { get; set; } = string.Empty;
}

[MemoryPackable]
public partial class ImageMessage : BaseMessage
{
    [MemoryPackOrder(10)]
    public string ImageUrl { get; set; } = string.Empty;
    
    [MemoryPackOrder(11)]
    public int Width { get; set; }
    
    [MemoryPackOrder(12)]
    public int Height { get; set; }
}
```

### 🚫 避免序列化陷阱

```csharp
// ❌ 避免：循环引用
public class BadUser
{
    public int Id { get; set; }
    public List<BadMessage> Messages { get; set; } = new(); // 可能导致循环引用
}

public class BadMessage
{
    public int Id { get; set; }
    public BadUser Sender { get; set; } = null!; // 循环引用
}

// ✅ 推荐：使用 ID 引用而非对象引用
[MemoryPackable]
public partial class User
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;
    
    // 不直接包含 Messages 集合，避免循环引用
}

[MemoryPackable]
public partial class Message
{
    [MemoryPackOrder(0)]
    public int Id { get; set; }
    
    [MemoryPackOrder(1)]
    public int SenderId { get; set; } // 使用 ID 而非对象引用
    
    [MemoryPackOrder(2)]
    public string Content { get; set; } = string.Empty;
}
```

## 传输层优化

### 🌐 TCP vs KCP 选择

```csharp
// ✅ TCP 适用场景：可靠性要求高
public class FileTransferClient
{
    private readonly IPulseClient _client;

    public FileTransferClient()
    {
        // 文件传输使用 TCP 确保数据完整性
        var config = ConnectionConfig.Tcp("file-transfer", "localhost", 8001);
        _client = new PulseClientBuilder()
            .AddConnection(config.ToDescriptor())
            .Build();
    }
}

// ✅ KCP 适用场景：实时性要求高
public class GameClient
{
    private readonly IPulseClient _client;

    public GameClient()
    {
        // 游戏数据使用 KCP 获得更低延迟
        var config = ConnectionDescriptor.CreateKcp("game", "GameServer", "localhost", 8002);
        _client = new PulseClientBuilder()
            .AddConnection(config)
            .Build();
    }
}

// ✅ 混合使用：不同场景选择不同协议
public class HybridGameClient
{
    private readonly IPulseClient _client;
    private IGameSaveHub? _saveHub;
    private IRealtimeHub? _realtimeHub;

    public HybridGameClient()
    {
        // 同一客户端可添加多个连接（TCP 和 KCP）
        _client = new PulseClientBuilder()
            .AddConnection(ConnectionDescriptor.CreateTcp("reliable", "GameServer", "localhost", 8001))
            .AddConnection(ConnectionDescriptor.CreateKcp("realtime", "GameServer", "localhost", 8002))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _client.InitializeAsync();
        _saveHub = await _client.GetGameSaveHubAsync();
        _realtimeHub = await _client.GetRealtimeHubAsync();
    }

    public async Task SaveGameAsync(GameSaveData data)
    {
        // 重要数据使用 TCP 连接
        if (_saveHub == null) throw new InvalidOperationException("未初始化");
        await _saveHub.SaveAsync(data);
    }

    public async Task SendPlayerPositionAsync(Vector3 position)
    {
        // 实时数据使用 KCP 连接
        if (_realtimeHub == null) throw new InvalidOperationException("未初始化");
        await _realtimeHub.UpdatePositionAsync(position);
    }
}
```

### 📊 连接池优化

```csharp
// ✅ 推荐：合理配置连接参数
public class OptimizedClientFactory
{
    public static IPulseClient CreateOptimizedClient(string name, string host, int port)
    {
        var descriptor = ConnectionDescriptor.CreateTcp(name, "GameServer", host, port);

        return new PulseClientBuilder()
            .AddConnection(descriptor)
            .WithTransportOptions(TransportType.TCP, new TcpTransportOptions
            {
                // TCP 相关优化
                KeepAlive = true,
                NoDelay = true,          // 禁用 Nagle 算法，降低延迟
                ReceiveBufferSize = 64 * 1024,  // 64KB 接收缓冲区
                SendBufferSize = 64 * 1024,     // 64KB 发送缓冲区
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ReadTimeout = TimeSpan.FromSeconds(30),
                WriteTimeout = TimeSpan.FromSeconds(30)
            })
            .Build();
    }
}
```

## Unity 集成实践

### 🎮 Unity 客户端实现

```csharp
// ✅ Unity 中的 PulseRPC 客户端
public class UnityGameClient : MonoBehaviour
{
    [SerializeField] private string serverHost = "localhost";
    [SerializeField] private int serverPort = 8000;

    private IPulseClient _client;
    private IGameHub _gameHub;
    private bool _isConnected = false;

    async void Start()
    {
        try
        {
            // 创建连接配置
            var config = ConnectionConfig.Tcp("GameServer", serverHost, serverPort);

            // 使用 Builder 创建客户端
            _client = new PulseClientBuilder()
                .AddConnection(config.ToDescriptor())
                .Build();

            // 初始化并获取 Hub 代理
            await _client.InitializeAsync();
            _gameHub = await _client.GetGameHubAsync();

            // 测试连接
            await _gameHub.PingAsync();
            _isConnected = true;

            Debug.Log("成功连接到游戏服务器");
        }
        catch (Exception ex)
        {
            Debug.LogError($"连接游戏服务器失败: {ex.Message}");
        }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (!_isConnected) return false;

        try
        {
            var request = new LoginRequest { Username = username, Password = password };
            var response = await _gameHub.LoginAsync(request);
            return response.Success;
        }
        catch (Exception ex)
        {
            Debug.LogError($"登录失败: {ex.Message}");
            return false;
        }
    }

    void OnDestroy()
    {
        // Unity 中确保资源清理
        _client?.Dispose();
    }
}
```

### 📱 Unity 异步处理

```csharp
// ✅ Unity 中处理异步操作
public class AsyncGameManager : MonoBehaviour
{
    private IGameHub _gameHub;
    private CancellationTokenSource _cancellationTokenSource;

    void Start()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        // 初始化客户端...
    }

    // 使用 UniTask 或 Unity 的 async/await
    public async Task<GameState> LoadGameStateAsync()
    {
        if (_gameHub == null)
            throw new InvalidOperationException("客户端未初始化");

        try
        {
            var request = new GetGameStateRequest { PlayerId = GetPlayerId() };
            var response = await _gameHub.GetGameStateAsync(request)
                .ConfigureAwait(false); // 避免死锁

            // 切换回主线程更新 UI
            await UnityMainThreadDispatcher.Instance.EnqueueAsync(() =>
            {
                UpdateGameUI(response.GameState);
            });

            return response.GameState;
        }
        catch (OperationCanceledException)
        {
            Debug.Log("游戏状态加载被取消");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"加载游戏状态失败: {ex.Message}");
            return null;
        }
    }

    void OnDestroy()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
```

## 性能优化

### ⚡ 并发处理

```csharp
// ✅ 推荐：合理的并发控制
public class HighPerformanceService
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<HighPerformanceService> _logger;

    public HighPerformanceService(ILogger<HighPerformanceService> logger)
    {
        _logger = logger;
        // 控制并发数量，避免资源耗尽
        _semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
    }

    public async Task<ProcessResult> ProcessBatchAsync(List<ProcessRequest> requests)
    {
        var tasks = requests.Select(async request =>
        {
            await _semaphore.WaitAsync();
            try
            {
                return await ProcessSingleRequestAsync(request);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return new ProcessResult { Results = results.ToList() };
    }

    private async Task<ProcessSingleResult> ProcessSingleRequestAsync(ProcessRequest request)
    {
        // 单个请求处理逻辑
        await Task.Delay(100); // 模拟处理时间
        return new ProcessSingleResult { Success = true };
    }
}
```

### 📈 缓存策略

```csharp
// ✅ 推荐：多层缓存策略
public class CachedUserService : IUserService
{
    private readonly IUserService _innerService;
    private readonly IMemoryCache _memoryCache;
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<CachedUserService> _logger;

    public async Task<GetUserResponse> GetUserAsync(GetUserRequest request)
    {
        var cacheKey = $"user:{request.UserId}";

        // 一级缓存：内存缓存（最快）
        if (_memoryCache.TryGetValue(cacheKey, out GetUserResponse cachedResponse))
        {
            _logger.LogDebug("从内存缓存获取用户: {UserId}", request.UserId);
            return cachedResponse;
        }

        // 二级缓存：分布式缓存
        var distributedData = await _distributedCache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(distributedData))
        {
            var response = JsonSerializer.Deserialize<GetUserResponse>(distributedData);
            
            // 回填内存缓存
            _memoryCache.Set(cacheKey, response, TimeSpan.FromMinutes(5));
            
            _logger.LogDebug("从分布式缓存获取用户: {UserId}", request.UserId);
            return response;
        }

        // 缓存未命中，调用实际服务
        var result = await _innerService.GetUserAsync(request);

        // 写入缓存
        var serializedData = JsonSerializer.Serialize(result);
        await _distributedCache.SetStringAsync(cacheKey, serializedData, 
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });

        _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

        _logger.LogDebug("从服务获取用户并缓存: {UserId}", request.UserId);
        return result;
    }
}
```

## 错误处理

### 🔄 重试和容错

```csharp
// ✅ 推荐：智能重试机制
public class ResilientServiceClient
{
    private readonly IPulseClient _client;
    private readonly ILogger<ResilientServiceClient> _logger;

    public async Task<T> CallWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        TimeSpan? baseDelay = null)
    {
        var delay = baseDelay ?? TimeSpan.FromMilliseconds(500);
        var attempt = 0;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxRetries))
            {
                attempt++;
                var waitTime = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                
                _logger.LogWarning("操作失败，{Delay}ms 后进行第 {Attempt} 次重试: {Error}", 
                    waitTime.TotalMilliseconds, attempt, ex.Message);
                
                await Task.Delay(waitTime);
            }
        }
    }

    private static bool ShouldRetry(Exception ex, int currentAttempt, int maxRetries)
    {
        if (currentAttempt >= maxRetries) return false;

        // 只对特定网络/传输相关异常进行重试
        return ex is SocketException ||
               ex is TimeoutException ||
               ex is TaskCanceledException ||
               ex is IOException;
    }
}
```

### 🛡️ 全局异常处理

```csharp
// ✅ 推荐：统一异常处理
public class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public async Task<ServiceResponse<T>> HandleAsync<T>(Func<Task<T>> operation, string operationName)
    {
        try
        {
            var result = await operation();
            return ServiceResponse<T>.Success(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("参数错误 - {Operation}: {Message}", operationName, ex.Message);
            return ServiceResponse<T>.Failure("INVALID_ARGUMENT", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("权限错误 - {Operation}: {Message}", operationName, ex.Message);
            return ServiceResponse<T>.Failure("UNAUTHORIZED", "访问被拒绝");
        }
        catch (TimeoutException ex)
        {
            _logger.LogError("超时错误 - {Operation}: {Message}", operationName, ex.Message);
            return ServiceResponse<T>.Failure("TIMEOUT", "操作超时");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常 - {Operation}", operationName);
            return ServiceResponse<T>.Failure("INTERNAL_ERROR", "内部服务器错误");
        }
    }
}

public class ServiceResponse<T>
{
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ServiceResponse<T> Success(T data) => new()
    {
        Success = true,
        Data = data
    };

    public static ServiceResponse<T> Failure(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
```

## 监控与调试

### 📊 性能监控

```csharp
// ✅ 推荐：集成性能监控
public class MonitoredChatService : IChatService
{
    private readonly IChatService _innerService;
    private readonly IMetricsCollector _metrics;
    private readonly ILogger<MonitoredChatService> _logger;

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var labels = new Dictionary<string, string>
        {
            ["method"] = nameof(SendMessageAsync),
            ["room_id"] = request.RoomId.ToString()
        };

        try
        {
            _metrics.IncrementCounter("rpc_requests_total", labels);
            
            var response = await _innerService.SendMessageAsync(request);
            
            _metrics.IncrementCounter("rpc_requests_success_total", labels);
            return response;
        }
        catch (Exception ex)
        {
            labels["error_type"] = ex.GetType().Name;
            _metrics.IncrementCounter("rpc_requests_error_total", labels);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _metrics.RecordHistogram("rpc_request_duration_seconds", 
                stopwatch.Elapsed.TotalSeconds, labels);
        }
    }
}
```

### 🔍 结构化日志

```csharp
// ✅ 推荐：结构化日志记录
public class ChatService : IChatService
{
    private readonly ILogger<ChatService> _logger;

    public async Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = Guid.NewGuid(),
            ["UserId"] = request.UserId,
            ["RoomId"] = request.RoomId,
            ["Operation"] = "SendMessage"
        });

        _logger.LogInformation("开始处理发送消息请求");

        try
        {
            // 业务逻辑...
            var response = new SendMessageResponse { Success = true };

            _logger.LogInformation("消息发送成功, MessageId: {MessageId}", response.MessageId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "消息发送失败");
            throw;
        }
    }
}
```

## 部署实践

### 🐳 容器化部署

```dockerfile
# ✅ 优化的 Dockerfile for .NET 10
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# 创建非 root 用户
RUN addgroup --system --gid 1001 pulserpc
RUN adduser --system --uid 1001 --ingroup pulserpc pulserpc

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 先复制项目文件，利用 Docker 缓存
COPY ["Directory.Packages.props", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/MyPulseRPCService/MyPulseRPCService.csproj", "src/MyPulseRPCService/"]
COPY ["src/PulseRPC.Abstractions/PulseRPC.Abstractions.csproj", "src/PulseRPC.Abstractions/"]
COPY ["src/PulseRPC.Server/PulseRPC.Server.csproj", "src/PulseRPC.Server/"]

RUN dotnet restore "src/MyPulseRPCService/MyPulseRPCService.csproj"

# 复制源代码并构建
COPY . .
WORKDIR "/src/src/MyPulseRPCService"
RUN dotnet build "MyPulseRPCService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyPulseRPCService.csproj" -c Release -o /app/publish \
    --no-restore --no-build

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 安全配置
USER pulserpc

# 健康检查
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "MyPulseRPCService.dll"]
```

### ☸️ Kubernetes 配置

```yaml
# ✅ 生产级 Kubernetes 部署
apiVersion: apps/v1
kind: Deployment
metadata:
  name: pulserpc-service
  labels:
    app: pulserpc-service
    version: v1.0.0
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 1
      maxSurge: 1
  selector:
    matchLabels:
      app: pulserpc-service
  template:
    metadata:
      labels:
        app: pulserpc-service
        version: v1.0.0
    spec:
      containers:
      - name: pulserpc-service
        image: pulserpc-service:v1.0.0
        ports:
        - containerPort: 8080
          name: rpc
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: PULSERPC_SERVER_HOST
          value: "0.0.0.0"
        - name: PULSERPC_SERVER_PORT
          value: "8080"
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "1Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 30
          periodSeconds: 10
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 5
          timeoutSeconds: 3
          failureThreshold: 3
        securityContext:
          allowPrivilegeEscalation: false
          runAsNonRoot: true
          runAsUser: 1001
          capabilities:
            drop:
            - ALL
---
apiVersion: v1
kind: Service
metadata:
  name: pulserpc-service
spec:
  selector:
    app: pulserpc-service
  ports:
  - name: rpc
    port: 8080
    targetPort: 8080
  type: ClusterIP
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: pulserpc-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: pulserpc-service
  minReplicas: 3
  maxReplicas: 20
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
  - type: Resource
    resource:
      name: memory
      target:
        type: Utilization
        averageUtilization: 80
```

---

## 命名约定与边界

### 📝 命名约定

| 类型 | 约定 | 示例 |
|------|------|------|
| Hub 接口 | `I{Name}Hub : IPulseHub` | `IGameHub`, `IChatRoomHub` |
| Receiver 接口 | `I{Name}Receiver : IPulseReceiver` | `IGameReceiver`, `IChatReceiver` |
| Hub 实现 | `{Name}Hub` | `GameHub`, `ChatRoomHub` |
| Service 实现 | `{Name}Service : UnifiedPulseServiceBase` | `ChatRoomService`, `PlayerService` |
| 方法命名 | 异步方法以 `Async` 结尾 | `GetPlayerInfoAsync()`, `JoinRoomAsync()` |
| 事件方法 | 以 `On` 开头 | `OnMatchFoundAsync()`, `OnKickedAsync()` |

### 🔐 认证与授权

```csharp
// 接口级别授权
[Channel("GameServer")]
[Authorize(Role = RoleTypes.External)]  // 需要外部认证
public interface IGameHub : IPulseHub
{
    // 允许匿名访问的方法
    [AllowAnonymous]
    Task<LoginResponse> LoginAsync(LoginRequest request);

    // 需要认证的方法（继承接口级别配置）
    Task<PlayerInfo> GetPlayerInfoAsync();
}

// 内部服务接口
[Channel("GameServerInternal")]
[Authorize(Role = RoleTypes.Internal)]  // 仅内部服务可调用
public interface IGameServerInternalHub : IPulseHub
{
    Task<bool> OnMatchFoundAsync(string playerId, MatchFoundNotification notification);
}
```

### 🚧 职责边界

```
┌─────────────────────────────────────────────────────────────┐
│                   IPulseHub 实现                            │
│  ✅ 参数验证                                                │
│  ✅ 身份认证检查                                            │
│  ✅ 路由到对应的 Service                                    │
│  ✅ 简单的数据转换                                          │
│  ❌ 业务状态管理                                            │
│  ❌ 复杂业务逻辑                                            │
└─────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│               UnifiedPulseServiceBase                       │
│  ✅ 业务状态管理                                            │
│  ✅ 复杂业务逻辑                                            │
│  ✅ 数据持久化                                              │
│  ✅ 线程安全保证（通过队列）                                 │
│  ❌ 直接访问网络上下文                                       │
│  ❌ 直接推送消息给客户端（应通过 IHubContext）               │
└─────────────────────────────────────────────────────────────┘
```

---

## 注意事项

### ⚠️ 线程安全

```csharp
// ❌ 错误：在 Hub 中直接访问共享状态
public class BadHub : IGameHub
{
    private readonly Dictionary<string, PlayerInfo> _players = new();  // 危险！

    public Task JoinAsync(string playerId)
    {
        _players[playerId] = new PlayerInfo();  // 多线程并发访问，不安全
        return Task.CompletedTask;
    }
}

// ✅ 正确：使用 Service 的队列保证线程安全
public class GoodHub : IGameHub
{
    private readonly IServiceAccessor<RoomService> _roomService;

    public async Task JoinAsync(string playerId)
    {
        var service = await _roomService.GetAsync("default");
        await service.EnqueueAsync(() => service.AddPlayer(playerId));  // 队列中顺序执行
    }
}
```

### ⚠️ 上下文传播

```csharp
// PulseContext 会自动传播到 Service 队列中
public async Task<JoinRoomResult> JoinRoomAsync(string roomId)
{
    var userId = PulseContext.CurrentUserId;  // Hub 中获取

    var service = await _roomService.GetAsync(roomId);

    return await service.EnqueueAsync(() =>
    {
        // Service 队列中也能访问上下文
        var contextUserId = PulseContext.CurrentUserId;
        return service.JoinAsync(contextUserId);
    });
}
```

### ⚠️ 避免阻塞

```csharp
// ❌ 错误：在 Service 队列中执行长时间同步 I/O
public Task<Data> LoadDataAsync()
{
    // 这会阻塞整个 Service 的消息处理
    var data = database.QuerySync();  // 同步阻塞
    return Task.FromResult(data);
}

// ✅ 正确：使用异步 I/O
public async Task<Data> LoadDataAsync()
{
    return await database.QueryAsync();  // 异步不阻塞
}
```

### ⚠️ 资源清理

```csharp
[PulseService(Scenario = ServiceScenario.Actor)]
public class RoomService : UnifiedPulseServiceBase
{
    private readonly Timer _timer;

    public RoomService(string roomId, ILogger<RoomService> logger)
        : base("Room", roomId, logger)
    {
        _timer = new Timer(Tick, null, 1000, 1000);
    }

    // 重要：在停止时清理资源
    public override async Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        await _timer.DisposeAsync();
        await base.OnStoppingAsync(cancellationToken);
    }
}
```

### ⚠️ 性能优化提示

```csharp
// 1. 批量操作优先
await _hubContext.Clients.Users(userIds).OnEventAsync(data);  // 一次调用
// 而不是
foreach (var userId in userIds)
{
    await _hubContext.Clients.User(userId).OnEventAsync(data);  // N 次调用
}

// 2. 使用 Notify 模式减少延迟
Task HeartbeatAsync();  // 无返回值，发送后立即返回

// 3. 合理设置队列容量
[PulseService(
    Scenario = ServiceScenario.Actor,
    QueueCapacity = 1000,  // 根据业务调整
    BackpressureMode = ServiceBackpressureMode.DropOldest)]  // 队列满时丢弃旧消息
```

---

## 快速参考

### 📤 客户端发送消息

```csharp
// 有返回值
var result = await gameHub.GetPlayerInfoAsync();

// 无返回值
await gameHub.MoveAsync(position);
```

### 📥 服务端推送消息

```csharp
// 向用户推送
await _hubContext.Clients.User(userId).OnEventAsync(data);

// 广播
await _hubContext.Clients.All.OnEventAsync(data);

// 组播
await _hubContext.Clients.Group("room").OnEventAsync(data);
```

### 🏗️ Hub + Service 模式

```csharp
// Hub: 路由和验证
var service = await _roomService.GetAsync(roomId);
return await service.EnqueueAsync(() => service.DoWork());

// Service: 业务逻辑
[PulseService(Scenario = ServiceScenario.Actor)]
public class RoomService : UnifiedPulseServiceBase { }
```

### 🔗 服务间通信

```csharp
// 定义内部接口
[Authorize(Role = RoleTypes.Internal)]
public interface IInternalHub : IPulseHub { }

// 调用远程服务
var channel = await _client.Connections.RouteAsync("ServiceName");
var hub = new IInternalHubStub(channel);
await hub.RemoteMethodAsync(args);
```

---

遵循这些最佳实践将帮助您构建健壮、高性能和可维护的 PulseRPC 应用程序。随着框架的发展，请持续关注最新的实践建议和性能优化技巧。
