using System.Diagnostics.CodeAnalysis;
using PulseRPC.Server.Services;

namespace PulseRPC.Server.Hubs;

/// <summary>
/// Hub 实例工厂接口
/// </summary>
/// <typeparam name="THub">Hub 类型，必须实现 <see cref="IPulseHub"/></typeparam>
/// <typeparam name="TService">Service 类型，必须实现 <see cref="IPulseService"/></typeparam>
/// <remarks>
/// <para>
/// 管理 Hub 实例的生命周期，Hub 和 Service 实例一对一绑定。
/// </para>
/// <para>
/// <strong>核心特性</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><strong>一对一绑定</strong>：每个 Hub 实例对应一个 Service 实例</description></item>
/// <item><description><strong>自动注入</strong>：Hub 构造函数自动接收 Service 实例</description></item>
/// <item><description><strong>生命周期同步</strong>：Hub 和 Service 的生命周期自动同步</description></item>
/// <item><description><strong>缓存复用</strong>：相同 ServiceId 的 Hub 会被缓存和复用</description></item>
/// </list>
/// <para>
/// <strong>使用场景</strong>：
/// </para>
/// <para>
/// 当 Hub 需要频繁访问同一个 Service 实例时，使用 HubFactory 可以简化代码：
/// </para>
/// <code>
/// // 不再需要每次调用 ServiceFactory
/// public async Task SendMessageAsync(string text)
/// {
///     _service.AddMessage(new Message { Text = text });
/// }
/// </code>
/// </remarks>
/// <example>
/// <code>
/// // 1. 定义 Hub（Service 直接注入）
/// public class ChatRoomHub : IPulseHub
/// {
///     private readonly ChatRoomService _service;
///
///     public ChatRoomHub(ChatRoomService service)
///     {
///         _service = service;
///     }
///
///     public Task SendMessageAsync(string text)
///     {
///         _service.AddMessage(new Message { Text = text });
///         return Task.CompletedTask;
///     }
/// }
///
/// // 2. 注册
/// services.AddPulseHubFactory&lt;ChatRoomHub, ChatRoomService&gt;(
///     (sp, service) => new ChatRoomHub(service));
///
/// // 3. 使用
/// var hub = await _hubFactory.GetOrCreateAsync("ChatRoom:room-1");
/// await hub.SendMessageAsync("Hello");
/// </code>
/// </example>
public interface IPulseHubFactory<THub, TService>
    where THub : class
    where TService : IPulseService
{
    /// <summary>
    /// 获取或创建 Hub 实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符（完整 ServiceId，格式：ServiceName:BusinessId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Hub 实例</returns>
    /// <remarks>
    /// <para>
    /// 此方法会：
    /// </para>
    /// <list type="number">
    /// <item><description>通过 ServiceFactory 获取或创建 Service 实例</description></item>
    /// <item><description>创建 Hub 实例并注入 Service</description></item>
    /// <item><description>缓存 Hub 实例以便复用</description></item>
    /// </list>
    /// <para>
    /// <strong>并发安全</strong>：多个线程同时请求同一 ServiceId 时，只会创建一个 Hub 实例。
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">serviceId 为 null 或空白字符串</exception>
    ValueTask<THub> GetOrCreateAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取已存在的 Hub 实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="hub">输出 Hub 实例</param>
    /// <returns>如果实例存在返回 <c>true</c>，否则返回 <c>false</c></returns>
    bool TryGet(string serviceId, [NotNullWhen(true)] out THub? hub);

    /// <summary>
    /// 移除 Hub 实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果移除成功返回 <c>true</c>，否则返回 <c>false</c></returns>
    /// <remarks>
    /// <para>
    /// 移除 Hub 实例时，会同时移除对应的 Service 实例。
    /// </para>
    /// </remarks>
    ValueTask<bool> RemoveAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有活跃的 Hub 实例 ID
    /// </summary>
    IReadOnlyCollection<string> GetActiveServiceIds();

    /// <summary>
    /// 获取当前活跃 Hub 实例数量
    /// </summary>
    int ActiveCount { get; }
}
