using PulseRPC.Server.Contexts;
using PulseRPC.Server.ServiceManagement;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务访问器接口 - 在 IPulseHub 实现类中获取对应的 IPulseService 实例
/// </summary>
/// <remarks>
/// <para>
/// <strong>设计理念</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>IPulseHub 实现类应该是无状态的</description></item>
/// <item><description>状态应该存放在 IPulseService 中</description></item>
/// <item><description>通过 IServiceAccessor 获取 Service 实例来操作状态</description></item>
/// </list>
/// <para>
/// <strong>使用方式</strong>：
/// </para>
/// <code>
/// public class PlayerHub : IPlayerHub
/// {
///     private readonly IServiceAccessor&lt;PlayerService&gt; _playerService;
///
///     public PlayerHub(IServiceAccessor&lt;PlayerService&gt; playerService)
///     {
///         _playerService = playerService;
///     }
///
///     public async Task&lt;PlayerInfo&gt; GetInfoAsync()
///     {
///         // 从请求上下文获取 ServiceId（如 playerId）
///         var playerId = RequestContext.Current.UserId;
///
///         // 获取或创建对应的 Service 实例
///         var service = await _playerService.GetAsync(playerId);
///
///         // 在 Service 队列中执行操作
///         return await service.EnqueueAsync(() =&gt; service.GetPlayerInfoAsync());
///     }
/// }
/// </code>
/// </remarks>
/// <typeparam name="TService">服务类型</typeparam>
public interface IServiceAccessor<TService> where TService : class, IUnifiedPulseService
{
    /// <summary>
    /// 获取或创建服务实例
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    ValueTask<TService> GetAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取已存在的服务实例（不创建）
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <returns>服务实例，如果不存在则返回 null</returns>
    TService? TryGet(string serviceId);

    /// <summary>
    /// 获取所有活跃的服务实例
    /// </summary>
    IEnumerable<TService> GetAll();
}

/// <summary>
/// 带上下文的服务访问器 - 自动从请求上下文提取 ServiceId
/// </summary>
/// <typeparam name="TService">服务类型</typeparam>
/// <remarks>
/// <para>
/// 简化版本，自动从当前请求上下文中提取 ServiceId。
/// </para>
/// <code>
/// public class PlayerHub : IPlayerHub
/// {
///     private readonly IContextualServiceAccessor&lt;PlayerService&gt; _playerService;
///
///     public async Task&lt;PlayerInfo&gt; GetInfoAsync()
///     {
///         // 自动从上下文获取 playerId，无需手动传递
///         var service = await _playerService.GetCurrentAsync();
///         return await service.EnqueueAsync(() =&gt; service.GetPlayerInfoAsync());
///     }
/// }
/// </code>
/// </remarks>
public interface IContextualServiceAccessor<TService> : IServiceAccessor<TService>
    where TService : class, IUnifiedPulseService
{
    /// <summary>
    /// 从当前请求上下文获取服务实例
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    /// <exception cref="InvalidOperationException">当上下文中无法获取 ServiceId 时抛出</exception>
    ValueTask<TService> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试从当前请求上下文获取服务实例
    /// </summary>
    /// <returns>服务实例，如果上下文无效或服务不存在则返回 null</returns>
    TService? TryGetCurrent();
}

/// <summary>
/// ServiceId 提取策略
/// </summary>
public interface IServiceIdResolver
{
    /// <summary>
    /// 从当前上下文提取 ServiceId
    /// </summary>
    /// <returns>ServiceId，如果无法提取则返回 null</returns>
    string? ResolveServiceId();
}

/// <summary>
/// 基于用户 ID 的 ServiceId 解析器
/// </summary>
/// <remarks>
/// 最常用的场景：ServiceId = UserId（玩家服务）
/// </remarks>
public sealed class UserIdServiceIdResolver : IServiceIdResolver
{
    public string? ResolveServiceId()
    {
        // 从请求上下文获取用户 ID
        return RequestContext.Current?.UserId;
    }
}

/// <summary>
/// 基于请求头的 ServiceId 解析器
/// </summary>
public sealed class HeaderServiceIdResolver : IServiceIdResolver
{
    private readonly string _headerName;

    public HeaderServiceIdResolver(string headerName = "X-Service-Id")
    {
        _headerName = headerName;
    }

    public string? ResolveServiceId()
    {
        return RequestContext.Current?.GetHeader(_headerName);
    }
}

/// <summary>
/// 请求上下文（兼容层 - 转发到 UnifiedRequestContext）
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ 已废弃</strong>：请使用 <see cref="Contexts.UnifiedRequestContext"/> 替代。
/// </para>
/// <para>
/// 此类保留用于向后兼容，内部转发到统一上下文系统。
/// </para>
/// </remarks>
[Obsolete("使用 UnifiedRequestContext 替代。此类将在未来版本中移除。")]
public static class RequestContext
{
    /// <summary>
    /// 当前请求上下文（从 UnifiedRequestContext 转换）
    /// </summary>
    public static RequestContextData? Current
    {
        get
        {
            var unified = Contexts.UnifiedRequestContext.Current;
            if (unified == null) return null;

            return new RequestContextData
            {
                ConnectionId = unified.ConnectionId,
                UserId = unified.UserId,
                Headers = unified.Headers
            };
        }
        set
        {
            if (value == null)
            {
                Contexts.UnifiedRequestContext.Clear();
            }
            else
            {
                Contexts.UnifiedRequestContext.Current = new Contexts.UnifiedContextData
                {
                    ConnectionId = value.ConnectionId,
                    UserId = value.UserId,
                    CallerId = value.UserId ?? string.Empty,
                    Headers = value.Headers
                };
            }
        }
    }

    /// <summary>
    /// 设置当前上下文
    /// </summary>
    [Obsolete("使用 UnifiedRequestContext.SetContext 替代。")]
    public static IDisposable SetContext(RequestContextData context)
    {
        var unifiedContext = new Contexts.UnifiedContextData
        {
            ConnectionId = context.ConnectionId,
            UserId = context.UserId,
            CallerId = context.UserId ?? string.Empty,
            Headers = context.Headers
        };

        return Contexts.UnifiedRequestContext.SetContext(unifiedContext);
    }

    /// <summary>
    /// 内部方法：直接设置连接信息（供 YieldingService 等内部使用）
    /// </summary>
    internal static void SetCurrent(object? sender)
    {
        // 兼容旧代码的内部调用
        if (sender == null)
        {
            Contexts.UnifiedRequestContext.Clear();
        }
    }
}

/// <summary>
/// 请求上下文数据（兼容层）
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ 已废弃</strong>：请使用 <see cref="Contexts.UnifiedContextData"/> 替代。
/// </para>
/// </remarks>
[Obsolete("使用 UnifiedContextData 替代。此类将在未来版本中移除。")]
public sealed class RequestContextData
{
    /// <summary>
    /// 连接 ID
    /// </summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// 用户 ID（已认证的用户）
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// 请求头
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// 获取请求头
    /// </summary>
    public string? GetHeader(string name)
    {
        if (Headers == null) return null;
        return Headers.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// 自定义属性
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
}

