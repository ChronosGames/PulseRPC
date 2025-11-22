using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// 用户角色列表
    /// </summary>
    /// <remarks>
    /// 默认角色为 "user"，可以包含多个角色
    /// 角色层次: guest < user < vip < moderator < admin < superadmin
    /// </remarks>
    [BsonElement("roles")]
    public List<string> Roles { get; set; } = new() { UserRoles.User };

    /// <summary>
    /// 额外的权限列表（角色之外的特殊权限）
    /// </summary>
    /// <remarks>
    /// 用于给特定用户授予角色之外的权限
    /// 例如：临时授予某个用户管理员权限但不改变其角色
    /// </remarks>
    [BsonElement("additionalPermissions")]
    public List<string> AdditionalPermissions { get; set; } = new();

    /// <summary>
    /// 获取用户的所有有效权限（角色权限 + 额外权限）
    /// </summary>
    /// <returns>所有权限的数组（去重）</returns>
    public string[] GetAllPermissions()
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. 添加角色对应的权限
        foreach (var role in Roles)
        {
            var rolePermissions = RolePermissions.GetPermissions(role);
            foreach (var permission in rolePermissions)
            {
                permissions.Add(permission);
            }
        }

        // 2. 添加额外权限
        foreach (var permission in AdditionalPermissions)
        {
            permissions.Add(permission);
        }

        return permissions.ToArray();
    }

    /// <summary>
    /// 检查是否有指定角色
    /// </summary>
    /// <param name="role">角色名称</param>
    /// <returns>是否有该角色</returns>
    public bool HasRole(string role)
    {
        return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查是否有指定权限
    /// </summary>
    /// <param name="permission">权限名称</param>
    /// <returns>是否有该权限</returns>
    public bool HasPermission(string permission)
    {
        var allPermissions = GetAllPermissions();

        // 检查通配符
        if (allPermissions.Contains("*"))
        {
            return true;
        }

        return allPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 添加角色
    /// </summary>
    /// <param name="role">角色名称</param>
    public void AddRole(string role)
    {
        if (!Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            Roles.Add(role);
        }
    }

    /// <summary>
    /// 移除角色
    /// </summary>
    /// <param name="role">角色名称</param>
    public void RemoveRole(string role)
    {
        Roles.RemoveAll(r => r.Equals(role, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 添加权限
    /// </summary>
    /// <param name="permission">权限名称</param>
    public void AddPermission(string permission)
    {
        if (!AdditionalPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
        {
            AdditionalPermissions.Add(permission);
        }
    }

    /// <summary>
    /// 移除权限
    /// </summary>
    /// <param name="permission">权限名称</param>
    public void RemovePermission(string permission)
    {
        AdditionalPermissions.RemoveAll(p => p.Equals(permission, StringComparison.OrdinalIgnoreCase));
    }
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
