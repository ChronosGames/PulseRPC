# 服务器间通信设计文档 - 全双工对等架构

> 文档状态：历史设计文档。当前已落地能力以 `IPulseBackplane`、`PulseRPC.Server` 集群相关实现和 `PulseServiceBase` 体系为准；本文中的 `ConcurrentServiceBase` 等旧类型为设计阶段术语。

## 概述

在分布式架构中，不同的服务提供者（GameServer、BattleServer、BackendServer等）需要相互通信。本设计提供了一种基于 **TransportChannel 全双工复用** 的对等通信机制，实现真正的双向 RPC。

## 核心概念澄清

### Hub vs Service

- **Hub（IPulseHub）**：远程接口，定义可调用的方法
- **Service（IPulseService）**：服务端实现，处理业务逻辑
- **TransportChannel**：传输通道，负责双向消息传递

**正确的命名**：
- ✅ `GetHubAsync<T>()` - 获取远程 Hub 代理
- ❌ `GetServiceAsync<T>()` - 不要使用，避免混淆

## 核心理念

### 1. 全双工连接复用

**关键创新**：TransportChannel 层原生支持双向通信

```
GameServer <------- 单一 TCP 连接 -------> BattleServer
    |                                           |
    |--- ClientTransportChannel              ServerTransportChannel ---|
    |       - 发起 RPC 调用                      - 接收 RPC 请求      |
    |       - 注册服务处理器                     - 发起反向调用       |
```

**特性**：
- ✅ **双向 RPC**：一个连接，双向调用
- ✅ **对等通信**：服务器之间是对等关系，不分主从
- ✅ **连接复用**：减少连接数，降低资源消耗
- ✅ **职责清晰**：IPulseClient 和 IPulseServer 保持各自定位

### 2. TransportChannel 双向能力

**ClientTransportChannel**（客户端连接）:
```csharp
// 客户端能力：调用远程 Hub
var battleHub = await channel.GetHubAsync<IBattleHub>();
await battleHub.StartBattleAsync(battleId, playerIds);

// 服务端能力：注册 Hub 实现
channel.RegisterHub<IGameHub>(new GameHubImpl());
```

**ServerTransportChannel**（服务器端接收的连接）:
```csharp
// 服务端能力：注册 Hub 实现
channel.RegisterHub<IBattleHub>(new BattleHubImpl());

// 客户端能力：反向调用客户端注册的 Hub
var gameHub = await channel.GetHubAsync<IGameHub>();
await gameHub.UpdatePlayerStatsAsync(winners, losers);
```

### 3. 职责分离

每个服务提供者只关注自身提供的服务：

```csharp
// GameServer 提供游戏相关服务
[Channel("GameServer")]
public interface IGameHub : IPulseHub
{
    Task<PlayerInfo> GetPlayerInfoAsync(string playerId);
    Task UpdatePlayerStatsAsync(string[] winners, string[] losers);
}

// BattleServer 提供战斗相关服务
[Channel("BattleServer")]
public interface IBattleHub : IPulseHub
{
    Task<BattleInfo> GetBattleInfoAsync(string battleId);
    Task StartBattleAsync(string battleId, string[] playerIds);
}
```

### 4. 自动代理生成

通过源代码生成器自动生成类型安全的代理类和服务处理器，无需手动编写网络通信代码。

## 架构设计

### TransportChannel 层架构

#### TransportChannelBase 抽象基类

```csharp
namespace PulseRPC.Abstractions.Channels;

/// <summary>
/// 传输通道抽象基类 - 实现双向 RPC 的通用逻辑
/// </summary>
public abstract class TransportChannelBase : ITransportChannel
{
    // === 现有成员 ===
    public abstract string ConnectionId { get; }
    public abstract bool IsConnected { get; }
    public abstract Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // === 双向 RPC 实现 ===
    private readonly Dictionary<string, IServiceHandler> _serviceHandlers = new();
    private readonly Dictionary<string, IHubRegistrationToken> _hubRegistrations = new();

    /// <summary>
    /// 获取远程 Hub 代理
    /// </summary>
    public Task<THub> GetHubAsync<THub>() where THub : class, IPulseHub
    {
        // 由代码生成器生成的工厂方法
        return HubProxyFactory.CreateProxy<THub>(this);
    }

    /// <summary>
    /// 注册 Hub 实现
    /// </summary>
    public IHubRegistrationToken RegisterHub<THub>(THub implementation)
        where THub : class, IPulseHub
    {
        // 由代码生成器生成具体实现
        return HubRegistrationHelper.Register(this, implementation);
    }

    /// <summary>
    /// 注册服务处理器（底层API，由代码生成器调用）
    /// </summary>
    public IDisposable RegisterServiceHandler(string serviceName, IServiceHandler handler)
    {
        _serviceHandlers[serviceName] = handler;
        return new HubRegistrationToken(
            typeof(object),
            serviceName,
            () => _serviceHandlers.Remove(serviceName));
    }

    /// <summary>
    /// 处理远程调用（由子类调用）
    /// </summary>
    protected async Task<object?> HandleRemoteInvocationAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        if (_serviceHandlers.TryGetValue(serviceName, out var handler))
        {
            return await handler.HandleRequestAsync(methodName, parameters, context);
        }

        throw new ServiceNotFoundException(serviceName);
    }

    // ... 其他成员
}
```

