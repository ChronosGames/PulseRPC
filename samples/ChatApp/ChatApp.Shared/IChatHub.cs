using System;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;

namespace ChatApp.NewArchitecture.Contracts;

/// <summary>
/// 聊天室 Hub 接口 - RPC 契约定义
/// </summary>
/// <remarks>
/// <para>
/// 此接口定义了客户端可以调用的聊天室相关方法。
/// 实现类 <see cref="Services.ChatRoomHub"/> 是无状态的 Singleton。
/// </para>
/// </remarks>
public interface IChatRoomHub : IPulseHub
{
    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="token">认证 Token</param>
    /// <returns>登录结果</returns>
    Task<ChatLoginResult> LoginAsync(string token);

    /// <summary>
    /// 加入聊天室
    /// </summary>
    /// <param name="roomId">房间 ID</param>
    /// <returns>加入结果</returns>
    Task<JoinRoomResult> JoinRoomAsync(string roomId);

    /// <summary>
    /// 发送消息
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <returns>发送结果</returns>
    Task<SendMessageResult> SendMessageAsync(string message);

    /// <summary>
    /// 获取房间成员列表
    /// </summary>
    /// <returns>成员列表</returns>
    Task<string[]> GetMembersAsync();

    /// <summary>
    /// 获取最近消息
    /// </summary>
    /// <param name="count">消息数量</param>
    /// <returns>消息列表</returns>
    Task<ChatMessage[]> GetRecentMessagesAsync(int count);

    /// <summary>
    /// 离开聊天室
    /// </summary>
    /// <returns>离开结果</returns>
    Task<bool> LeaveRoomAsync();

    /// <summary>
    /// 登出
    /// </summary>
    Task LogoutAsync();
}

/// <summary>
/// 登录结果
/// </summary>
[MemoryPackable]
public partial class ChatLoginResult
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ErrorMessage { get; set; }

    public static ChatLoginResult Ok(string userId, string userName)
        => new() { Success = true, UserId = userId, UserName = userName };

    public static ChatLoginResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// 加入房间结果
/// </summary>
[MemoryPackable]
public partial class JoinRoomResult
{
    public bool Success { get; set; }
    public string? RoomId { get; set; }
    public int MemberCount { get; set; }
    public string? ErrorMessage { get; set; }

    public static JoinRoomResult Ok(string roomId, int memberCount)
        => new() { Success = true, RoomId = roomId, MemberCount = memberCount };

    public static JoinRoomResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// 发送消息结果
/// </summary>
[MemoryPackable]
public partial class SendMessageResult
{
    public bool Success { get; set; }
    public long MessageId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? ErrorMessage { get; set; }

    public static SendMessageResult Ok(long messageId)
        => new() { Success = true, MessageId = messageId, Timestamp = DateTime.UtcNow };

    public static SendMessageResult Failed(string error)
        => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// 聊天消息
/// </summary>
[MemoryPackable]
public partial class ChatMessage
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

