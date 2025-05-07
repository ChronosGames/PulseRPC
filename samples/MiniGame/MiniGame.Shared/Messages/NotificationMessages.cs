using MemoryPack;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using System.Collections.Generic;

namespace PulseRPC.Samples.Shared.Messages;

/// <summary>
/// 系统通知消息
/// </summary>
[MemoryPackable, Message(2001, MessageType.Notification)]
public partial class SystemNotification : IMessage
{
    /// <summary>
    /// 通知类型
    /// </summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// 通知标题
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// 通知内容
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// 过期时间戳（毫秒）
    /// </summary>
    public long ExpireTime { get; set; }

    /// <summary>
    /// 附加数据
    /// </summary>
    public Dictionary<string, string> ExtraData { get; set; }
}

/// <summary>
/// 用户状态通知消息
/// </summary>
[MemoryPackable, Message(2002, MessageType.Notification)]
public partial class UserStatusNotification : IMessage
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户状态
    /// </summary>
    public UserStatus Status { get; set; }

    /// <summary>
    /// 状态变更时间戳（毫秒）
    /// </summary>
    public long StatusChangedTime { get; set; }
}

/// <summary>
/// 全局广播消息
/// </summary>
[MemoryPackable, Message(3001, MessageType.Broadcast)]
public partial class GlobalBroadcast : IMessage
{
    /// <summary>
    /// 广播内容
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// 发送者
    /// </summary>
    public string Sender { get; set; }

    /// <summary>
    /// 广播颜色
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// 显示时长（毫秒）
    /// </summary>
    public long Duration { get; set; }
}