#### ITransportChannel 接口

```csharp
namespace PulseRPC.Abstractions.Channels;

/// <summary>
/// 传输通道接口 - 支持双向 RPC
/// </summary>
public interface ITransportChannel : IDisposable
{
    // === 现有成员 ===
    string ConnectionId { get; }
    bool IsConnected { get; }
    Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // === 双向 RPC ===
    Task<THub> GetHubAsync<THub>() where THub : class, IPulseHub;
    IHubRegistrationToken RegisterHub<THub>(THub implementation) where THub : class, IPulseHub;
    IDisposable RegisterServiceHandler(string serviceName, IServiceHandler handler);
}
```

#### ClientTransportChannel 实现

```csharp
namespace PulseRPC.Client.Channels;

/// <summary>
/// 客户端传输通道 - 继承双向通信能力
/// </summary>
public class ClientTransportChannel : TransportChannelBase
{
    // === 现有实现 ===
    private readonly ITransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<ResponseMessage>> _pendingRequests;

    public override string ConnectionId { get; }
    public override bool IsConnected => _transport?.IsConnected ?? false;

    // === 发送/接收消息 ===
    public override async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(data, cancellationToken);
    }

    // === 接收消息处理 ===
    protected override async Task OnMessageReceivedAsync(ReadOnlyMemory<byte> data)
    {
        var message = Deserialize(data);

        // 处理响应
        if (message is ResponseMessage response)
        {
            if (_pendingRequests.TryRemove(response.RequestId, out var tcs))
            {
                tcs.SetResult(response);
            }
            return;
        }

        // 处理远程调用（对方调用我们注册的 Hub）
        if (message is RequestMessage request)
        {
            var result = await HandleRemoteInvocationAsync(
                request.ServiceName,
                request.MethodName,
                request.Parameters,
                context);

            await SendResponseAsync(request.RequestId, result);
        }
    }

    // === 继承的双向能力 ===
    // - GetHubAsync<T>() 由基类提供
    // - RegisterHub<T>() 由基类提供
    // - RegisterServiceHandler() 由基类提供
}
```

#### ServerTransportChannel 实现

```csharp
namespace PulseRPC.Server.Channels;

/// <summary>
/// 服务器端传输通道 - 继承双向通信能力
/// </summary>
public class ServerTransportChannel : TransportChannelBase
{
    // === 现有实现 ===
    private readonly IServerTransport _transport;
    private readonly IServiceDispatcher _dispatcher;

    public override string ConnectionId { get; }
    public override bool IsConnected => _transport?.IsConnected ?? false;

    // === 发送/接收消息 ===
    public override async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(ConnectionId, data, cancellationToken);
    }

    // === 接收消息处理 ===
    protected override async Task OnMessageReceivedAsync(ReadOnlyMemory<byte> data)
    {
        var message = Deserialize(data);

        // 处理客户端请求
        if (message is RequestMessage request)
        {
            // 优先检查是否有本地注册的 Hub 处理器
            try
            {
                var result = await HandleRemoteInvocationAsync(
                    request.ServiceName,
                    request.MethodName,
                    request.Parameters,
                    context);

                await SendResponseAsync(request.RequestId, result);
                return;
            }
            catch (ServiceNotFoundException)
            {
                // 如果本地没有注册，则转发到 IPulseService 处理
                await _dispatcher.DispatchAsync(request, context);
            }
        }

        // 处理响应（反向调用的响应）
        if (message is ResponseMessage response)
        {
            // 处理反向调用的响应...
        }
    }

    // === 继承的双向能力 ===
    // - GetHubAsync<T>() 由基类提供
    // - RegisterHub<T>() 由基类提供
    // - RegisterServiceHandler() 由基类提供
}
```

### IPulseClient 和 IPulseServer API

#### IPulseClient 核心 API

```csharp
namespace PulseRPC.Client;

public interface IPulseClient : IAsyncDisposable, IDisposable
{
    // === 现有成员 ===
    string ConnectionId { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    // ... 其他成员

    // === 新增：访问传输通道 ===
    /// <summary>
    /// 获取传输通道（包含所有双向 RPC 能力）
    /// </summary>
    ITransportChannel Channel { get; }
}
```

**使用方式**：
```csharp
// 直接使用 Channel 的能力
var battleHub = await client.Channel.GetHubAsync<IBattleHub>();
client.Channel.RegisterHub<IGameHub>(new GameHubImpl());

// 或者使用扩展方法（可选的语法糖）
var battleHub = await client.GetHubAsync<IBattleHub>();
client.RegisterHub<IGameHub>(new GameHubImpl());
```

#### IPulseServer 核心 API

