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
///         var playerId = PulseContext.CurrentUserId;
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
        return PulseContext.CurrentUserId;
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
        return PulseContext.Current?.GetHeader(_headerName);
    }
}
