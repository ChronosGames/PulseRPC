using PulseRPC.Authentication;
using PulseRPC.Server.Transport;
using PulseRPC.Transport;

namespace PulseRPC.Server.Sessions;

/// <summary>
/// 会话事件参数
/// </summary>
public class SessionEventArgs(IClientSession session) : EventArgs
{
    public IClientSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));
}

/// <summary>
/// 会话认证事件参数
/// </summary>
public class SessionAuthenticatedEventArgs(IClientSession session, IAuthenticationContext authContext)
    : SessionEventArgs(session)
{
    public IAuthenticationContext AuthenticationContext { get; } = authContext ?? throw new ArgumentNullException(nameof(authContext));
}

/// <summary>
/// 会话管理器统计信息
/// </summary>
public class SessionManagerStats
{
    public int ActiveSessions { get; set; }
    public int HealthySessions { get; set; }
    public int DegradedSessions { get; set; }
    public int UnhealthySessions { get; set; }
    public int FailedSessions { get; set; }
    public long TotalSessionsCreated { get; set; }
    public long TotalSessionsRemoved { get; set; }
    public long TotalHubInvocations { get; set; }
    public long TotalMessagesSent { get; set; }
    public long TotalMessagesReceived { get; set; }
    public long TotalBytesTransferred { get; set; }
}

/// <summary>
/// 服务端会话管理器接口 - 三层抽象架构增强版
/// 提供IClientSession的管理和业务功能
/// </summary>
public interface IServerSessionManager : IDisposable
{
    /// <summary>
    /// 会话超时时间（毫秒）
    /// </summary>
    int SessionTimeoutMs { get; set; }

    /// <summary>
    /// 当前会话数
    /// </summary>
    int SessionCount { get; }

    /// <summary>
    /// 所有会话ID
    /// </summary>
    IEnumerable<string> SessionIds { get; }

    /// <summary>
    /// 会话创建事件
    /// </summary>
    event EventHandler<SessionEventArgs>? SessionCreated;

    /// <summary>
    /// 会话移除事件
    /// </summary>
    event EventHandler<SessionEventArgs>? SessionRemoved;

    /// <summary>
    /// 会话认证事件
    /// </summary>
    event EventHandler<SessionAuthenticatedEventArgs>? SessionAuthenticated;

    /// <summary>
    /// 从服务端通道创建新的客户端会话
    /// </summary>
    /// <param name="serverChannel">服务端通道</param>
    /// <param name="descriptor">会话描述符</param>
    /// <returns>创建的客户端会话</returns>
    IClientSession CreateSession(IServerChannel serverChannel, ClientSessionDescriptor descriptor);

    /// <summary>
    /// 获取指定的客户端会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>客户端会话，如果不存在则返回null</returns>
    IClientSession? GetSession(string sessionId);

    /// <summary>
    /// 根据连接ID获取客户端会话
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>客户端会话，如果不存在则返回null</returns>
    IClientSession? GetSessionByConnectionId(string connectionId);

    /// <summary>
    /// 移除指定的客户端会话
    /// </summary>
    /// <param name="sessionId">会话ID</param>
    /// <returns>是否成功移除</returns>
    bool RemoveSession(string sessionId);

    /// <summary>
    /// 获取所有客户端会话
    /// </summary>
    /// <returns>所有客户端会话的集合</returns>
    IEnumerable<IClientSession> GetAllSessions();

    /// <summary>
    /// 获取所有已认证的客户端会话
    /// </summary>
    /// <returns>已认证的客户端会话集合</returns>
    IEnumerable<IClientSession> GetAuthenticatedSessions();

    /// <summary>
    /// 获取所有可用的客户端会话（已连接且健康）
    /// </summary>
    /// <returns>可用的客户端会话集合</returns>
    IEnumerable<IClientSession> GetAvailableSessions();

    /// <summary>
    /// 根据认证用户名获取客户端会话
    /// </summary>
    /// <param name="username">用户名</param>
    /// <returns>用户的客户端会话集合</returns>
    IEnumerable<IClientSession> GetSessionsByUser(string username);

    /// <summary>
    /// 根据组名获取客户端会话
    /// </summary>
    /// <param name="groupName">组名</param>
    /// <returns>该组的客户端会话集合</returns>
    IEnumerable<IClientSession> GetSessionsByGroup(string groupName);

    /// <summary>
    /// 根据标签获取客户端会话
    /// </summary>
    /// <param name="tagKey">标签键</param>
    /// <param name="tagValue">标签值</param>
    /// <returns>匹配标签的客户端会话集合</returns>
    IEnumerable<IClientSession> GetSessionsByTag(string tagKey, string tagValue);

    /// <summary>
    /// 向指定组广播Hub方法调用
    /// </summary>
    /// <typeparam name="THub">Hub接口类型</typeparam>
    /// <param name="groupName">组名</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="args">方法参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功调用的会话数量</returns>
    Task<int> BroadcastToGroupAsync<THub>(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub;

    /// <summary>
    /// 向所有已认证会话广播Hub方法调用
    /// </summary>
    /// <typeparam name="THub">Hub接口类型</typeparam>
    /// <param name="methodName">方法名称</param>
    /// <param name="args">方法参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>成功调用的会话数量</returns>
    Task<int> BroadcastToAllAsync<THub>(string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub;

    /// <summary>
    /// 检查所有会话的健康状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果集合</returns>
    Task<IEnumerable<SessionHealthCheckResult>> CheckAllSessionsHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理不健康的会话
    /// </summary>
    /// <param name="maxUnhealthyDuration">允许的最大不健康持续时间</param>
    /// <returns>清理的会话数量</returns>
    Task<int> CleanupUnhealthySessionsAsync(TimeSpan maxUnhealthyDuration);

    /// <summary>
    /// 获取会话管理器统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    SessionManagerStats GetSessionManagerStats();
}
