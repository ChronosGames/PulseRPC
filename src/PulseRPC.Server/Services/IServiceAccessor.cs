namespace PulseRPC.Server.Services;

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
///         // 获取或创建对应的 Service 实例
///         var playerId = "some-player-id"; // 从请求上下文或参数获取
///         var service = await _playerService.GetAsync(playerId);
///
///         // 在 Service 队列中执行操作
///         return await service.EnqueueAsync(() =&gt; service.GetPlayerInfoAsync());
///     }
/// }
/// </code>
/// </remarks>
/// <typeparam name="TService">服务类型</typeparam>
public interface IServiceAccessor<TService> where TService : class, IPulseService
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

    // ═══════════════════════════════════════════════════════════════════════
    // 生命周期管理（合并自 IPulseServiceFactory）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 移除并释放服务实例
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果移除成功返回 <c>true</c>，否则返回 <c>false</c></returns>
    /// <remarks>
    /// <para>
    /// 移除实例前会调用 <see cref="IPulseServiceLifecycle.OnStoppingAsync"/>，
    /// 然后调用 <see cref="IAsyncDisposable.DisposeAsync"/>（如果实现）。
    /// </para>
    /// <para>
    /// <strong>使用场景</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>管理员手动关闭房间</description></item>
    /// <item><description>业务逻辑要求立即释放资源</description></item>
    /// <item><description>测试时清理实例</description></item>
    /// </list>
    /// </remarks>
    ValueTask<bool> RemoveAsync(string serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有活跃的服务实例 ID
    /// </summary>
    /// <returns>活跃的 ServiceId 集合</returns>
    /// <remarks>
    /// <para>
    /// 返回当前缓存中所有实例的 ServiceId。
    /// </para>
    /// <para>
    /// <strong>使用场景</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>监控和统计</description></item>
    /// <item><description>管理界面显示</description></item>
    /// <item><description>批量操作</description></item>
    /// </list>
    /// <para>
    /// <strong>注意</strong>：返回的集合是快照，可能与实际状态有延迟。
    /// </para>
    /// </remarks>
    IReadOnlyCollection<string> GetActiveServiceIds();

    /// <summary>
    /// 获取当前活跃实例数量
    /// </summary>
    /// <value>活跃实例的数量</value>
    /// <remarks>
    /// <para>
    /// <strong>使用场景</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>监控指标</description></item>
    /// <item><description>容量规划</description></item>
    /// <item><description>告警阈值检查</description></item>
    /// </list>
    /// </remarks>
    int ActiveCount { get; }
}
