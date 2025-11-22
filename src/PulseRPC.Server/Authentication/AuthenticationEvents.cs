using System;
using PulseRPC.Authentication;

namespace PulseRPC.Server.Authentication;

/// <summary>
/// 认证状态变化事件参数
/// </summary>
public class AuthenticationChangedEventArgs : EventArgs
{
    /// <summary>
    /// 连接ID
    /// </summary>
    public string ConnectionId { get; }

    /// <summary>
    /// 旧的认证上下文
    /// </summary>
    public IAuthenticationContext? PreviousAuthentication { get; }

    /// <summary>
    /// 新的认证上下文
    /// </summary>
    public IAuthenticationContext? CurrentAuthentication { get; }

    /// <summary>
    /// 变化时间
    /// </summary>
    public DateTime ChangedAt { get; }

    public AuthenticationChangedEventArgs(
        string connectionId,
        IAuthenticationContext? previousAuthentication,
        IAuthenticationContext? currentAuthentication)
    {
        ConnectionId = connectionId;
        PreviousAuthentication = previousAuthentication;
        CurrentAuthentication = currentAuthentication;
        ChangedAt = DateTime.UtcNow;
    }
}
