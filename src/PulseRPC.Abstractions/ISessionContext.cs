using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace PulseRPC
{
    /// <summary>
    /// 会话上下文接口，用于存储用户认证和会话信息
    /// </summary>
    public interface ISessionContext
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        Guid? UserId { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        string? Username { get; set; }

        /// <summary>
        /// 认证令牌
        /// </summary>
        string? Token { get; set; }

        /// <summary>
        /// 登录时间
        /// </summary>
        DateTime? LoginTime { get; set; }

        /// <summary>
        /// 是否已认证
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// 会话属性字典，用于存储自定义数据
        /// </summary>
        IDictionary<string, object> Properties { get; }

        /// <summary>
        /// 清除会话信息
        /// </summary>
        void Clear();

        /// <summary>
        /// 设置认证信息
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="username">用户名</param>
        /// <param name="token">认证令牌</param>
        void SetAuthentication(Guid userId, string username, string? token = null);

        /// <summary>
        /// Claims主体（用于复杂认证场景），新增属性
        /// </summary>
        ClaimsPrincipal? User { get; set; }

        /// <summary>
        /// 设置Claims认证信息，支持新的认证架构
        /// </summary>
        /// <param name="user">Claims主体</param>
        /// <param name="token">认证令牌</param>
        void SetClaimsAuthentication(ClaimsPrincipal user, string? token = null);
    }
}
