using System;
using MemoryPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedGameApp.Shared.Domain.Social;

/// <summary>
/// 好友关系
/// </summary>
[MemoryPackable]
public partial class Friend
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 好友用户ID
    /// </summary>
    [BsonElement("friendUserId")]
    public string FriendUserId { get; set; } = string.Empty;

    /// <summary>
    /// 好友用户名
    /// </summary>
    [BsonElement("friendUsername")]
    public string FriendUsername { get; set; } = string.Empty;

    /// <summary>
    /// 状态（Pending, Accepted, Blocked）
    /// </summary>
    [BsonElement("status")]
    public string Status { get; set; } = "Pending";

    /// <summary>
    /// 添加时间
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 接受时间
    /// </summary>
    [BsonElement("acceptedAt")]
    public DateTime? AcceptedAt { get; set; }
}

/// <summary>
/// 聊天消息
/// </summary>
[MemoryPackable]
public partial class ChatMessage
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 消息ID
    /// </summary>
    [BsonElement("messageId")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>
    /// 频道类型（World, Private, Guild, Team）
    /// </summary>
    [BsonElement("channelType")]
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>
    /// 频道ID（私聊时为对方用户ID，帮派时为帮派ID）
    /// </summary>
    [BsonElement("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// 发送者用户ID
    /// </summary>
    [BsonElement("senderId")]
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// 发送者用户名
    /// </summary>
    [BsonElement("senderName")]
    public string SenderName { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 发送时间
    /// </summary>
    [BsonElement("sentAt")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 发送消息请求
/// </summary>
[MemoryPackable]
public partial class SendMessageRequest
{
    /// <summary>
    /// 频道类型
    /// </summary>
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>
    /// 频道ID
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
