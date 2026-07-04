namespace PulseRPC.Server.Services;

/// <summary>
/// 统一的 PulseRPC 服务接口 - 所有服务实现的基础接口
/// </summary>
/// <remarks>
/// <para>
/// 此接口统一了 <see cref="IPulseService"/> 的服务标识能力和生命周期管理。
/// 所有需要调度隔离的服务都应实现此接口。
/// </para>
/// <para>
/// <strong>接口层次</strong>：
/// </para>
/// <code>
/// IPulseHub（含 [Channel("CLIENT")] 推送接收器）  ← 通信契约（定义 RPC 方法签名）
///         ↓
/// IPulseService        ← 服务实例（提供标识和生命周期）
///         ↓
/// [PulseServiceAttribute]     ← 服务元数据（声明启动类型、实例范围等）
/// </code>
/// <para>
/// <strong>组合使用示例</strong>：
/// </para>
/// <code>
/// // 定义通信契约
/// public interface IChatRoomHub : IPulseHub
/// {
///     Task SendMessageAsync(string message);
///     Task&lt;List&lt;Message&gt;&gt; GetHistoryAsync(int count);
/// }
///
/// // 实现服务（同时实现契约和服务接口）
/// [PulseService(
///     StartupType = ServiceStartupType.OnDemand,
///     InstanceScope = ServiceInstanceScope.MultiInstance)]
/// public class ChatRoomService : IPulseService, IChatRoomHub
/// {
///     public string ServiceType => "ChatRoom";
///     public string ServiceId { get; }
///
///     public ChatRoomService(string roomId)
///     {
///         ServiceId = roomId;
///     }
///
///     public Task SendMessageAsync(string message) { ... }
///     public Task&lt;List&lt;Message&gt;&gt; GetHistoryAsync(int count) { ... }
/// }
/// </code>
/// </remarks>
public interface IPulseService : IAsyncDisposable
{
    /// <summary>
    /// 服务类型标识（不可变）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用于标识服务的逻辑类型，通常与业务领域对应。
    /// </para>
    /// <para>
    /// <strong>命名约定</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>使用 PascalCase 命名</description></item>
    /// <item><description>使用名词或名词短语</description></item>
    /// <item><description>例如：ChatRoom、GameMatch、UserSession、Leaderboard</description></item>
    /// </list>
    /// <para>
    /// <strong>重要</strong>：ServiceType 在服务生命周期内不可变。
    /// </para>
    /// </remarks>
    /// <value>服务类型名称字符串</value>
    string ServiceType { get; }

    /// <summary>
    /// 服务实例唯一标识符（不可变）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用于在同一 ServiceType 下唯一标识一个服务实例。
    /// </para>
    /// <para>
    /// <strong>标识符格式</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>全集群唯一服务：固定值，如 "global" 或 "master"</description></item>
    /// <item><description>进程内唯一服务：固定值，如 "local" 或进程标识</description></item>
    /// <item><description>多实例服务：业务标识，如 "room-123"、"user-456"</description></item>
    /// </list>
    /// <para>
    /// <strong>完整服务地址</strong>：{ServiceType}:{ServiceId}
    /// </para>
    /// <code>
    /// "ChatRoom:room-123"
    /// "GameMatch:match-456"
    /// "Leaderboard:global"
    /// </code>
    /// <para>
    /// <strong>重要</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>必须在构造函数中初始化，之后不可更改</description></item>
    /// <item><description>长度限制：1 到 256 字符</description></item>
    /// <item><description>允许字符：字母、数字、连字符、下划线</description></item>
    /// </list>
    /// </remarks>
    /// <value>服务实例唯一标识符字符串</value>
    string ServiceId { get; }

    /// <summary>
    /// 获取服务的完整标识地址
    /// </summary>
    /// <remarks>
    /// 格式：{ServiceType}:{ServiceId}
    /// </remarks>
    string ServiceAddress => $"{ServiceType}:{ServiceId}";

