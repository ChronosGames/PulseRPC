using System;
using System.Collections.Generic;
using MemoryPack;

namespace DistributedGameApp.Shared.Domain.Mail;

/// <summary>
/// 邮件实体
/// </summary>
[MemoryPackable]
public partial class Mail
{
    /// <summary>
    /// 邮件ID
    /// </summary>
    public string MailId { get; set; } = string.Empty;

    /// <summary>
    /// 发送者ID
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// 发送者名称
    /// </summary>
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// 接收者ID
    /// </summary>
    public string ReceiverId { get; set; } = string.Empty;

    /// <summary>
    /// 接收者名称
    /// </summary>
    public string ReceiverName { get; set; } = string.Empty;

    /// <summary>
    /// 邮件标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 邮件内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 邮件类型（系统邮件、玩家邮件等）
    /// </summary>
    public MailType Type { get; set; } = MailType.Player;

    /// <summary>
    /// 是否已读
    /// </summary>
    public bool IsRead { get; set; } = false;

    /// <summary>
    /// 是否已领取附件
    /// </summary>
    public bool IsAttachmentClaimed { get; set; } = false;

    /// <summary>
    /// 附件列表（可选）
    /// </summary>
    public List<MailAttachment> Attachments { get; set; } = new();

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 过期时间（可选，过期后自动删除）
    /// </summary>
    public DateTime? ExpireAt { get; set; }

    /// <summary>
    /// 是否已删除
    /// </summary>
    public bool IsDeleted { get; set; } = false;
}

/// <summary>
/// 邮件附件
/// </summary>
[MemoryPackable]
public partial class MailAttachment
{
    /// <summary>
    /// 物品ID
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 物品类型
    /// </summary>
    public string ItemType { get; set; } = string.Empty;

    /// <summary>
    /// 物品数量
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// 物品名称（用于显示）
    /// </summary>
    public string ItemName { get; set; } = string.Empty;
}

/// <summary>
/// 邮件类型
/// </summary>
public enum MailType
{
    /// <summary>
    /// 玩家邮件
    /// </summary>
    Player = 1,

    /// <summary>
    /// 系统邮件
    /// </summary>
    System = 2,

    /// <summary>
    /// 奖励邮件
    /// </summary>
    Reward = 3,

    /// <summary>
    /// 帮派邮件
    /// </summary>
    Guild = 4
}

/// <summary>
/// 发送邮件请求
/// </summary>
[MemoryPackable]
public partial class SendMailRequest
{
    /// <summary>
    /// 接收者ID
    /// </summary>
    public string ReceiverId { get; set; } = string.Empty;

    /// <summary>
    /// 邮件标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 邮件内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 附件列表（可选）
    /// </summary>
    public List<MailAttachment> Attachments { get; set; } = new();
}

/// <summary>
/// 发送邮件响应
/// </summary>
[MemoryPackable]
public partial class SendMailResponse
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 邮件ID
    /// </summary>
    public string MailId { get; set; } = string.Empty;

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 获取邮件列表请求
/// </summary>
[MemoryPackable]
public partial class GetMailListRequest
{
    /// <summary>
    /// 页码（从1开始）
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// 每页数量
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// 是否只获取未读邮件
    /// </summary>
    public bool OnlyUnread { get; set; } = false;
}

/// <summary>
/// 邮件列表响应
/// </summary>
[MemoryPackable]
public partial class MailListResponse
{
    /// <summary>
    /// 邮件列表
    /// </summary>
    public List<Mail> Mails { get; set; } = new();

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 未读数量
    /// </summary>
    public int UnreadCount { get; set; }
}
