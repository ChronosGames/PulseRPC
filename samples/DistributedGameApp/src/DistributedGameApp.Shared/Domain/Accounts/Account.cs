using System;
using MemoryPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedGameApp.Shared.Domain.Accounts;

/// <summary>
/// 用户账户
/// </summary>
[MemoryPackable]
public partial class Account
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public ObjectId Id { get; set; } = ObjectId.Empty;

    /// <summary>
    /// 用户唯一标识
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 用户名
    /// </summary>
    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 第三方登录提供商（google, facebook, apple, 等）
    /// </summary>
    [BsonElement("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// 第三方提供商的用户ID
    /// </summary>
    [BsonElement("providerUserId")]
    public string ProviderUserId { get; set; } = string.Empty;

    /// <summary>
    /// 密码哈希（仅用于 provider = "local" 的本地账户）
    /// </summary>
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// 账户状态（Normal, Banned, Suspended）
    /// </summary>
    [BsonElement("status")]
    public AccountStatus Status { get; set; } = AccountStatus.Normal;

    /// <summary>
    /// 创建时间
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后登录时间
    /// </summary>
    [BsonElement("lastLoginAt")]
    public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后登录IP
    /// </summary>
    [BsonElement("lastLoginIp")]
    public string LastLoginIp { get; set; } = string.Empty;
}

/// <summary>
/// 账户状态
/// </summary>
[MemoryPackable]
public partial class AccountStatus
{
    public static readonly AccountStatus Normal = new() { Code = 0, Name = "Normal" };
    public static readonly AccountStatus Banned = new() { Code = 1, Name = "Banned" };
    public static readonly AccountStatus Suspended = new() { Code = 2, Name = "Suspended" };

    public int Code { get; set; }
    public string Name { get; set; } = string.Empty;
}