    /// <summary>
    /// 服务当前生命周期状态
    /// </summary>
    ServiceLifecycleState State { get; }

    /// <summary>
    /// 启动服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// <para>
    /// 启动流程：
    /// </para>
    /// <list type="number">
    /// <item><description>状态变更：Created → Starting</description></item>
    /// <item><description>执行 <see cref="OnStartingAsync"/> 钩子</description></item>
    /// <item><description>状态变更：Starting → Running</description></item>
    /// </list>
    /// </remarks>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// <para>
    /// 停止流程：
    /// </para>
    /// <list type="number">
    /// <item><description>状态变更：Running → Stopping</description></item>
    /// <item><description>等待队列中的消息处理完成（可配置超时）</description></item>
    /// <item><description>执行 <see cref="OnStoppingAsync"/> 钩子</description></item>
    /// <item><description>状态变更：Stopping → Stopped</description></item>
    /// </list>
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务生命周期扩展接口 - 提供启动/停止时的钩子
/// </summary>
/// <remarks>
/// <para>
/// 实现此接口以在服务启动/停止时执行自定义逻辑。
/// </para>
/// </remarks>
public interface IPulseServiceLifecycle : IPulseService
{
    /// <summary>
    /// 服务启动时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// 在此方法中执行：
    /// <list type="bullet">
    /// <item><description>从持久化存储加载状态</description></item>
    /// <item><description>建立外部连接</description></item>
    /// <item><description>初始化资源</description></item>
    /// </list>
    /// </remarks>
    Task OnStartingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务停止时调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <remarks>
    /// 在此方法中执行：
    /// <list type="bullet">
    /// <item><description>保存状态到持久化存储</description></item>
    /// <item><description>关闭外部连接</description></item>
    /// <item><description>释放资源</description></item>
    /// </list>
    /// </remarks>
    Task OnStoppingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务健康检查接口
/// </summary>
public interface IPulseServiceHealthCheck : IPulseService
{
    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    /// <remarks>
    /// <para>
    /// 健康检查应快速完成（&lt; 100ms），避免执行 IO 操作。
    /// </para>
    /// <para>
    /// 检查项示例：
    /// </para>
    /// <list type="bullet">
    /// <item><description>内存使用是否超过阈值</description></item>
    /// <item><description>队列积压是否过多</description></item>
    /// <item><description>最近是否有错误</description></item>
    /// </list>
    /// </remarks>
    Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务生命周期状态枚举
/// </summary>
/// <remarks>
/// 注意：与 <see cref="PulseRPC.Server.Models.ServiceState"/> 不同，
/// 此枚举表示服务实例的运行时生命周期状态，而非服务注册状态。
/// </remarks>
public enum ServiceLifecycleState
{
    /// <summary>
    /// 已创建，尚未启动
    /// </summary>
    Created = 0,

    /// <summary>
    /// 正在启动
    /// </summary>
    Starting = 1,

    /// <summary>
    /// 正在运行
    /// </summary>
    Running = 2,

    /// <summary>
    /// 正在停止
    /// </summary>
    Stopping = 3,

    /// <summary>
    /// 已停止
    /// </summary>
    Stopped = 4,

    /// <summary>
    /// 发生错误
    /// </summary>
    Faulted = 5
}

/// <summary>
/// 服务健康检查结果
/// </summary>
public sealed class ServiceHealthCheckResult
{
    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// 健康状态描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 附加数据
    /// </summary>
    public IReadOnlyDictionary<string, object>? Data { get; init; }

    /// <summary>
    /// 创建健康结果
    /// </summary>
    public static ServiceHealthCheckResult Healthy(string? description = null)
        => new() { IsHealthy = true, Description = description };

    /// <summary>
    /// 创建不健康结果
    /// </summary>
    public static ServiceHealthCheckResult Unhealthy(string description, IReadOnlyDictionary<string, object>? data = null)
        => new() { IsHealthy = false, Description = description, Data = data };
}

