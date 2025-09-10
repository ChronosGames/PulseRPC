using System;
using System.Linq;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Abstractions;

namespace PulseRPC.Phase2.Benchmarks.TestServices;

/// <summary>
/// 基准测试聊天服务 - 用于验证Source Generator性能
/// </summary>
[PulseService]
[Channel("BenchmarkChannel")]
public interface IBenchmarkChatService
{
    ValueTask<SendMessageResponse> SendMessageAsync(SendMessageRequest request);
    ValueTask<GetMessagesResponse> GetMessagesAsync(GetMessagesRequest request);
    ValueTask BroadcastMessageAsync(BroadcastMessageRequest request);
    Task<UserInfo> GetUserInfoAsync(GetUserInfoRequest request);
}

/// <summary>
/// 基准测试聊天服务实现
/// </summary>
public class BenchmarkChatService : IBenchmarkChatService
{
    public ValueTask<SendMessageResponse> SendMessageAsync(SendMessageRequest request)
    {
        return ValueTask.FromResult(new SendMessageResponse
        {
            MessageId = Guid.NewGuid().ToString(),
            Success = true,
            ProcessedAt = DateTime.UtcNow
        });
    }

    public ValueTask<GetMessagesResponse> GetMessagesAsync(GetMessagesRequest request)
    {
        var messages = Enumerable.Range(0, request.Count)
            .Select(i => new ChatMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = $"Message {i}",
                UserId = "user1",
                UserName = "User One",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                ChannelId = request.ChannelId
            })
            .ToArray();

        return ValueTask.FromResult(new GetMessagesResponse
        {
            Messages = messages,
            HasMore = false
        });
    }

    public ValueTask BroadcastMessageAsync(BroadcastMessageRequest request)
    {
        // 模拟广播处理
        return ValueTask.CompletedTask;
    }

    public Task<UserInfo> GetUserInfoAsync(GetUserInfoRequest request)
    {
        return Task.FromResult(new UserInfo
        {
            UserId = request.UserId,
            UserName = $"User_{request.UserId}",
            Email = $"{request.UserId}@example.com",
            LastSeen = DateTime.UtcNow,
            IsOnline = true
        });
    }
}

/// <summary>
/// 发送消息请求
/// </summary>
[MemoryPackable]
public partial class SendMessageRequest
{
    [MemoryPackOrder(0)]
    public required string Message { get; set; }
    
    [MemoryPackOrder(1)]
    public required string UserId { get; set; }
    
    [MemoryPackOrder(2)]
    public string? ChannelId { get; set; }
    
    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 发送消息响应
/// </summary>
[MemoryPackable]
public partial class SendMessageResponse
{
    [MemoryPackOrder(0)]
    public required string MessageId { get; set; }
    
    [MemoryPackOrder(1)]
    public bool Success { get; set; }
    
    [MemoryPackOrder(2)]
    public string? ErrorMessage { get; set; }
    
    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 获取消息请求
/// </summary>
[MemoryPackable]
public partial class GetMessagesRequest
{
    [MemoryPackOrder(0)]
    public required string ChannelId { get; set; }
    
    [MemoryPackOrder(1)]
    public int Count { get; set; } = 50;
    
    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public DateTime? After { get; set; }
}

/// <summary>
/// 获取消息响应
/// </summary>
[MemoryPackable]
public partial class GetMessagesResponse
{
    [MemoryPackOrder(0)]
    public required ChatMessage[] Messages { get; set; } = [];
    
    [MemoryPackOrder(1)]
    public bool HasMore { get; set; }
    
    [MemoryPackOrder(2)]
    public string? NextCursor { get; set; }
}

/// <summary>
/// 广播消息请求
/// </summary>
[MemoryPackable]
public partial class BroadcastMessageRequest
{
    [MemoryPackOrder(0)]
    public required string Message { get; set; }
    
    [MemoryPackOrder(1)]
    public required string FromUserId { get; set; }
    
    [MemoryPackOrder(2)]
    public string[]? TargetChannels { get; set; }
}

/// <summary>
/// 获取用户信息请求
/// </summary>
[MemoryPackable]
public partial class GetUserInfoRequest
{
    [MemoryPackOrder(0)]
    public required string UserId { get; set; }
}

/// <summary>
/// 聊天消息
/// </summary>
[MemoryPackable]
public partial class ChatMessage
{
    [MemoryPackOrder(0)]
    public required string Id { get; set; }
    
    [MemoryPackOrder(1)]
    public required string Content { get; set; }
    
    [MemoryPackOrder(2)]
    public required string UserId { get; set; }
    
    [MemoryPackOrder(3)]
    public required string UserName { get; set; }
    
    [MemoryPackOrder(4)]
    [MemoryPackAllowSerialize]
    public DateTime Timestamp { get; set; }
    
    [MemoryPackOrder(5)]
    public string? ChannelId { get; set; }
}

/// <summary>
/// 用户信息
/// </summary>
[MemoryPackable]
public partial class UserInfo
{
    [MemoryPackOrder(0)]
    public required string UserId { get; set; }
    
    [MemoryPackOrder(1)]
    public required string UserName { get; set; }
    
    [MemoryPackOrder(2)]
    public string? Email { get; set; }
    
    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public DateTime LastSeen { get; set; }
    
    [MemoryPackOrder(4)]
    public bool IsOnline { get; set; }
}