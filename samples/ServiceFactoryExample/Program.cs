using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.ServiceManagement;

// ============================================================================
// 示例：使用 IPulseServiceFactory 管理有状态的聊天室服务
// ============================================================================

Console.WriteLine("=== PulseRPC ServiceFactory Example ===\n");

// 1. 配置 DI 容器
var services = new ServiceCollection();

// 添加日志
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// 注册聊天室服务工厂
services.AddPulseServiceFactory<ChatRoomService>(
    (sp, serviceId) =>
    {
        // 从 ServiceId 中提取 roomId
        var roomId = serviceId.Split(':')[1];
        return new ChatRoomService(
            roomId,
            sp.GetRequiredService<ILogger<ChatRoomService>>());
    },
    options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(5);
        options.MaxCachedInstances = 1000;
        options.EnableHealthCheck = true;
    });

// 注册 Hub
services.AddSingleton<ChatRoomHub>();

var serviceProvider = services.BuildServiceProvider();

// 2. 获取服务
var chatHub = serviceProvider.GetRequiredService<ChatRoomHub>();
var metrics = serviceProvider.GetRequiredService<IPulseServiceFactoryMetrics>();

// 3. 使用示例
Console.WriteLine("1. 创建房间并发送消息");
Console.WriteLine("-----------------------------------");

// Room 1
await chatHub.JoinRoomAsync("room-1", "user-1", "Alice");
await chatHub.SendMessageAsync("room-1", "Hello, World!", "user-1", "Alice");
await chatHub.SendMessageAsync("room-1", "How are you?", "user-1", "Alice");

// Room 2
await chatHub.JoinRoomAsync("room-2", "user-2", "Bob");
await chatHub.SendMessageAsync("room-2", "Hi there!", "user-2", "Bob");

Console.WriteLine("\n2. 查看房间消息");
Console.WriteLine("-----------------------------------");

var room1Messages = await chatHub.GetMessagesAsync("room-1");
Console.WriteLine($"Room 1 has {room1Messages.Length} messages:");
foreach (var msg in room1Messages)
{
    Console.WriteLine($"  [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Text}");
}

var room2Messages = await chatHub.GetMessagesAsync("room-2");
Console.WriteLine($"\nRoom 2 has {room2Messages.Length} messages:");
foreach (var msg in room2Messages)
{
    Console.WriteLine($"  [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Text}");
}

Console.WriteLine("\n3. 查看工厂指标");
Console.WriteLine("-----------------------------------");
Console.WriteLine($"Active Instances: {metrics.ActiveInstances}");
Console.WriteLine($"Total Created: {metrics.TotalCreated}");
Console.WriteLine($"Cache Hit Rate: {metrics.CacheHitRate:P2}");
Console.WriteLine($"Cache Hits: {metrics.CacheHits}");
Console.WriteLine($"Cache Misses: {metrics.CacheMisses}");

Console.WriteLine("\n4. 演示缓存复用");
Console.WriteLine("-----------------------------------");

// 再次访问 room-1（应该命中缓存）
await chatHub.SendMessageAsync("room-1", "Another message", "user-1", "Alice");
Console.WriteLine($"After second access to room-1:");
Console.WriteLine($"  Cache Hits: {metrics.CacheHits}");
Console.WriteLine($"  Cache Misses: {metrics.CacheMisses}");
Console.WriteLine($"  Cache Hit Rate: {metrics.CacheHitRate:P2}");

Console.WriteLine("\n5. 获取房间信息");
Console.WriteLine("-----------------------------------");

var room1Info = await chatHub.GetRoomInfoAsync("room-1");
Console.WriteLine($"Room: {room1Info.Name}");
Console.WriteLine($"  Participants: {room1Info.ParticipantCount}");
Console.WriteLine($"  Messages: {room1Info.MessageCount}");
Console.WriteLine($"  Created: {room1Info.CreatedAt:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"  Last Activity: {room1Info.LastActivityAt:yyyy-MM-dd HH:mm:ss}");

Console.WriteLine("\n=== Example Completed ===");

// ============================================================================
// 领域模型
// ============================================================================

public record Message
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public record RoomInfo
{
    public string RoomId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int ParticipantCount { get; init; }
    public int MessageCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
}

// ============================================================================
// Service：有状态的聊天室服务
// ============================================================================

public class ChatRoomService : IPulseService, IServiceLifecycle
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    private readonly string _roomId;
    private readonly ILogger<ChatRoomService> _logger;

    // 业务状态
    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _participants = new();
    private readonly DateTimeOffset _createdAt;
    private DateTimeOffset _lastActivityAt;

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
    {
        _roomId = roomId;
        ServiceId = $"ChatRoom:{roomId}";
        _logger = logger;
        _createdAt = DateTimeOffset.UtcNow;
        _lastActivityAt = _createdAt;

        _logger.LogInformation("ChatRoomService created: {ServiceId}", ServiceId);
    }

    // 业务方法
    public void Join(string userId, string userName)
    {
        _participants.Add(userId);
        _lastActivityAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("User joined: RoomId={RoomId}, UserId={UserId}", _roomId, userId);
    }

    public void AddMessage(Message message)
    {
        _messages.Add(message);
        _lastActivityAt = DateTimeOffset.UtcNow;
        _logger.LogDebug("Message added: RoomId={RoomId}, MessageId={MessageId}", _roomId, message.Id);
    }

    public IReadOnlyList<Message> GetMessages() => _messages;

    public RoomInfo GetRoomInfo()
    {
        return new RoomInfo
        {
            RoomId = _roomId,
            Name = $"Room {_roomId}",
            ParticipantCount = _participants.Count,
            MessageCount = _messages.Count,
            CreatedAt = _createdAt,
            LastActivityAt = _lastActivityAt
        };
    }

    // 生命周期钩子
    public Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ChatRoom activated: {ServiceId}", ServiceId);
        // 这里可以从数据库加载历史消息
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ChatRoom deactivated: {ServiceId}, Messages={MessageCount}",
            ServiceId, _messages.Count);
        // 这里可以保存消息到数据库
        return Task.CompletedTask;
    }

    public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        // 检查消息数量是否超过限制
        var isHealthy = _messages.Count < 10000;
        if (!isHealthy)
        {
            _logger.LogWarning("Health check failed: {ServiceId}, Messages={MessageCount}",
                ServiceId, _messages.Count);
        }
        return Task.FromResult(isHealthy);
    }
}

// ============================================================================
// Hub：无状态的通信契约
// ============================================================================

public class ChatRoomHub
{
    private readonly IPulseServiceFactory<ChatRoomService> _factory;

    public ChatRoomHub(IPulseServiceFactory<ChatRoomService> factory)
    {
        _factory = factory;
    }

    public async Task JoinRoomAsync(string roomId, string userId, string userName)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        service.Join(userId, userName);
    }

    public async Task<Message> SendMessageAsync(string roomId, string text, string userId, string userName)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");

        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            UserName = userName,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow
        };

        service.AddMessage(message);
        return message;
    }

    public async Task<Message[]> GetMessagesAsync(string roomId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        return service.GetMessages().ToArray();
    }

    public async Task<RoomInfo> GetRoomInfoAsync(string roomId)
    {
        var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
        return service.GetRoomInfo();
    }
}