```csharp
namespace PulseRPC.Server;

public interface IPulseServer : IAsyncDisposable, IDisposable
{
    // === 现有成员 ===
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    // ... 其他成员

    // === 新增：访问传输通道 ===
    /// <summary>
    /// 获取指定连接的传输通道
    /// </summary>
    ITransportChannel? GetChannel(string connectionId);

    /// <summary>
    /// 获取所有活动连接的传输通道
    /// </summary>
    IReadOnlyList<ITransportChannel> GetAllChannels();

    /// <summary>
    /// 当客户端连接时触发
    /// </summary>
    event EventHandler<ClientConnectedEventArgs>? OnClientConnected;
}

public class ClientConnectedEventArgs : EventArgs
{
    public string ConnectionId { get; }
    public ITransportChannel Channel { get; }
    public DateTime ConnectedAt { get; }
}
```

**使用方式**：
```csharp
server.OnClientConnected += async (sender, e) =>
{
    var channel = e.Channel;

    // 直接使用 Channel 的能力
    channel.RegisterHub<IBattleHub>(new BattleHubImpl());
    var gameHub = await channel.GetHubAsync<IGameHub>();

    // 或者使用扩展方法（可选的语法糖）
    server.RegisterHub(e.ConnectionId, new BattleHubImpl());
    var gameHub = await server.GetHubAsync<IGameHub>(e.ConnectionId);
};
```

#### 可选的扩展方法（语法糖）

```csharp
namespace PulseRPC.Client;

public static class PulseClientHubExtensions
{
    public static Task<THub> GetHubAsync<THub>(this IPulseClient client)
        where THub : class, IPulseHub
        => client.Channel.GetHubAsync<THub>();

    public static IHubRegistrationToken RegisterHub<THub>(
        this IPulseClient client, THub implementation)
        where THub : class, IPulseHub
        => client.Channel.RegisterHub(implementation);
}

namespace PulseRPC.Server;

public static class PulseServerHubExtensions
{
    public static async Task<THub> GetHubAsync<THub>(
        this IPulseServer server, string connectionId)
        where THub : class, IPulseHub
    {
        var channel = server.GetChannel(connectionId);
        return channel != null
            ? await channel.GetHubAsync<THub>()
            : throw new ConnectionNotFoundException(connectionId);
    }

    public static IHubRegistrationToken RegisterHub<THub>(
        this IPulseServer server, string connectionId, THub implementation)
        where THub : class, IPulseHub
    {
        var channel = server.GetChannel(connectionId);
        return channel != null
            ? channel.RegisterHub(implementation)
            : throw new ConnectionNotFoundException(connectionId);
    }
}
```

### 代理生成策略

#### 客户端代理（已存在）

客户端源代码生成器已经生成了调用远程服务的代理类：

```csharp
// 自动生成的客户端代理（已有）
internal class BattleHubProxy : IBattleHub
{
    private readonly ITransportChannel _channel;

    public BattleHubProxy(ITransportChannel channel)
    {
        _channel = channel;
    }

    public async Task<BattleInfo> GetBattleInfoAsync(string battleId)
    {
        var request = new RpcRequest
        {
            MethodName = "GetBattleInfoAsync",
            Parameters = new object[] { battleId }
        };

        var response = await _channel.SendRequestAsync(request);
        return response.Deserialize<BattleInfo>();
    }
}
```

#### 服务端处理器生成（新增）

**关键创新**：生成服务端处理器，使 `IPulseClient` 能够接收并处理 RPC 请求

```csharp
// 自动生成的服务端处理器
internal class GameHubServiceHandler : IServiceHandler
{
    private readonly IGameHub _implementation;

    public GameHubServiceHandler(IGameHub implementation)
    {
        _implementation = implementation;
    }

    public async Task<object?> HandleRequestAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        switch (methodName)
        {
            case "GetPlayerInfoAsync":
            {
                var playerId = Deserialize<string>(parameters);
                var result = await _implementation.GetPlayerInfoAsync(playerId);
                return result;
            }

            case "UpdatePlayerStatsAsync":
            {
                var (winners, losers) = Deserialize<(string[], string[])>(parameters);
                await _implementation.UpdatePlayerStatsAsync(winners, losers);
                return null;
            }

            default:
                throw new MethodNotFoundException(methodName);
        }
    }
}
```

#### 服务注册扩展方法生成

```csharp
// 自动生成的扩展方法
public static partial class PulseClientServiceExtensions
{
    public static IServiceRegistrationToken RegisterService<T>(
        this IPulseClient client,
        T implementation)
        where T : class, IPulseHub
    {
        var serviceType = typeof(T);

        // GameHub 注册
        if (serviceType == typeof(IGameHub) || implementation is IGameHub)
        {
            var handler = new GameHubServiceHandler((IGameHub)implementation);
            var token = client.RegisterServiceHandler("IGameHub", handler);
            return new ServiceRegistrationToken(serviceType, "GameServer", token);
        }

        // BattleHub 注册
        if (serviceType == typeof(IBattleHub) || implementation is IBattleHub)
        {
            var handler = new BattleHubServiceHandler((IBattleHub)implementation);
            var token = client.RegisterServiceHandler("IBattleHub", handler);
            return new ServiceRegistrationToken(serviceType, "BattleServer", token);
        }

        throw new ArgumentException(
            $"不支持的服务类型: {serviceType.Name}");
    }
}
```

## 使用场景

### 场景 1: GameServer ↔ BattleServer 全双工通信

