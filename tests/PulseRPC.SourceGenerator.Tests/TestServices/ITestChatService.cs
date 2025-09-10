using System;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Abstractions;

namespace PulseRPC.SourceGenerator.Tests.TestServices;

/// <summary>
/// 测试聊天服务接口 - 用于验证Source Generator
/// </summary>
[PulseService]
[Channel("ChatChannel")]
public interface ITestChatService
{
    ValueTask<SendMessageResponse> SendMessageAsync(SendMessageRequest request);
    ValueTask<GetMessagesResponse> GetMessagesAsync(GetMessagesRequest request);
    ValueTask BroadcastMessageAsync(BroadcastMessageRequest request);
    Task<UserInfo> GetUserInfoAsync(GetUserInfoRequest request);
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
    public DateTime LastSeen { get; set; }

    [MemoryPackOrder(4)]
    public bool IsOnline { get; set; }
}
