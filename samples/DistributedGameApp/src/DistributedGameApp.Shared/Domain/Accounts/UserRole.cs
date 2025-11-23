using System;
using System.Collections.Generic;
using System.Linq;

namespace DistributedGameApp.Shared.Domain.Accounts;

/// <summary>
/// 用户角色常量定义
/// </summary>
public static class UserRoles
{
    /// <summary>
    /// 访客 - 最低权限级别
    /// </summary>
    public const string Guest = "guest";

    /// <summary>
    /// 普通用户 - 基础权限
    /// </summary>
    public const string User = "user";

    /// <summary>
    /// VIP用户 - 增强权限
    /// </summary>
    public const string Vip = "vip";

    /// <summary>
    /// 版主 - 管理权限
    /// </summary>
    public const string Moderator = "moderator";

    /// <summary>
    /// 管理员 - 高级管理权限
    /// </summary>
    public const string Admin = "admin";

    /// <summary>
    /// 超级管理员 - 最高权限
    /// </summary>
    public const string SuperAdmin = "superadmin";

    /// <summary>
    /// 所有角色列表
    /// </summary>
    public static readonly string[] All = new[]
    {
        Guest,
        User,
        Vip,
        Moderator,
        Admin,
        SuperAdmin
    };

    /// <summary>
    /// 验证角色是否有效
    /// </summary>
    public static bool IsValid(string role)
    {
        return All.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 用户权限常量定义
/// </summary>
public static class UserPermissions
{
    // ========== 游戏权限 ==========

    /// <summary>
    /// 游戏 - 玩游戏
    /// </summary>
    public const string GamePlay = "game.play";

    /// <summary>
    /// 游戏 - 创建房间
    /// </summary>
    public const string GameCreate = "game.create";

    /// <summary>
    /// 游戏 - 加入房间
    /// </summary>
    public const string GameJoin = "game.join";

    // ========== 聊天权限 ==========

    /// <summary>
    /// 聊天 - 发送消息
    /// </summary>
    public const string ChatSend = "chat.send";

    /// <summary>
    /// 聊天 - 管理消息（删除、禁言等）
    /// </summary>
    public const string ChatModerate = "chat.moderate";

    // ========== 玩家管理权限 ==========

    /// <summary>
    /// 玩家 - 查看玩家信息
    /// </summary>
    public const string PlayerView = "player.view";

    /// <summary>
    /// 玩家 - 管理玩家（修改信息等）
    /// </summary>
    public const string PlayerManage = "player.manage";

    /// <summary>
    /// 玩家 - 踢出玩家
    /// </summary>
    public const string PlayerKick = "player.kick";

    /// <summary>
    /// 玩家 - 封禁玩家
    /// </summary>
    public const string PlayerBan = "player.ban";

    // ========== 角色管理权限 ==========

    /// <summary>
    /// 角色 - 创建角色
    /// </summary>
    public const string CharacterCreate = "character.create";

    /// <summary>
    /// 角色 - 删除角色
    /// </summary>
    public const string CharacterDelete = "character.delete";

    // ========== 支付权限 ==========

    /// <summary>
    /// 支付 - 查看支付记录
    /// </summary>
    public const string PaymentView = "payment.view";

    /// <summary>
    /// 支付 - 创建支付订单
    /// </summary>
    public const string PaymentCreate = "payment.create";

    /// <summary>
    /// 支付 - 退款
    /// </summary>
    public const string PaymentRefund = "payment.refund";

    // ========== 邮件权限 ==========

    /// <summary>
    /// 邮件 - 发送邮件
    /// </summary>
    public const string MailSend = "mail.send";

    /// <summary>
    /// 邮件 - 发送系统邮件
    /// </summary>
    public const string MailSendSystem = "mail.send.system";

    // ========== 管理权限 ==========

    /// <summary>
    /// 管理 - 访问管理面板
    /// </summary>
    public const string AdminAccess = "admin.access";

    /// <summary>
    /// 管理 - 管理系统配置
    /// </summary>
    public const string AdminManage = "admin.manage";

    /// <summary>
    /// 管理 - 查看系统统计
    /// </summary>
    public const string AdminStats = "admin.stats";

    // ========== 匹配权限 ==========

    /// <summary>
    /// 匹配 - 请求匹配
    /// </summary>
    public const string MatchmakingRequest = "matchmaking.request";

    /// <summary>
    /// 匹配 - 优先匹配（VIP）
    /// </summary>
    public const string MatchmakingPriority = "matchmaking.priority";

    /// <summary>
    /// 所有权限列表
    /// </summary>
    public static readonly string[] All = new[]
    {
        GamePlay,
        GameCreate,
        GameJoin,
        ChatSend,
        ChatModerate,
        PlayerView,
        PlayerManage,
        PlayerKick,
        PlayerBan,
        CharacterCreate,
        CharacterDelete,
        PaymentView,
        PaymentCreate,
        PaymentRefund,
        MailSend,
        MailSendSystem,
        AdminAccess,
        AdminManage,
        AdminStats,
        MatchmakingRequest,
        MatchmakingPriority
    };

    /// <summary>
    /// 验证权限是否有效
    /// </summary>
    public static bool IsValid(string permission)
    {
        return All.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 角色-权限映射关系
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// 角色到权限的映射表
    /// </summary>
    public static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 访客 - 只能查看
        [UserRoles.Guest] = new[]
        {
            UserPermissions.PlayerView
        },

        // 普通用户 - 基础游戏权限
        [UserRoles.User] = new[]
        {
            UserPermissions.GamePlay,
            UserPermissions.GameJoin,
            UserPermissions.ChatSend,
            UserPermissions.PlayerView,
            UserPermissions.CharacterCreate,
            UserPermissions.CharacterDelete,
            UserPermissions.PaymentView,
            UserPermissions.MailSend,
            UserPermissions.MatchmakingRequest
        },

        // VIP用户 - 增强权限
        [UserRoles.Vip] = new[]
        {
            UserPermissions.GamePlay,
            UserPermissions.GameCreate,
            UserPermissions.GameJoin,
            UserPermissions.ChatSend,
            UserPermissions.PlayerView,
            UserPermissions.CharacterCreate,
            UserPermissions.CharacterDelete,
            UserPermissions.PaymentView,
            UserPermissions.PaymentCreate,
            UserPermissions.MailSend,
            UserPermissions.MatchmakingRequest,
            UserPermissions.MatchmakingPriority
        },

        // 版主 - 管理权限
        [UserRoles.Moderator] = new[]
        {
            UserPermissions.GamePlay,
            UserPermissions.GameJoin,
            UserPermissions.ChatSend,
            UserPermissions.ChatModerate,
            UserPermissions.PlayerView,
            UserPermissions.PlayerManage,
            UserPermissions.PlayerKick,
            UserPermissions.CharacterCreate,
            UserPermissions.CharacterDelete,
            UserPermissions.MailSend
        },

        // 管理员 - 高级管理权限
        [UserRoles.Admin] = new[]
        {
            UserPermissions.GamePlay,
            UserPermissions.GameCreate,
            UserPermissions.GameJoin,
            UserPermissions.ChatSend,
            UserPermissions.ChatModerate,
            UserPermissions.PlayerView,
            UserPermissions.PlayerManage,
            UserPermissions.PlayerKick,
            UserPermissions.PlayerBan,
            UserPermissions.CharacterCreate,
            UserPermissions.CharacterDelete,
            UserPermissions.PaymentView,
            UserPermissions.PaymentRefund,
            UserPermissions.MailSend,
            UserPermissions.MailSendSystem,
            UserPermissions.AdminAccess,
            UserPermissions.AdminStats
        },

        // 超级管理员 - 所有权限
        [UserRoles.SuperAdmin] = new[]
        {
            "*" // 通配符表示所有权限
        }
    };

    /// <summary>
    /// 获取指定角色的所有权限
    /// </summary>
    /// <param name="role">角色名称</param>
    /// <returns>权限数组</returns>
    public static string[] GetPermissions(string role)
    {
        if (Map.TryGetValue(role, out var permissions))
        {
            return permissions;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// 获取多个角色的所有权限（去重）
    /// </summary>
    /// <param name="roles">角色列表</param>
    /// <returns>权限数组（去重后）</returns>
    public static string[] GetPermissions(params string[] roles)
    {
        var permissionsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            if (Map.TryGetValue(role, out var rolePermissions))
            {
                foreach (var permission in rolePermissions)
                {
                    permissionsSet.Add(permission);
                }
            }
        }

        return permissionsSet.ToArray();
    }

    /// <summary>
    /// 检查角色是否有指定权限
    /// </summary>
    /// <param name="role">角色名称</param>
    /// <param name="permission">权限名称</param>
    /// <returns>是否有该权限</returns>
    public static bool HasPermission(string role, string permission)
    {
        if (!Map.TryGetValue(role, out var permissions))
        {
            return false;
        }

        // 检查通配符
        if (permissions.Contains("*"))
        {
            return true;
        }

        return permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查多个角色是否有指定权限
    /// </summary>
    /// <param name="roles">角色列表</param>
    /// <param name="permission">权限名称</param>
    /// <returns>是否有该权限</returns>
    public static bool HasPermission(string[] roles, string permission)
    {
        foreach (var role in roles)
        {
            if (HasPermission(role, permission))
            {
                return true;
            }
        }

        return false;
    }
}
