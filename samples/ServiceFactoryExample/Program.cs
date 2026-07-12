using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddPulseService<ChatRoomService>((services, roomId) =>
    new ChatRoomService(
        roomId,
        services.GetRequiredService<ILogger<ChatRoomService>>()));

using var host = builder.Build();
await host.StartAsync();

var rooms = host.Services.GetRequiredService<IServiceAccessor<ChatRoomService>>();
var room = await rooms.GetAsync("room-1");
room.Join("alice");
room.AddMessage("alice", "Hello from the current IServiceAccessor API.");

var cached = await rooms.GetAsync("room-1");
Console.WriteLine($"Same instance: {ReferenceEquals(room, cached)}");
Console.WriteLine($"Active rooms: {rooms.ActiveCount}");
Console.WriteLine($"Messages: {string.Join(" | ", cached.Messages)}");

Console.WriteLine($"Removed: {await rooms.RemoveAsync("room-1")}");
Console.WriteLine($"Active rooms after removal: {rooms.ActiveCount}");

await host.StopAsync();

internal sealed class ChatRoomService : PulseServiceBase
{
    private readonly HashSet<string> _members = new(StringComparer.Ordinal);
    private readonly List<string> _messages = new();

    public ChatRoomService(string roomId, ILogger<ChatRoomService> logger)
        : base("ChatRoom", roomId, logger)
    {
    }

    public IReadOnlyList<string> Messages => _messages;

    public void Join(string userId) => _members.Add(userId);

    public void AddMessage(string userId, string text)
    {
        if (!_members.Contains(userId))
        {
            throw new InvalidOperationException($"User '{userId}' has not joined the room.");
        }

        _messages.Add($"{userId}: {text}");
    }
}
