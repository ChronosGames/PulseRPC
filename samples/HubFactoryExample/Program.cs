using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Services;

// ============================================================================
// 示例：无状态 Hub + IServiceAccessor<TService>（替代旧 HubFactory）
// ============================================================================

Console.WriteLine("=== PulseRPC IServiceAccessor Example ===\n");
Console.WriteLine("Hub 由标准 DI 管理，有状态实例统一交给 PulseServiceManager。\n");

// 1. 配置 DI 容器
var services = new ServiceCollection();

// 添加日志
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

services.AddPulseServiceManagement(options =>
{
    options.CleanupInterval = TimeSpan.FromMinutes(1);
    options.MaxCachedInstances = 1000;
});
services.AddPulseService<ChatRoomService>((sp, roomId) =>
    new ChatRoomService(
        roomId,
        sp.GetRequiredService<ILogger<ChatRoomService>>()));
services.AddSingleton<ChatRoomHub>();

await using var serviceProvider = services.BuildServiceProvider();

// 2. Hub 是无状态单例；Service accessor 负责 keyed 生命周期。
var hub = serviceProvider.GetRequiredService<ChatRoomHub>();
var rooms = serviceProvider.GetRequiredService<IServiceAccessor<ChatRoomService>>();

Console.WriteLine("===== 使用方式对比 =====\n");

Console.WriteLine("❌ 旧方式：每个 Service 再缓存一个 Hub 实例");
Console.WriteLine("```csharp");
Console.WriteLine("public async Task SendMessageAsync(string roomId, string text)");
Console.WriteLine("{");
Console.WriteLine("    var hub = await _hubFactory.GetOrCreateAsync($\"ChatRoom:{roomId}\");");
Console.WriteLine("    await hub.SendMessageAsync(text);");
Console.WriteLine("}");
Console.WriteLine("```\n");

Console.WriteLine("✅ 新方式：无状态 Hub 注入 IServiceAccessor");
Console.WriteLine("```csharp");
Console.WriteLine("public async Task SendMessageAsync(string roomId, string text)");
Console.WriteLine("{");
Console.WriteLine("    var room = await _rooms.GetAsync(roomId);");
Console.WriteLine("    room.AddMessage(new Message { Text = text });");
Console.WriteLine("}");
Console.WriteLine("```\n");

Console.WriteLine("===== 实际使用 =====\n");

// 3. 使用同一个无状态 Hub 路由到不同 Service 实例
Console.WriteLine("1. 创建房间并发送消息");
Console.WriteLine("-----------------------------------");

await hub.JoinAsync("room-1", "user-1", "Alice");
await hub.SendMessageAsync("room-1", "Hello, World!");
await hub.SendMessageAsync("room-1", "How are you?");

await hub.JoinAsync("room-2", "user-2", "Bob");
await hub.SendMessageAsync("room-2", "Hi there!");

Console.WriteLine("\n2. 查看房间消息");
Console.WriteLine("-----------------------------------");

var room1Messages = await hub.GetMessagesAsync("room-1");
Console.WriteLine($"Room 1 has {room1Messages.Length} messages:");
foreach (var msg in room1Messages)
{
    Console.WriteLine($"  [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Text}");
}

var room2Messages = await hub.GetMessagesAsync("room-2");
Console.WriteLine($"\nRoom 2 has {room2Messages.Length} messages:");
foreach (var msg in room2Messages)
{
    Console.WriteLine($"  [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Text}");
}

Console.WriteLine("\n3. 验证 Service 实例缓存");
Console.WriteLine("-----------------------------------");

var room1 = await rooms.GetAsync("room-1");
var room1Again = await rooms.GetAsync("room-1");
Console.WriteLine($"room1 == room1Again: {ReferenceEquals(room1, room1Again)}");
Console.WriteLine($"Active Service Count: {rooms.ActiveCount}");

Console.WriteLine("\n4. Hub 和 Service 的关系");
Console.WriteLine("-----------------------------------");
Console.WriteLine("✅ Hub 无状态，由标准 DI 管理");
Console.WriteLine("✅ 所有 keyed Service 由同一个 PulseServiceManager 管理");
Console.WriteLine("✅ IServiceAccessor 是唯一访问与释放入口");

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

// ============================================================================
// Service：有状态的聊天室服务
// ============================================================================

public class ChatRoomService : PulseServiceBase
{
    private readonly string _roomId;
    private readonly ILogger<ChatRoomService> _logger;

    // 业务状态
    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _participants = new();

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
        : base("ChatRoom", roomId, logger)
    {
        _roomId = roomId;
        _logger = logger;
        _logger.LogInformation("ChatRoomService created: {ServiceType}:{ServiceId}", ServiceType, ServiceId);
    }

    // 业务方法
    public void Join(string userId, string userName)
    {
        _participants.Add(userId);
        _logger.LogInformation("User joined: RoomId={RoomId}, UserId={UserId}", _roomId, userId);
    }

    public void AddMessage(Message message)
    {
        _messages.Add(message);
        _logger.LogDebug("Message added: RoomId={RoomId}, MessageId={MessageId}", _roomId, message.Id);
    }

    public IReadOnlyList<Message> GetMessages() => _messages;
}

// ============================================================================
// Hub：无状态的通信边界，通过 accessor 定位有状态 Service
// ============================================================================

public class ChatRoomHub
{
    private readonly IServiceAccessor<ChatRoomService> _rooms;

    public ChatRoomHub(IServiceAccessor<ChatRoomService> rooms)
    {
        _rooms = rooms;
    }

    public async Task JoinAsync(string roomId, string userId, string userName)
    {
        var room = await _rooms.GetAsync(roomId);
        room.Join(userId, userName);
    }

    public async Task SendMessageAsync(string roomId, string text)
    {
        var room = await _rooms.GetAsync(roomId);

        // 从上下文获取当前用户信息（简化示例，实际应从认证信息获取）
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "current-user",
            UserName = "Current User",
            Text = text,
            Timestamp = DateTimeOffset.UtcNow
        };

        room.AddMessage(message);
    }

    public async Task<Message[]> GetMessagesAsync(string roomId)
    {
        var room = await _rooms.GetAsync(roomId);
        return room.GetMessages().ToArray();
    }
}
