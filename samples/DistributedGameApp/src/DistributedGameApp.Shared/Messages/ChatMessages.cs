using MemoryPack;
using System;
using System.Collections.Generic;

namespace DistributedGameApp.Shared.Messages;

/// <summary>
/// 聊天消息
/// </summary>
[MemoryPackable]
public partial class ChatMessage
{
    /// <summary>消息ID</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>房间ID（私聊时为空）</summary>
    public string? RoomId { get; set; }

    /// <summary>频道类型</summary>
    public ChatChannel Channel { get; set; }

    /// <summary>发送者ID</summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>发送者名称</summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>接收者ID（私聊时使用）</summary>
    public string? ReceiverId { get; set; }

    /// <summary>消息类型</summary>
    public MessageType MessageType { get; set; }

    /// <summary>消息内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>发送时间</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>是否已读</summary>
    public bool IsRead { get; set; }
}

/// <summary>
/// 发送消息请求
/// </summary>
[MemoryPackable]
public partial class SendMessageRequest
{
    /// <summary>频道类型</summary>
    public ChatChannel Channel { get; set; }

    /// <summary>消息内容</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>接收者ID（私聊时必填）</summary>
    public string? ReceiverId { get; set; }
}

/// <summary>
/// 发送消息结果
/// </summary>
[MemoryPackable]
public partial class SendMessageResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>消息ID</summary>
    public string? MessageId { get; set; }

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>错误消息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 房间信息
/// </summary>
[MemoryPackable]
public partial class RoomInfo
{
    /// <summary>房间ID</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>房间名称</summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>房间类型</summary>
    public RoomType RoomType { get; set; }

    /// <summary>当前人数</summary>
    public int MemberCount { get; set; }

    /// <summary>最大人数</summary>
    public int MaxMembers { get; set; }

    /// <summary>房主ID</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>是否需要密码</summary>
    public bool RequirePassword { get; set; }
}

/// <summary>
/// 房间成员
/// </summary>
[MemoryPackable]
public partial class RoomMember
{
    /// <summary>玩家ID</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>玩家名称</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>加入时间</summary>
    public DateTime JoinedAt { get; set; }

    /// <summary>角色（房主、管理员、成员）</summary>
    public MemberRole Role { get; set; }

    /// <summary>是否静音</summary>
    public bool IsMuted { get; set; }
}

/// <summary>
/// 创建房间请求
/// </summary>
[MemoryPackable]
public partial class CreateRoomRequest
{
    /// <summary>房间名称</summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>房间类型</summary>
    public RoomType RoomType { get; set; }

    /// <summary>最大人数</summary>
    public int MaxMembers { get; set; }

    /// <summary>密码（可选）</summary>
    public string? Password { get; set; }
}

/// <summary>
/// 加入房间请求
/// </summary>
[MemoryPackable]
public partial class JoinRoomRequest
{
    /// <summary>房间ID</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>密码（如果房间需要）</summary>
    public string? Password { get; set; }
}

/// <summary>
/// 获取历史消息请求
/// </summary>
[MemoryPackable]
public partial class GetHistoryRequest
{
    /// <summary>频道类型</summary>
    public ChatChannel Channel { get; set; }

    /// <summary>房间ID（房间聊天时使用）</summary>
    public string? RoomId { get; set; }

    /// <summary>对方ID（私聊时使用）</summary>
    public string? TargetPlayerId { get; set; }

    /// <summary>起始时间（可选，用于分页）</summary>
    public DateTime? BeforeTime { get; set; }

    /// <summary>数量</summary>
    public int Count { get; set; }
}

/// <summary>
/// 聊天频道
/// </summary>
public enum ChatChannel
{
    /// <summary>世界频道</summary>
    World = 1,
    /// <summary>公会频道</summary>
    Guild = 2,
    /// <summary>队伍频道</summary>
    Party = 3,
    /// <summary>房间频道</summary>
    Room = 4,
    /// <summary>私聊</summary>
    Whisper = 5,
    /// <summary>系统</summary>
    System = 6
}

/// <summary>
/// 消息类型
/// </summary>
public enum MessageType
{
    /// <summary>普通文本</summary>
    Text = 1,
    /// <summary>表情</summary>
    Emoji = 2,
    /// <summary>系统通知</summary>
    System = 3,
    /// <summary>物品链接</summary>
    ItemLink = 4,
    /// <summary>语音消息</summary>
    Voice = 5
}

/// <summary>
/// 房间类型
/// </summary>
public enum RoomType
{
    /// <summary>公开房间</summary>
    Public = 1,
    /// <summary>私密房间（需要密码）</summary>
    Private = 2,
    /// <summary>公会房间</summary>
    Guild = 3,
    /// <summary>战斗房间</summary>
    Battle = 4
}

/// <summary>
/// 成员角色
/// </summary>
public enum MemberRole
{
    /// <summary>普通成员</summary>
    Member = 1,
    /// <summary>管理员</summary>
    Admin = 2,
    /// <summary>房主</summary>
    Owner = 3
}
