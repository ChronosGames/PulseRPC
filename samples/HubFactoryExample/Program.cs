using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Extensions;

// ============================================================================
// 示例：使用 IPulseHubFactory 简化代码 - Hub 和 Service 一对一绑定
// ============================================================================

Console.WriteLine("=== PulseRPC HubFactory Example ===\n");
Console.WriteLine("展示改进后的使用方式：Hub 直接注入 Service，代码更简洁\n");

// 1. 配置 DI 容器
var services = new ServiceCollection();

// 添加日志
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

// 先注册 ServiceFactory
services.AddPulseServiceFactory<ChatRoomService>(
    (sp, serviceId) =>
    {
        return new ChatRoomService(
            serviceId,
            sp.GetRequiredService<ILogger<ChatRoomService>>());
    },
    options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(5);
        options.MaxCachedInstances = 1000;
    });

// 再注册 HubFactory（Hub 和 Service 绑定）
services.AddPulseHubFactory<ChatRoomHub, ChatRoomService>(
    service => new ChatRoomHub(service));

var serviceProvider = services.BuildServiceProvider();

// 2. 获取 HubFactory
var hubFactory = serviceProvider.GetRequiredService<IPulseHubFactory<ChatRoomHub, ChatRoomService>>();

Console.WriteLine("===== 使用方式对比 =====\n");

Console.WriteLine("❌ 旧方式：每次都要调用 Factory，传入 roomId");
Console.WriteLine("```csharp");
Console.WriteLine("public async Task SendMessageAsync(string roomId, string text)");
Console.WriteLine("{");
Console.WriteLine("    var service = await _factory.GetOrCreateAsync($\"ChatRoom:{roomId}\");");
Console.WriteLine("    service.AddMessage(new Message { Text = text });");
Console.WriteLine("}");
Console.WriteLine("```\n");

Console.WriteLine("✅ 新方式：Service 直接注入，不需要 roomId 参数");
Console.WriteLine("```csharp");
Console.WriteLine("public Task SendMessageAsync(string text)");
Console.WriteLine("{");
Console.WriteLine("    _service.AddMessage(new Message { Text = text });");
Console.WriteLine("    return Task.CompletedTask;");
Console.WriteLine("}");
Console.WriteLine("```\n");

Console.WriteLine("===== 实际使用 =====\n");

// 3. 使用 HubFactory 获取 Hub 实例
Console.WriteLine("1. 创建房间并发送消息");
Console.WriteLine("-----------------------------------");

// Room 1
var hub1 = await hubFactory.GetOrCreateAsync("room-1");
await hub1.JoinAsync("user-1", "Alice");
await hub1.SendMessageAsync("Hello, World!");
await hub1.SendMessageAsync("How are you?");

// Room 2
var hub2 = await hubFactory.GetOrCreateAsync("room-2");
await hub2.JoinAsync("user-2", "Bob");
await hub2.SendMessageAsync("Hi there!");

Console.WriteLine("\n2. 查看房间消息");
Console.WriteLine("-----------------------------------");

var room1Messages = await hub1.GetMessagesAsync();
Console.WriteLine($"Room 1 has {room1Messages.Length} messages:");
foreach (var msg in room1Messages)
{
    Console.WriteLine($"  [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Text}");
}

var room2Messages = await hub2.GetMessagesAsync();
Console.WriteLine($"\nRoom 2 has {room2Messages.Length} messages:");
foreach (var msg in room2Messages)
{
    Console.WriteLine($"  [{msg.Timestamp:HH:mm:ss}] {msg.UserName}: {msg.Text}");
}

Console.WriteLine("\n3. 验证 Hub 实例缓存");
Console.WriteLine("-----------------------------------");

// 再次获取 room-1 的 Hub，应该返回同一个实例
var hub1Again = await hubFactory.GetOrCreateAsync("ChatRoom:room-1");
Console.WriteLine($"hub1 == hub1Again: {ReferenceEquals(hub1, hub1Again)}");
Console.WriteLine($"Active Hub Count: {hubFactory.ActiveCount}");

Console.WriteLine("\n4. Hub 和 Service 的关系");
Console.WriteLine("-----------------------------------");
Console.WriteLine("✅ 每个 Hub 实例对应一个 Service 实例");
Console.WriteLine("✅ Hub 直接持有 Service 引用，无需每次查找");
Console.WriteLine("✅ Hub 和 Service 生命周期自动同步");

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

public class ChatRoomService : IPulseService
{
    public string ServiceName => "ChatRoom";
    public string ServiceId { get; }

    private readonly string _roomId;
    private readonly ILogger<ChatRoomService> _logger;

    // 业务状态
    private readonly List<Message> _messages = new();
    private readonly HashSet<string> _participants = new();

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
    {
        _roomId = roomId;
        ServiceId = $"ChatRoom:{roomId}";
        _logger = logger;
        _logger.LogInformation("ChatRoomService created: {ServiceId}", ServiceId);
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
// Hub：无状态的通信契约（Service 直接注入）
// ============================================================================

public class ChatRoomHub
{
    private readonly ChatRoomService _service;

    // Service 直接注入到构造函数
    public ChatRoomHub(ChatRoomService service)
    {
        _service = service;
    }

    // 方法不再需要 roomId 参数，Service 已经知道自己是哪个房间
    public Task JoinAsync(string userId, string userName)
    {
        _service.Join(userId, userName);
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string text)
    {
        // 从上下文获取当前用户信息（简化示例，实际应从认证信息获取）
        var message = new Message
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "current-user",
            UserName = "Current User",
            Text = text,
            Timestamp = DateTimeOffset.UtcNow
        };

        _service.AddMessage(message);
        return Task.CompletedTask;
    }

    public Task<Message[]> GetMessagesAsync()
    {
        return Task.FromResult(_service.GetMessages().ToArray());
    }
}