```csharp
// ============ GameServer 端 ============

// 1. 连接到 BattleServer
var battleClient = new PulseClient(battleServerHost, battleServerPort);
await battleClient.ConnectAsync();

// 2. 注册 GameServer 自己的 Hub（供 BattleServer 调用）
battleClient.RegisterHub<IGameHub>(new GameHubImpl());

// 3. 调用 BattleServer 的 Hub
public class MatchmakingService : ConcurrentServiceBase
{
    private readonly IPulseClient _battleClient;

    public async Task OnMatchFoundAsync(string[] playerIds)
    {
        // 调用 BattleServer 创建战斗
        var battleHub = await _battleClient.GetHubAsync<IBattleHub>();
        var battleId = await battleHub.CreateBattleAsync(playerIds);

        Console.WriteLine($"Battle created: {battleId}");
    }
}

// 4. 实现 IGameHub，响应 BattleServer 的回调
public class GameHubImpl : IGameHub
{
    private readonly IDatabase _database;

    public async Task UpdatePlayerStatsAsync(string[] winners, string[] losers)
    {
        // BattleServer 战斗结束后会调用此方法更新玩家数据
        Console.WriteLine($"Updating stats: {winners.Length} winners, {losers.Length} losers");
        await _database.UpdatePlayerStatsAsync(winners, losers);
    }

    public Task<PlayerInfo> GetPlayerInfoAsync(string playerId)
    {
        return _database.GetPlayerInfoAsync(playerId);
    }
}
```

```csharp
// ============ BattleServer 端 ============

// 1. 启动服务器，等待 GameServer 连接
var server = new PulseServer();
await server.StartAsync();

// 2. 当 GameServer 连接进来时，注册 Hub 并调用对方
server.OnClientConnected += async (sender, e) =>
{
    var connectionId = e.ConnectionId;

    // 注册 BattleServer 自己的 Hub（供 GameServer 调用）
    server.RegisterHub(connectionId, new BattleHubImpl(server, connectionId));

    // 调用 GameServer 注册的 Hub
    var gameHub = await server.GetHubAsync<IGameHub>(connectionId);
    var playerInfo = await gameHub.GetPlayerInfoAsync("player-123");

    Console.WriteLine($"GameServer connected, player: {playerInfo.Name}");
};

// 3. 实现 IBattleHub，响应 GameServer 的调用
public class BattleHubImpl : IBattleHub
{
    private readonly IPulseServer _server;
    private readonly string _connectionId;

    public BattleHubImpl(IPulseServer server, string connectionId)
    {
        _server = server;
        _connectionId = connectionId;
    }

    public async Task<string> CreateBattleAsync(string[] playerIds)
    {
        var battleId = GenerateBattleId();

        // 创建战斗...
        await _battleManager.CreateBattleAsync(battleId, playerIds);

        return battleId;
    }

    public async Task OnBattleEndedAsync(string battleId)
    {
        var result = await _battleManager.GetResultAsync(battleId);

        // 回调 GameServer 更新玩家数据
        var gameHub = await _server.GetHubAsync<IGameHub>(_connectionId);
        await gameHub.UpdatePlayerStatsAsync(result.Winners, result.Losers);
    }
}
```

### 场景 2: 多服务器协作（GameServer + BattleServer + ChatServer）

```csharp
// ============ GameServer 启动配置 ============
public class GameServerStartup
{
    private IPulseClient _battleClient;
    private IPulseClient _chatClient;

    public async Task InitializeAsync()
    {
        // 连接到 BattleServer
        _battleClient = new PulseClient("battle-server", 5001);
        await _battleClient.ConnectAsync();
        _battleClient.RegisterHub<IGameHub>(new GameHubImpl());

        // 连接到 ChatServer
        _chatClient = new PulseClient("chat-server", 5002);
        await _chatClient.ConnectAsync();
        _chatClient.RegisterHub<IGameHub>(new GameHubImpl());

        Console.WriteLine("GameServer connected to BattleServer and ChatServer");
    }

    public async Task CreateMatchAsync(string[] playerIds)
    {
        // 调用 BattleServer
        var battleHub = await _battleClient.GetHubAsync<IBattleHub>();
        var battleId = await battleHub.CreateBattleAsync(playerIds);

        // 调用 ChatServer 创建战斗房间
        var chatHub = await _chatClient.GetHubAsync<IChatHub>();
        await chatHub.CreateBattleRoomAsync(battleId, playerIds);
    }
}
```

### 场景 3: 服务器集群（多个 GameServer 实例）

```csharp
// ============ BackendServer 管理多个 GameServer ============
public class BackendServer
{
    private readonly List<string> _gameServerConnections = new();
    private readonly PulseServer _server;

    public async Task StartAsync()
    {
        _server = new PulseServer();

        // 当 GameServer 连接进来时
        _server.OnClientConnected += async (sender, e) =>
        {
            var connectionId = e.ConnectionId;

            // 注册 BackendServer 的管理 Hub
            _server.RegisterHub(connectionId, new BackendHubImpl());

            // 保存 GameServer 连接ID
            _gameServerConnections.Add(connectionId);

            Console.WriteLine($"GameServer connected: {connectionId}");
        };

        await _server.StartAsync();
    }

    // 广播系统公告到所有 GameServer
    public async Task BroadcastAnnouncementAsync(string announcement)
    {
        foreach (var connectionId in _gameServerConnections)
        {
            try
            {
                var gameHub = await _server.GetHubAsync<IGameHub>(connectionId);
                await gameHub.BroadcastAnnouncementAsync(announcement);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send to {connectionId}: {ex.Message}");
            }
        }
    }
}
```

