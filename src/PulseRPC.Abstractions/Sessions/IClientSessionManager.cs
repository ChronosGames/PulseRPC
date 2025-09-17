using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Authentication;
using PulseRPC.Transport;

namespace PulseRPC.Sessions;

/// <summary>
/// 客户端会话管理器 - 应用层的多客户端管理
/// 符合三层抽象架构的设计，专注于业务级会话管理
/// </summary>
public interface IClientSessionManager : IDisposable
{
    /// <summary>
    /// 当前活跃会话数
    /// </summary>
    int ActiveSessionCount { get; }

    /// <summary>
    /// 所有会话ID
    /// </summary>
    IReadOnlyCollection<string> SessionIds { get; }

    /// <summary>
    /// 添加客户端会话
    /// </summary>
    /// <param name="session">客户端会话</param>
    /// <returns>是否成功添加</returns>
    bool AddSession(IClientSession session);

    /// <summary>
    /// 获取客户端会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>客户端会话，如果不存在返回null</returns>
    IClientSession? GetSession(string sessionId);

    /// <summary>
    /// 移除客户端会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>是否成功移除</returns>
    Task<bool> RemoveSessionAsync(string sessionId);

    /// <summary>
    /// 获取所有活跃会话
    /// </summary>
    /// <returns>客户端会话集合</returns>
    IReadOnlyCollection<IClientSession> GetActiveSessions();

    /// <summary>
    /// 获取已认证的会话
    /// </summary>
    /// <returns>已认证的客户端会话集合</returns>
    IReadOnlyCollection<IClientSession> GetAuthenticatedSessions();

    /// <summary>
    /// 根据用户名获取会话
    /// </summary>
    /// <param name="username">用户名</param>
    /// <returns>该用户的会话集合</returns>
    IReadOnlyCollection<IClientSession> GetSessionsByUser(string username);

    /// <summary>
    /// 广播消息到所有会话
    /// </summary>
    /// <param name="data">消息数据</param>
    /// <param name="filter">会话过滤器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功发送的会话数量</returns>
    Task<int> BroadcastAsync(ReadOnlyMemory<byte> data, Func<IClientSession, bool>? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 会话连接事件
    /// </summary>
    event EventHandler<SessionConnectedEventArgs>? SessionConnected;

    /// <summary>
    /// 会话断开事件
    /// </summary>
    event EventHandler<SessionDisconnectedEventArgs>? SessionDisconnected;

    /// <summary>
    /// 会话认证事件
    /// </summary>
    event EventHandler<SessionAuthenticatedEventArgs>? SessionAuthenticated;
}

/// <summary>
/// 会话连接事件参数
/// </summary>
public sealed class SessionConnectedEventArgs : EventArgs
{
    public IClientSession Session { get; }
    public DateTime Timestamp { get; }

    public SessionConnectedEventArgs(IClientSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 会话断开事件参数
/// </summary>
public sealed class SessionDisconnectedEventArgs : EventArgs
{
    public IClientSession Session { get; }
    public string? DisconnectReason { get; }
    public DateTime Timestamp { get; }

    public SessionDisconnectedEventArgs(IClientSession session, string? disconnectReason = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        DisconnectReason = disconnectReason;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// 会话认证事件参数
/// </summary>
public sealed class SessionAuthenticatedEventArgs : EventArgs
{
    public IClientSession Session { get; }
    public IAuthenticationContext AuthenticationContext { get; }
    public DateTime Timestamp { get; }

    public SessionAuthenticatedEventArgs(IClientSession session, IAuthenticationContext authenticationContext)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        AuthenticationContext = authenticationContext ?? throw new ArgumentNullException(nameof(authenticationContext));
        Timestamp = DateTime.UtcNow;
    }
}