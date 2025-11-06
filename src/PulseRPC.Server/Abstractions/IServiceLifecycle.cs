namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例生命周期接口
/// </summary>
/// <remarks>
/// <para>
/// 提供服务实例的激活、停用和健康检查钩子，配合 <see cref="IPulseServiceFactory{TService}"/> 使用。
/// </para>
/// <para>
/// <strong>生命周期流程</strong>：
/// </para>
/// <list type="number">
/// <item><description><strong>激活</strong>：实例首次创建后调用 <see cref="OnActivateAsync"/></description></item>
/// <item><description><strong>活跃</strong>：处理业务请求</description></item>
/// <item><description><strong>健康检查</strong>：定期调用 <see cref="OnHealthCheckAsync"/> 检查实例健康状态</description></item>
/// <item><description><strong>停用</strong>：实例从缓存移除前调用 <see cref="OnDeactivateAsync"/></description></item>
/// </list>
/// <para>
/// <strong>典型使用场景</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>激活时从数据库加载持久化状态</description></item>
/// <item><description>停用时保存状态到数据库</description></item>
/// <item><description>健康检查时验证内存使用、连接状态等</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class ChatRoomService : IPulseService, IServiceLifecycle
/// {
///     private readonly IChatRepository _repository;
///     private List&lt;Message&gt; _messages = new();
///
///     public async Task OnActivateAsync(CancellationToken cancellationToken = default)
///     {
///         // 从数据库加载历史消息
///         var messages = await _repository.LoadMessagesAsync(ServiceId, cancellationToken);
///         _messages = messages.ToList();
///     }
///
///     public async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
///     {
///         // 保存消息到数据库
///         await _repository.SaveMessagesAsync(ServiceId, _messages, cancellationToken);
///     }
///
///     public Task&lt;bool&gt; OnHealthCheckAsync(CancellationToken cancellationToken = default)
///     {
///         // 检查消息数量是否超过限制
///         var isHealthy = _messages.Count &lt; 10000;
///         return Task.FromResult(isHealthy);
///     }
/// }
/// </code>
/// </example>
public interface IServiceLifecycle
{
    /// <summary>
    /// 服务实例激活时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// <para>
    /// 在实例首次创建后、处理第一个请求前调用。
    /// </para>
    /// <para>
    /// <strong>推荐用途</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>从持久化存储加载状态</description></item>
    /// <item><description>建立外部连接（数据库、缓存等）</description></item>
    /// <item><description>初始化资源</description></item>
    /// </list>
    /// <para>
    /// <strong>重要提示</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>应提供降级处理，避免抛出异常导致实例创建失败</description></item>
    /// <item><description>执行时间应控制在 1 秒以内，避免阻塞实例创建</description></item>
    /// <item><description>实现应具备幂等性，支持重试</description></item>
    /// </list>
    /// </remarks>
    /// <returns>表示异步操作的任务</returns>
    Task OnActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务实例停用时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// <para>
    /// 在实例从缓存移除前调用，触发场景包括：
    /// </para>
    /// <list type="bullet">
    /// <item><description>空闲超时清理</description></item>
    /// <item><description>健康检查失败</description></item>
    /// <item><description>LRU 驱逐</description></item>
    /// <item><description>手动移除</description></item>
    /// </list>
    /// <para>
    /// <strong>推荐用途</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>保存状态到持久化存储</description></item>
    /// <item><description>释放外部资源（连接、文件句柄等）</description></item>
    /// <item><description>清理临时文件</description></item>
    /// </list>
    /// <para>
    /// <strong>重要提示</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>不应抛出异常，异常会被记录但不影响实例移除</description></item>
    /// <item><description>执行时间应控制在 1 秒以内，避免阻塞清理线程</description></item>
    /// <item><description>应具备幂等性，支持重复调用</description></item>
    /// </list>
    /// </remarks>
    /// <returns>表示异步操作的任务</returns>
    Task OnDeactivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 健康检查时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// <para>
    /// 定期调用以检查实例健康状态，返回 <c>false</c> 将触发实例移除。
    /// </para>
    /// <para>
    /// <strong>推荐检查项</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>内存使用是否超过限制</description></item>
    /// <item><description>外部连接是否正常</description></item>
    /// <item><description>业务状态是否有效</description></item>
    /// <item><description>请求处理是否正常</description></item>
    /// </list>
    /// <para>
    /// <strong>重要提示</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>执行时间应控制在 100ms 以内，避免阻塞健康检查线程</description></item>
    /// <item><description>不应执行 IO 操作（数据库查询、网络请求等）</description></item>
    /// <item><description>异常会被视为不健康，实例将被移除</description></item>
    /// </list>
    /// </remarks>
    /// <returns>表示实例是否健康的任务，<c>true</c> 表示健康，<c>false</c> 表示不健康</returns>
    Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default);
}