## 连接管理

### 统一连接池设计

**核心理念**：使用统一的连接池管理所有 `ITransportChannel` 实例，实现真正的连接复用。

#### TransportChannelPool - 连接池

```csharp
namespace PulseRPC.Abstractions.Channels;

/// <summary>
/// 传输通道连接池 - 管理所有活动连接
/// </summary>
public interface ITransportChannelPool
{
    /// <summary>
    /// 注册连接到连接池
    /// </summary>
    void Register(string connectionId, ITransportChannel channel);

    /// <summary>
    /// 从连接池移除连接
    /// </summary>
    bool Unregister(string connectionId);

    /// <summary>
    /// 获取指定连接
    /// </summary>
    ITransportChannel? GetChannel(string connectionId);

    /// <summary>
    /// 获取所有活动连接
    /// </summary>
    IReadOnlyCollection<ITransportChannel> GetAllChannels();

    /// <summary>
    /// 获取所有活动连接ID
    /// </summary>
    IReadOnlyCollection<string> GetAllConnectionIds();

    /// <summary>
    /// 检查连接是否存在
    /// </summary>
    bool Contains(string connectionId);

    /// <summary>
    /// 活动连接数量
    /// </summary>
    int Count { get; }
}

/// <summary>
/// 连接池默认实现
/// </summary>
public sealed class TransportChannelPool : ITransportChannelPool
{
    private readonly ConcurrentDictionary<string, ITransportChannel> _channels = new();

    public void Register(string connectionId, ITransportChannel channel)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentException("ConnectionId cannot be null or empty", nameof(connectionId));

        if (channel == null)
            throw new ArgumentNullException(nameof(channel));

        _channels[connectionId] = channel;
    }

    public bool Unregister(string connectionId)
    {
        return _channels.TryRemove(connectionId, out _);
    }

    public ITransportChannel? GetChannel(string connectionId)
    {
        return _channels.TryGetValue(connectionId, out var channel) ? channel : null;
    }

    public IReadOnlyCollection<ITransportChannel> GetAllChannels()
    {
        return _channels.Values.ToList().AsReadOnly();
    }

    public IReadOnlyCollection<string> GetAllConnectionIds()
    {
        return _channels.Keys.ToList().AsReadOnly();
    }

    public bool Contains(string connectionId)
    {
        return _channels.ContainsKey(connectionId);
    }

    public int Count => _channels.Count;
}
```

### IPulseServer 访问 TransportChannel

**关键设计**：`IPulseServer` 通过连接池访问 `ServerTransportChannel`

```csharp
public interface IPulseServer
{
    // 现有成员...

    /// <summary>
    /// 获取指定连接的传输通道
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>传输通道实例，如果连接不存在返回 null</returns>
    ITransportChannel? GetChannel(string connectionId);

    /// <summary>
    /// 获取所有活动连接的传输通道
    /// </summary>
    IReadOnlyList<ITransportChannel> GetAllChannels();

    /// <summary>
    /// 获取连接池（用于高级场景）
    /// </summary>
    ITransportChannelPool ChannelPool { get; }

    /// <summary>
    /// 当客户端连接时触发
    /// </summary>
    event EventHandler<ClientConnectedEventArgs>? OnClientConnected;

    /// <summary>
    /// 当客户端断开时触发
    /// </summary>
    event EventHandler<ClientDisconnectedEventArgs>? OnClientDisconnected;
}

public class ClientConnectedEventArgs : EventArgs
{
    public string ConnectionId { get; }
    public ITransportChannel Channel { get; }
    public DateTime ConnectedAt { get; }

    public ClientConnectedEventArgs(string connectionId, ITransportChannel channel)
    {
        ConnectionId = connectionId;
        Channel = channel;
        ConnectedAt = DateTime.UtcNow;
    }
}

public class ClientDisconnectedEventArgs : EventArgs
{
    public string ConnectionId { get; }
    public DateTime DisconnectedAt { get; }
    public DisconnectReason Reason { get; }

    public ClientDisconnectedEventArgs(string connectionId, DisconnectReason reason)
    {
        ConnectionId = connectionId;
        DisconnectedAt = DateTime.UtcNow;
        Reason = reason;
    }
}

public enum DisconnectReason
{
    Normal,
    Timeout,
    Error,
    ServerShutdown
}
```

### 服务器端使用示例

