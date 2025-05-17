using MemoryPack;

namespace PulseRPC.Samples.Shared.Messages;

public interface INotificationStreaming : IStreamingReceiver
{
    /// <summary>
    /// 接收系统通知
    /// </summary>
    Task ReceiveSystemNotification(SystemNotification notification);

    /// <summary>
    /// 接收用户状态通知
    /// </summary>
    Task ReceiveUserStatusNotification(UserStatusNotification notification);

    /// <summary>
    /// 接收全局广播
    /// </summary>
    Task ReceiveGlobalBroadcast(GlobalBroadcast broadcast);
}

/// <summary>
/// 系统通知消息
/// </summary>
[MemoryPackable]
public partial class SystemNotification
{
    /// <summary>
    /// 通知ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 通知类型
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// 通知标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 通知内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间戳（毫秒）
    /// </summary>
    public long CreateTime { get; set; }

    /// <summary>
    /// 过期时间戳（毫秒）
    /// </summary>
    public long ExpireTime { get; set; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// 用户状态通知消息
/// </summary>
[MemoryPackable]
public partial class UserStatusNotification
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 用户状态
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// 最后登录时间（毫秒）
    /// </summary>
    public long LastLoginTime { get; set; }

    /// <summary>
    /// 时间戳（毫秒）
    /// </summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// 全局广播消息
/// </summary>
[MemoryPackable]
public partial class GlobalBroadcast
{
    /// <summary>
    /// 广播ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 广播消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 发送者
    /// </summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// 广播颜色
    /// </summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>
    /// 显示时长（毫秒）
    /// </summary>
    public int DisplayTime { get; set; }

    /// <summary>
    /// 时间戳（毫秒）
    /// </summary>
    public long Timestamp { get; set; }
}
