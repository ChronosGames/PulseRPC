using System.Net;
using PulseRPC.Authentication;
using PulseRPC.Transport;

namespace PulseRPC.Server.Sessions;

/// <summary>
/// 服务端客户端会话接口 - 三层抽象架构的应用层（服务端）
/// 代表服务端管理的客户端连接会话，提供业务层功能
/// </summary>
public interface IClientSession : ISessionChannel
{
    /// <summary>
    /// 会话描述符，包含会话标识和元数据
    /// </summary>
    ClientSessionDescriptor Descriptor { get; }

    /// <summary>
    /// 会话健康状态
    /// </summary>
    SessionHealth Health { get; }

    /// <summary>
    /// 会话统计信息
    /// </summary>
    SessionStatistics Statistics { get; }

    /// <summary>
    /// 会话是否可用（已连接且健康）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 用户组或角色标识（用于权限控制）
    /// </summary>
    IReadOnlyList<string> Groups { get; }

    /// <summary>
    /// 会话标签（用于分类和路由）
    /// </summary>
    IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>
    /// 向客户端发送Hub方法调用
    /// </summary>
    /// <typeparam name="THub">Hub接口类型</typeparam>
    /// <param name="methodName">方法名称</param>
    /// <param name="args">方法参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>调用结果</returns>
    Task<TResult> InvokeAsync<THub, TResult>(string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub;

    /// <summary>
    /// 向客户端发送Hub方法调用（无返回值）
    /// </summary>
    /// <typeparam name="THub">Hub接口类型</typeparam>
    /// <param name="methodName">方法名称</param>
    /// <param name="args">方法参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InvokeAsync<THub>(string methodName, object?[] args, CancellationToken cancellationToken = default)
        where THub : class, IPulseHub;

    /// <summary>
    /// 检查会话健康状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    Task<SessionHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置会话组/角色
    /// </summary>
    /// <param name="groups">组或角色列表</param>
    void SetGroups(IEnumerable<string> groups);

    /// <summary>
    /// 添加会话标签
    /// </summary>
    /// <param name="key">标签键</param>
    /// <param name="value">标签值</param>
    void SetTag(string key, string value);

    /// <summary>
    /// 移除会话标签
    /// </summary>
    /// <param name="key">标签键</param>
    /// <returns>是否成功移除</returns>
    bool RemoveTag(string key);

    /// <summary>
    /// 会话健康状态变化事件
    /// </summary>
    event EventHandler<SessionHealthChangedEventArgs>? HealthChanged;

    /// <summary>
    /// 会话组变化事件
    /// </summary>
    event EventHandler<SessionGroupsChangedEventArgs>? GroupsChanged;
}

/// <summary>
/// 客户端会话描述符
/// </summary>
public sealed class ClientSessionDescriptor
{
    /// <summary>
    /// 会话唯一标识
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 会话名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 客户端标识
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// 客户端版本
    /// </summary>
    public string? ClientVersion { get; init; }

    /// <summary>
    /// 传输协议类型
    /// </summary>
    public required TransportType Transport { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 会话超时时间（毫秒）
    /// </summary>
    public int TimeoutMs { get; init; } = 300000; // 5分钟

    /// <summary>
    /// 是否启用自动清理过期会话
    /// </summary>
    public bool AutoCleanup { get; init; } = true;
}

/// <summary>
/// 会话健康状态枚举
/// </summary>
public enum SessionHealth
{
    /// <summary>
    /// 健康状态
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// 降级状态（部分功能受限）
    /// </summary>
    Degraded = 1,

    /// <summary>
    /// 不健康状态（需要重新连接）
    /// </summary>
    Unhealthy = 2,

    /// <summary>
    /// 失败状态（连接已断开）
    /// </summary>
    Failed = 3
}

/// <summary>
/// 会话统计信息
/// </summary>
public sealed class SessionStatistics
{
    /// <summary>
    /// 会话持续时间
    /// </summary>
    public TimeSpan Duration => DateTime.UtcNow - CreatedAt;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 发送的消息数量
    /// </summary>
    public long MessagesSent { get; set; }

    /// <summary>
    /// 接收的消息数量
    /// </summary>
    public long MessagesReceived { get; set; }

    /// <summary>
    /// 发送的字节数
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// 接收的字节数
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Hub方法调用次数
    /// </summary>
    public long HubInvocations { get; set; }

    /// <summary>
    /// 异常次数
    /// </summary>
    public long Exceptions { get; set; }
}

/// <summary>
/// 会话健康检查结果
/// </summary>
public sealed class SessionHealthCheckResult
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 健康状态
    /// </summary>
    public required SessionHealth Health { get; init; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; init; }

    /// <summary>
    /// 检查消息
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 会话健康状态变化事件参数
/// </summary>
public sealed class SessionHealthChangedEventArgs : EventArgs
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 之前的健康状态
    /// </summary>
    public SessionHealth PreviousHealth { get; init; }

    /// <summary>
    /// 当前的健康状态
    /// </summary>
    public required SessionHealth CurrentHealth { get; init; }

    /// <summary>
    /// 变化时间
    /// </summary>
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 变化原因
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// 会话组变化事件参数
/// </summary>
public sealed class SessionGroupsChangedEventArgs : EventArgs
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 之前的组列表
    /// </summary>
    public IReadOnlyList<string> PreviousGroups { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 当前的组列表
    /// </summary>
    public required IReadOnlyList<string> CurrentGroups { get; init; }

    /// <summary>
    /// 变化时间
    /// </summary>
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
}