```csharp
public class BattleServer
{
    private readonly PulseServer _server;
    private readonly Dictionary<string, ITransportChannel> _gameServerChannels = new();

    public async Task StartAsync()
    {
        _server = new PulseServer();

        // 监听 GameServer 连接
        _server.OnClientConnected += HandleGameServerConnected;

        await _server.StartAsync();
        Console.WriteLine("BattleServer started on port 5001");
    }

    private async void HandleGameServerConnected(object? sender, ClientConnectedEventArgs e)
    {
        var connectionId = e.ConnectionId;
        var channel = e.Channel;

        // 在通道上注册 BattleServer 的 Hub
        channel.RegisterHub<IBattleHub>(new BattleHubImpl(_server, connectionId));

        // 保存通道引用
        _gameServerChannels[connectionId] = channel;

        // 调用 GameServer 的 Hub
        var gameHub = await channel.GetHubAsync<IGameHub>();
        var serverInfo = await gameHub.GetServerInfoAsync();

        Console.WriteLine($"GameServer connected: {serverInfo.ServerId}");
    }
}
```

### 连接管理器（可选）

对于需要管理多个连接的场景：

```csharp
public class ServerConnectionManager
{
    private readonly Dictionary<string, ConnectionInfo> _connections = new();

    public void RegisterConnection(string connectionId, string channel, ITransportChannel transportChannel)
    {
        _connections[connectionId] = new ConnectionInfo
        {
            ConnectionId = connectionId,
            ChannelName = channel,
            TransportChannel = transportChannel,
            ConnectedAt = DateTime.UtcNow
        };
    }

    public ITransportChannel? GetChannel(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var info)
            ? info.TransportChannel
            : null;
    }

    public IEnumerable<ITransportChannel> GetChannelsByName(string channelName)
    {
        return _connections.Values
            .Where(info => info.ChannelName == channelName)
            .Select(info => info.TransportChannel);
    }
}

public class ConnectionInfo
{
    public string ConnectionId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public ITransportChannel TransportChannel { get; set; } = null!;
    public DateTime ConnectedAt { get; set; }
}
```

## 实现计划

### 第零阶段：连接池基础设施

**目标**：创建统一的连接池管理，支持 `ConnectionId => ITransportChannel` 映射

#### 0.1 创建 ITransportChannelPool 接口

```csharp
// src/PulseRPC.Abstractions/Channels/ITransportChannelPool.cs
namespace PulseRPC.Abstractions.Channels;

public interface ITransportChannelPool
{
    void Register(string connectionId, ITransportChannel channel);
    bool Unregister(string connectionId);
    ITransportChannel? GetChannel(string connectionId);
    IReadOnlyCollection<ITransportChannel> GetAllChannels();
    IReadOnlyCollection<string> GetAllConnectionIds();
    bool Contains(string connectionId);
    int Count { get; }
}
```

#### 0.2 实现 TransportChannelPool

```csharp
// src/PulseRPC.Abstractions/Channels/TransportChannelPool.cs
namespace PulseRPC.Abstractions.Channels;

public sealed class TransportChannelPool : ITransportChannelPool
{
    private readonly ConcurrentDictionary<string, ITransportChannel> _channels = new();

    // 实现所有接口方法...
}
```

#### 0.3 扩展 IPulseServer 接口

```csharp
// src/PulseRPC.Server/IPulseServer.cs
public interface IPulseServer
{
    // 新增成员
    ITransportChannel? GetChannel(string connectionId);
    IReadOnlyList<ITransportChannel> GetAllChannels();
    ITransportChannelPool ChannelPool { get; }
    event EventHandler<ClientConnectedEventArgs>? OnClientConnected;
    event EventHandler<ClientDisconnectedEventArgs>? OnClientDisconnected;
}
```

#### 0.4 更新 PulseServer 实现

```csharp
// src/PulseRPC.Server/PulseServer.cs
public partial class PulseServer : IPulseServer
{
    private readonly ITransportChannelPool _channelPool;

    public PulseServer()
    {
        _channelPool = new TransportChannelPool();
    }

    public ITransportChannelPool ChannelPool => _channelPool;

    public ITransportChannel? GetChannel(string connectionId)
        => _channelPool.GetChannel(connectionId);

    public IReadOnlyList<ITransportChannel> GetAllChannels()
        => _channelPool.GetAllChannels().ToList();

    // 在连接建立时自动注册到连接池
    private void OnTransportConnected(string connectionId, ITransportChannel channel)
    {
        _channelPool.Register(connectionId, channel);
        OnClientConnected?.Invoke(this, new ClientConnectedEventArgs(connectionId, channel));
    }

    // 在连接断开时自动从连接池移除
    private void OnTransportDisconnected(string connectionId, DisconnectReason reason)
    {
        _channelPool.Unregister(connectionId);
        OnClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(connectionId, reason));
    }
}
```

### 第一阶段：TransportChannelBase 基类设计

**目标**：创建 `TransportChannelBase` 抽象基类，实现双向 RPC 的通用逻辑

#### 1.1 扩展 ITransportChannel 接口

```csharp
// src/PulseRPC.Abstractions/Channels/ITransportChannel.cs
public interface ITransportChannel
{
    // 现有成员...

    /// <summary>
    /// 获取远程 Hub 代理
    /// </summary>
    Task<THub> GetHubAsync<THub>() where THub : class, IPulseHub;

    /// <summary>
    /// 注册 Hub 实现
    /// </summary>
    IHubRegistrationToken RegisterHub<THub>(THub implementation) where THub : class, IPulseHub;

    /// <summary>
    /// 注册服务处理器（底层API，由代码生成器调用）
    /// </summary>
    IDisposable RegisterServiceHandler(string serviceName, IServiceHandler handler);
}
```

