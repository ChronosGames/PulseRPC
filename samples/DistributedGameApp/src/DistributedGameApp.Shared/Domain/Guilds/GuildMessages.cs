using System;
using MemoryPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedGameApp.Shared.Domain.Guilds;

/// <summary>
/// 创建公会响应
/// </summary>
[MemoryPackable]
public partial class CreateGuildResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guild? Guild { get; set; }
}

/// <summary>
/// 获取公会列表请求
/// </summary>
[MemoryPackable]
public partial class GetGuildListRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "Level"; // Level, MemberCount, CreatedAt
    public bool Descending { get; set; } = true;
}

/// <summary>
/// 公会列表响应
/// </summary>
[MemoryPackable]
public partial class GuildListResponse
{
    public Guild[] Guilds { get; set; } = Array.Empty<Guild>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// 更新公会信息请求
/// </summary>
[MemoryPackable]
public partial class UpdateGuildInfoRequest
{
    public string GuildId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Tag { get; set; }
    public string? Description { get; set; }
    public string? Notice { get; set; }
}

/// <summary>
/// 我的公会信息
/// </summary>
[MemoryPackable]
public partial class MyGuildInfo
{
    public Guild? Guild { get; set; }
    public GuildMember? MyMember { get; set; }
    public int OnlineMemberCount { get; set; }
}

/// <summary>
/// 加入申请
/// </summary>
[MemoryPackable]
public partial class JoinRequest
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("level")]
    public int Level { get; set; }

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("status")]
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 公会消息
/// </summary>
[MemoryPackable]
public partial class GuildMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 捐献请求
/// </summary>
[MemoryPackable]
public partial class DonateRequest
{
    public string ResourceType { get; set; } = string.Empty; // Gold, Diamond, Item
    public long Amount { get; set; }
    public string? ItemId { get; set; }
}

/// <summary>
/// 公会活动
/// </summary>
[MemoryPackable]
public partial class GuildActivity
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [BsonElement("activityType")]
    public string ActivityType { get; set; } = string.Empty; // CheckIn, Donate, Boss, War

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 公会公告
/// </summary>
[MemoryPackable]
public partial class GuildAnnouncement
{
    [BsonId]
    // [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [BsonElement("authorId")]
    public string AuthorId { get; set; } = string.Empty;

    [BsonElement("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [BsonElement("content")]
    public string Content { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("isPinned")]
    public bool IsPinned { get; set; } = false;
}
