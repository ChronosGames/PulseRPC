using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace PulseRPC.Authentication;

/// <summary>
/// 认证类型枚举
/// </summary>
public enum AuthenticationType
{
    /// <summary>未认证</summary>
    None,
    /// <summary>客户端认证</summary>
    Client,
    /// <summary>服务间认证</summary>
    Service,
    /// <summary>内部系统认证</summary>
    Internal
}

/// <summary>
/// 统一认证上下文接口
/// </summary>
public interface IAuthenticationContext
{
    /// <summary>
    /// 连接ID
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// 是否已认证
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 认证类型
    /// </summary>
    AuthenticationType Type { get; }

    /// <summary>
    /// 身份标识（用户ID或服务ID）
    /// </summary>
    string? Identity { get; }

    /// <summary>
    /// 显示名称（用户名或服务名）
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// 认证令牌
    /// </summary>
    string? Token { get; }

    /// <summary>
    /// 认证时间
    /// </summary>
    DateTime? AuthenticationTime { get; }

    /// <summary>
    /// Claims主体（用于复杂认证场景）
    /// </summary>
    ClaimsPrincipal? Principal { get; }

    /// <summary>
    /// 会话属性
    /// </summary>
    IDictionary<string, object> Properties { get; }

    /// <summary>
    /// 权限范围（用于服务间认证）
    /// </summary>
    string[]? Scopes { get; }

    /// <summary>
    /// 设置客户端认证信息
    /// </summary>
    void SetClientAuthentication(string userId, string username, string? token = null, ClaimsPrincipal? principal = null);

    /// <summary>
    /// 设置服务认证信息
    /// </summary>
    void SetServiceAuthentication(string serviceId, string serviceName, string token, string[]? scopes = null, ClaimsPrincipal? principal = null);

    /// <summary>
    /// 清除认证信息
    /// </summary>
    void Clear();

    /// <summary>
    /// 检查是否具有指定权限范围
    /// </summary>
    bool HasScope(string scope);

    /// <summary>
    /// 检查是否具有指定角色
    /// </summary>
    bool IsInRole(string role);
}
