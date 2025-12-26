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