#### 1.2 创建 TransportChannelBase 抽象基类

```csharp
// src/PulseRPC.Abstractions/Channels/TransportChannelBase.cs
namespace PulseRPC.Abstractions.Channels;

public abstract class TransportChannelBase : ITransportChannel
{
    private readonly Dictionary<string, IServiceHandler> _serviceHandlers = new();
    private readonly Dictionary<string, IHubRegistrationToken> _hubRegistrations = new();

    // 抽象成员（由子类实现）
    public abstract string ConnectionId { get; }
    public abstract bool IsConnected { get; }
    public abstract Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    // 双向 RPC 实现（所有子类共享）
    public Task<THub> GetHubAsync<THub>() where THub : class, IPulseHub
    {
        return HubProxyFactory.CreateProxy<THub>(this);
    }

    public IHubRegistrationToken RegisterHub<THub>(THub implementation)
        where THub : class, IPulseHub
    {
        return HubRegistrationHelper.Register(this, implementation);
    }

    public IDisposable RegisterServiceHandler(string serviceName, IServiceHandler handler)
    {
        _serviceHandlers[serviceName] = handler;
        return new HubRegistrationToken(
            typeof(object),
            serviceName,
            () => _serviceHandlers.Remove(serviceName));
    }

    protected async Task<object?> HandleRemoteInvocationAsync(
        string serviceName,
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        if (_serviceHandlers.TryGetValue(serviceName, out var handler))
        {
            return await handler.HandleRequestAsync(methodName, parameters, context);
        }

        throw new ServiceNotFoundException(serviceName);
    }
}
```

#### 1.2 定义服务处理器接口

```csharp
// src/PulseRPC.Abstractions/IServiceHandler.cs
public interface IServiceHandler
{
    Task<object?> HandleRequestAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context);
}
```

#### 1.3 定义 Hub 注册令牌

```csharp
// src/PulseRPC.Abstractions/IHubRegistrationToken.cs
public interface IHubRegistrationToken : IDisposable
{
    /// <summary>
    /// Hub 接口类型
    /// </summary>
    Type HubType { get; }

    /// <summary>
    /// Channel 名称
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// 是否已取消注册
    /// </summary>
    bool IsUnregistered { get; }

    /// <summary>
    /// 取消注册
    /// </summary>
    void Unregister();
}
```

### 第二阶段：ClientTransportChannel 迁移到 TransportChannelBase

**目标**：让 `ClientTransportChannel` 继承 `TransportChannelBase`，复用双向 RPC 逻辑

#### 2.1 更新 ClientTransportChannel

```csharp
// src/PulseRPC.Client/Channels/ClientTransportChannel.cs
namespace PulseRPC.Client.Channels;

/// <summary>
/// 客户端传输通道 - 继承 TransportChannelBase 获得双向通信能力
/// </summary>
public class ClientTransportChannel : TransportChannelBase
{
    private readonly ITransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<ResponseMessage>> _pendingRequests = new();

    public override string ConnectionId { get; }
    public override bool IsConnected => _transport?.IsConnected ?? false;

    public override async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(data, cancellationToken);
    }

    // 接收消息处理
    protected override async Task OnMessageReceivedAsync(ReadOnlyMemory<byte> data)
    {
        var message = Deserialize(data);

        // 处理响应
        if (message is ResponseMessage response)
        {
            if (_pendingRequests.TryRemove(response.RequestId, out var tcs))
            {
                tcs.SetResult(response);
            }
            return;
        }

        // 处理远程调用（服务器调用我们注册的 Hub）
        if (message is RequestMessage request)
        {
            var result = await HandleRemoteInvocationAsync(
                request.ServiceName,
                request.MethodName,
                request.Parameters,
                context);

            await SendResponseAsync(request.RequestId, result);
        }
    }

    // GetHubAsync<T>() 和 RegisterHub<T>() 由基类提供，无需重新实现
}
```

#### 2.2 更新 IPulseClient

```csharp
// src/PulseRPC.Client/IPulseClient.cs
public interface IPulseClient : IAsyncDisposable, IDisposable
{
    // 现有成员...

    /// <summary>
    /// 获取传输通道（包含所有双向 RPC 能力）
    /// </summary>
    ITransportChannel Channel { get; }
}
```

### 第三阶段：ServerTransportChannel 迁移到 TransportChannelBase

**目标**：让 `ServerTransportChannel` 继承 `TransportChannelBase`，支持反向调用

#### 3.1 更新 ServerTransportChannel

