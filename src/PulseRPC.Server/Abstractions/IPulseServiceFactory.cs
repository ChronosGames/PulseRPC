using System.Diagnostics.CodeAnalysis;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例工厂接口
/// </summary>
/// <typeparam name="TService">服务类型，必须实现 <see cref="IPulseService"/></typeparam>
/// <remarks>
/// <para>
/// 管理有状态的 <see cref="IPulseService"/> 实例的生命周期，解决"多个无状态 IPulseHub 共享同一个有状态 IPulseService 实例"的架构需求。
/// </para>
/// <para>
/// <strong>核心功能</strong>：
/// </para>
/// <list type="bullet">
/// <item><description><strong>按需创建</strong>：根据 ServiceId 自动创建服务实例</description></item>
/// <item><description><strong>实例缓存</strong>：缓存已创建的实例，避免重复创建</description></item>
/// <item><description><strong>空闲清理</strong>：自动清理长时间未使用的实例</description></item>
/// <item><description><strong>LRU 驱逐</strong>：当缓存满时，驱逐最少使用的实例</description></item>
/// <item><description><strong>生命周期管理</strong>：自动调用 <see cref="IServiceLifecycle"/> 钩子</description></item>
/// <item><description><strong>健康检查</strong>：定期检查实例健康状态，自动移除不健康的实例</description></item>
/// </list>
/// <para>
/// <strong>线程安全</strong>：所有方法都是线程安全的，支持高并发访问。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // 1. 定义服务
/// public class ChatRoomService : IPulseService, IServiceLifecycle
/// {
///     public string ServiceName => "ChatRoom";
///     public string ServiceId { get; }
///
///     private readonly List&lt;Message&gt; _messages = new();
///
///     public ChatRoomService(string roomId)
///     {
///         ServiceId = $"ChatRoom:{roomId}";
///     }
///
///     public void AddMessage(Message msg) => _messages.Add(msg);
/// }
///
/// // 2. 在 Hub 中使用
/// public class ChatRoomHub : IPulseHub
/// {
///     private readonly IPulseServiceFactory&lt;ChatRoomService&gt; _factory;
///
///     public ChatRoomHub(IPulseServiceFactory&lt;ChatRoomService&gt; factory)
///     {
///         _factory = factory;
///     }
///
///     public async Task SendMessageAsync(string roomId, string text)
///     {
///         var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
///         service.AddMessage(new Message { Text = text });
///     }
/// }
///
/// // 3. DI 注册
/// services.AddPulseServiceFactory&lt;ChatRoomService&gt;(
///     (sp, serviceId) =>
///     {
///         var roomId = serviceId.Split(':')[1];
///         return new ChatRoomService(roomId);
///     });
/// </code>
/// </example>
public interface IPulseServiceFactory<TService> where TService : IPulseService
{
    /// <summary>
    /// 获取或创建服务实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符（完整 ServiceId，格式：ServiceName:BusinessId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务实例</returns>
    /// <remarks>
    /// <para>
    /// 如果实例已存在于缓存中，直接返回；否则创建新实例并调用 <see cref="IServiceLifecycle.OnActivateAsync"/>。
    /// </para>
    /// <para>
    /// <strong>并发安全</strong>：多个线程同时请求同一 ServiceId 时，只会创建一个实例。
    /// </para>
    /// <para>
    /// <strong>性能特性</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>缓存命中：O(1) 时间复杂度</description></item>
    /// <item><description>缓存未命中：创建实例 + 激活钩子</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">serviceId 为 null 或空白字符串</exception>
    /// <exception cref="ServiceCreationException">服务实例创建失败</exception>
    /// <exception cref="ServiceActivationException">服务实例激活失败</exception>
    ValueTask<TService> GetOrCreateAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试获取已存在的服务实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="service">输出服务实例</param>
    /// <returns>如果实例存在返回 <c>true</c>，否则返回 <c>false</c></returns>
    /// <remarks>
    /// <para>
    /// 与 <see cref="GetOrCreateAsync"/> 不同，此方法不会创建新实例。
    /// </para>
    /// <para>
    /// <strong>使用场景</strong>：需要检查实例是否存在，但不希望创建新实例。
    /// </para>
    /// </remarks>
    bool TryGet(string serviceId, [NotNullWhen(true)] out TService? service);

    /// <summary>
    /// 移除服务实例
    /// </summary>
    /// <param name="serviceId">服务实例的唯一标识符</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>如果移除成功返回 <c>true</c>，否则返回 <c>false</c></returns>
    /// <remarks>
    /// <para>
    /// 移除实例前会调用 <see cref="IServiceLifecycle.OnDeactivateAsync"/>，然后调用 <see cref="IDisposable.Dispose"/>（如果实现）。
    /// </para>
    /// <para>
    /// <strong>使用场景</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>管理员手动关闭房间</description></item>
    /// <item><description>业务逻辑要求立即释放资源</description></item>
    /// <item><description>测试时清理实例</description></item>
    /// </list>
    /// <para>
    /// <strong>注意</strong>：停用钩子中的异常会被记录但不影响实例移除。
    /// </para>
    /// </remarks>
    ValueTask<bool> RemoveAsync(
        string serviceId,
        CancellationToken cancellationToken = default);

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

/// <summary>
/// 服务创建异常
/// </summary>
/// <remarks>
/// 当服务工厂无法创建服务实例时抛出此异常。
/// </remarks>
public class ServiceCreationException : Exception
{
    /// <summary>
    /// 服务实例 ID
    /// </summary>
    public string ServiceId { get; }

    /// <summary>
    /// 初始化 <see cref="ServiceCreationException"/> 类的新实例
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public ServiceCreationException(string serviceId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceId = serviceId;
    }
}

/// <summary>
/// 服务激活异常
/// </summary>
/// <remarks>
/// 当服务实例激活失败（<see cref="IServiceLifecycle.OnActivateAsync"/> 抛出异常）时抛出此异常。
/// </remarks>
public class ServiceActivationException : Exception
{
    /// <summary>
    /// 服务实例 ID
    /// </summary>
    public string ServiceId { get; }

    /// <summary>
    /// 初始化 <see cref="ServiceActivationException"/> 类的新实例
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public ServiceActivationException(string serviceId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceId = serviceId;
    }
}
