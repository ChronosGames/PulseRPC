using System;
using MemoryPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedGameApp.Shared.Domain.Guilds;

/// <summary>
/// 帮派/公会
/// </summary>
[MemoryPackable]
public partial class Guild
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    // [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 帮派ID
    /// </summary>
    [BsonElement("guildId")]
    public string GuildId { get; set; } = string.Empty;

    /// <summary>
    /// 帮派名称
    /// </summary>
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 帮派标签（简称）
    /// </summary>
    [BsonElement("tag")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// 帮派描述
    /// </summary>
    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 帮主用户ID
    /// </summary>
    [BsonElement("leaderId")]
    public string LeaderId { get; set; } = string.Empty;

    /// <summary>
    /// 帮派等级
    /// </summary>
    [BsonElement("level")]
    public int Level { get; set; } = 1;

    /// <summary>
    /// 帮派经验
    /// </summary>
    [BsonElement("exp")]
    public long Exp { get; set; } = 0;

    /// <summary>
    /// 最大成员数
    /// </summary>
    [BsonElement("maxMembers")]
    public int MaxMembers { get; set; } = 50;

    /// <summary>
    /// 当前成员数
    /// </summary>
    [BsonElement("memberCount")]
    public int MemberCount { get; set; } = 0;

    /// <summary>
    /// 创建时间
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 帮派成员
/// </summary>
[MemoryPackable]
public partial class GuildMember
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    // [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 帮派ID
    /// </summary>
    [BsonElement("guildId")]
    public string GuildId { get; set; } = string.Empty;

    /// <summary>
    /// 用户ID
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 用户名
    /// </summary>
    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 职位（Leader, Officer, Member）
    /// </summary>
    [BsonElement("role")]
    public string Role { get; set; } = "Member";

    /// <summary>
    /// 贡献度
    /// </summary>
    [BsonElement("contribution")]
    public long Contribution { get; set; } = 0;

    /// <summary>
    /// 加入时间
    /// </summary>
    [BsonElement("joinedAt")]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后上线时间
    /// </summary>
    [BsonElement("lastOnlineAt")]
    public DateTime LastOnlineAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 创建帮派请求
/// </summary>
[MemoryPackable]
public partial class CreateGuildRequest
{
    /// <summary>
    /// 帮派名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 帮派标签
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// 帮派描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