```csharp
// src/PulseRPC.Server/Channels/ServerTransportChannel.cs
namespace PulseRPC.Server.Channels;

/// <summary>
/// 服务器端传输通道 - 继承 TransportChannelBase 获得双向通信能力
/// </summary>
public class ServerTransportChannel : TransportChannelBase
{
    private readonly IServerTransport _transport;
    private readonly IServiceDispatcher _dispatcher;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<ResponseMessage>> _pendingRequests = new();

    public override string ConnectionId { get; }
    public override bool IsConnected => _transport?.IsConnected ?? false;

    public override async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(ConnectionId, data, cancellationToken);
    }

    // 接收消息处理
    protected override async Task OnMessageReceivedAsync(ReadOnlyMemory<byte> data)
    {
        var message = Deserialize(data);

        // 处理客户端请求
        if (message is RequestMessage request)
        {
            // 优先检查是否有本地注册的 Hub 处理器
            try
            {
                var result = await HandleRemoteInvocationAsync(
                    request.ServiceName,
                    request.MethodName,
                    request.Parameters,
                    context);

                await SendResponseAsync(request.RequestId, result);
                return;
            }
            catch (ServiceNotFoundException)
            {
                // 如果本地没有注册，则转发到 IPulseService 处理
                await _dispatcher.DispatchAsync(request, context);
            }
        }

        // 处理响应（反向调用的响应）
        if (message is ResponseMessage response)
        {
            if (_pendingRequests.TryRemove(response.RequestId, out var tcs))
            {
                tcs.SetResult(response);
            }
        }
    }

    // GetHubAsync<T>() 和 RegisterHub<T>() 由基类提供，无需重新实现
}
```

#### 3.2 更新 PulseServer 实现（已在第零阶段完成）

连接池相关功能已在第零阶段实现，此阶段只需确保 ServerTransportChannel 正确注册到连接池。

### 第四阶段：代理生成器扩展

**目标**：生成双向通信的代理和处理器

#### 4.1 客户端生成器扩展

扩展 `PulseRPC.Client.SourceGenerator`：
- 为每个 `IPulseHub` 接口生成 Hub 代理（已有）
- **新增**：生成服务处理器类 `{InterfaceName}ServiceHandler`
- **新增**：生成 `RegisterHub<T>` 扩展方法的具体实现

#### 4.2 服务端生成器扩展

扩展 `PulseRPC.Server.SourceGenerator`：
- **新增**：生成服务处理器类（与客户端类似）
- **新增**：生成 `GetHubAsync<T>` 扩展方法（用于反向调用）
- **新增**：生成 `RegisterHub<T>` 扩展方法的具体实现

#### 4.3 生成代码示例

```csharp
// 自动生成的服务处理器
internal class GameHubServiceHandler : IServiceHandler
{
    private readonly IGameHub _implementation;

    public GameHubServiceHandler(IGameHub implementation)
    {
        _implementation = implementation;
    }

    public async Task<object?> HandleRequestAsync(
        string methodName,
        ReadOnlyMemory<byte> parameters,
        IRequestContext context)
    {
        switch (methodName)
        {
            case "GetPlayerInfoAsync":
                var playerId = Deserialize<string>(parameters);
                return await _implementation.GetPlayerInfoAsync(playerId);

            case "UpdatePlayerStatsAsync":
                var (winners, losers) = Deserialize<(string[], string[])>(parameters);
                await _implementation.UpdatePlayerStatsAsync(winners, losers);
                return null;

            default:
                throw new MethodNotFoundException(methodName);
        }
    }
}
```

### 第五阶段：测试和示例

#### 5.1 单元测试

- TransportChannel 双向能力测试
- 服务注册和取消注册测试
- Hub 代理生成测试
- 双向 RPC 调用测试

#### 5.2 集成测试

- GameServer ↔ BattleServer 全双工通信
- 多服务器协作场景
- 服务器集群广播
- 连接断开时的清理测试

#### 5.3 示例项目

创建 `samples/ServerToServer/` 示例：
- GameServer 项目
- BattleServer 项目
- 共享接口项目（定义 IPulseHub 接口）
- Docker Compose 配置

## 核心优势

### 1. 真正的全双工通信

- ✅ 一个 TCP 连接，双向 RPC
- ✅ 无需建立反向连接
- ✅ 降低连接数和资源消耗

### 2. 对等架构

- ✅ 服务器之间是对等关系
- ✅ 每个服务器既是客户端也是服务器
- ✅ 灵活的拓扑结构

### 3. 类型安全

- ✅ 编译时检查
- ✅ 自动生成代理和处理器
- ✅ 避免运行时错误

### 4. 简化架构

- ✅ 无需额外的连接注册表
- ✅ 复用现有的 IPulseClient
- ✅ 统一的 API 设计

## 技术挑战和解决方案

### 挑战 1: 生命周期管理

**问题**：服务注册的生命周期如何管理？

**解决方案**：
- 使用 `IServiceRegistrationToken` 管理生命周期
- 连接断开时自动清理注册
- 支持手动取消注册

## 后续扩展

1. **服务发现**：自动发现和连接到其他服务器
2. **负载均衡**：多个相同服务的实例，自动负载均衡
3. **健康检查**：定期检查连接健康状态，自动重连
4. **请求追踪**：分布式追踪和监控
5. **认证授权**：服务器间认证和授权机制

---

**相关文档**：
- [IPulseHub 统一架构指南](../../concepts/rpc-model.md)
- 事件监听器重构总结 - 旧独立文档当前仓库未提供
- ChannelAttribute 使用指南 - `specs/001-channelattribute-servicename-ipulsehub/spec.md` 当前仓库未提供
