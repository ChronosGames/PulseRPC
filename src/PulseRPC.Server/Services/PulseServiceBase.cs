using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Scheduling;
using PulseRPC.Server.Hubs; using PulseRPC.Server.Services; using PulseRPC.Server.Transport;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Shared;

namespace PulseRPC.Server.Services;

/// <summary>
/// 统一的 PulseService 基类实现
/// </summary>
/// <remarks>
/// <para>
/// 提供 <see cref="IPulseService"/> 的默认实现，包括：
/// </para>
/// <list type="bullet">
/// <item><description>状态管理（Created → Running → Stopped）</description></item>
/// <item><description>消息队列（可选的专属队列）</description></item>
/// <item><description>生命周期钩子</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// [PulseService(
///     StartupType = ServiceStartupType.OnDemand,
///     InstanceScope = ServiceInstanceScope.MultiInstance)]
/// public class ChatRoomService : PulseServiceBase, IChatRoomHub
/// {
///     private readonly List&lt;Message&gt; _messages = new();
///
///     public ChatRoomService(string roomId, ILogger&lt;ChatRoomService&gt; logger)
///         : base("ChatRoom", roomId, logger)
///     {
///     }
///
///     public Task SendMessageAsync(string message)
///     {
///         _messages.Add(new Message(message));
///         return Task.CompletedTask;
///     }
///
///     protected override Task OnStartingAsync(CancellationToken cancellationToken)
///     {
///         // 加载历史消息
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </remarks>
public abstract class PulseServiceBase : IPulseService, IPulseServiceLifecycle, IPulseServiceHealthCheck
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Channel<WorkItem>? _messageQueue;
    private readonly IThreadAffinityScheduler? _affinityScheduler;
    private Task? _messageProcessingTask;
    private CancellationTokenSource? _processingCts;

    // 固定帧驱动（[Tick]）相关状态
    private CancellationTokenSource? _tickCts;
    private Task? _tickLoopTask;

    // ════════════════════════════════════════════════════════════════════════
    // 请求上下文访问（通过 PulseContext 统一管理）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 获取当前请求的上下文
    /// </summary>
    /// <returns>请求上下文，如果没有则返回 null</returns>
    /// <remarks>
    /// 上下文由 PulseContext 统一管理，在消息处理引擎中自动设置。
    /// 在服务方法中可以使用此方法获取调用者信息。
    /// </remarks>
    protected static IPulseContext? GetCurrentContext() => PulseContext.Current;

    /// <summary>
    /// 获取当前调用者的 UserId
    /// </summary>
    protected static string? CurrentUserId => PulseContext.CurrentUserId;

    /// <summary>
    /// 获取当前调用者的 CallerId
    /// </summary>
    protected static string? CurrentCallerId => PulseContext.Current?.CallerId;

    /// <summary>
    /// 获取当前传输连接
    /// </summary>
    protected static IServerTransport? CurrentTransport => PulseContext.CurrentTransport;

    // ════════════════════════════════════════════════════════════════════════
    // 基础属性
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 日志记录器
    /// </summary>
    protected ILogger Logger { get; }

    /// <inheritdoc/>
    public string ServiceType { get; }

    /// <inheritdoc/>
    public string ServiceId { get; }

    /// <inheritdoc/>
    public ServiceLifecycleState State { get; private set; } = ServiceLifecycleState.Created;

    /// <summary>
    /// 服务执行配置
    /// </summary>
    /// <remarks>
    /// 通过 <see cref="ServiceExecutionOptions.FromAttribute"/> 从 Attribute 读取，
    /// 或通过构造函数传入自定义配置。
    /// </remarks>
    protected ServiceExecutionOptions ExecutionOptions { get; }

    /// <summary>
    /// 服务配置（已废弃，使用 ExecutionOptions）
    /// </summary>
    [Obsolete("使用 ExecutionOptions 替代。此属性将在未来版本中移除。")]
    protected ServiceExecutionOptions Configuration => ExecutionOptions;

    // ════════════════════════════════════════════════════════════════════════
    // 访问时间跟踪（用于实例清理）
    // ════════════════════════════════════════════════════════════════════════

    private long _lastAccessTimeTicks;
    private long _totalAccessCount;

    /// <summary>
    /// 服务创建时间
    /// </summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// 最后访问时间（最后一次调用 EnqueueAsync 的时间）
    /// </summary>
    public DateTime LastAccessTime => new(Interlocked.Read(ref _lastAccessTimeTicks), DateTimeKind.Utc);

    /// <summary>
    /// 总访问次数
    /// </summary>
    public long TotalAccessCount => Interlocked.Read(ref _totalAccessCount);

    /// <summary>
    /// 空闲时长（自最后访问以来的时间）
    /// </summary>
    public TimeSpan IdleDuration => DateTime.UtcNow - LastAccessTime;

    /// <summary>
    /// 更新访问时间（每次 EnqueueAsync 调用时自动调用）
    /// </summary>
    private void UpdateAccessTime()
    {
        Interlocked.Exchange(ref _lastAccessTimeTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref _totalAccessCount);
    }

    /// <summary>
    /// 创建新的 PulseServiceBase 实例
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="executionOptions">执行配置（可选，推荐使用 <see cref="ServiceExecutionOptions"/> 的预定义配置）</param>
    /// <param name="affinityScheduler">线程亲和性调度器（可选，仅 ThreadAffinity 模式需要）</param>
    /// <example>
    /// <code>
    /// // 使用预定义配置
    /// public class PlayerService : PulseServiceBase
    /// {
    ///     public PlayerService(string playerId, ILogger&lt;PlayerService&gt; logger)
    ///         : base("Player", playerId, logger, ServiceExecutionOptions.StatefulIO)
    ///     {
    ///     }
    /// }
    ///
    /// // 使用场景枚举
    /// public class ChatRoomService : PulseServiceBase
    /// {
    ///     public ChatRoomService(string roomId, ILogger&lt;ChatRoomService&gt; logger)
    ///         : base("ChatRoom", roomId, logger, ServiceExecutionOptions.FromScenario(ServiceScenario.Actor))
    ///     {
    ///     }
    /// }
    /// </code>
    /// </example>
    protected PulseServiceBase(
        string serviceType,
        string serviceId,
        ILogger? logger = null,
        ServiceExecutionOptions? executionOptions = null,
        IThreadAffinityScheduler? affinityScheduler = null)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
            throw new ArgumentException("ServiceType cannot be null or whitespace", nameof(serviceType));
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        ServiceType = serviceType;
        ServiceId = serviceId;
        Logger = logger ?? NullLogger.Instance;
        ExecutionOptions = executionOptions ?? ServiceExecutionOptions.Default;
        _affinityScheduler = affinityScheduler;

        // 根据调度模式创建消息队列
        if (ExecutionOptions.SchedulingMode == ServiceSchedulingMode.DedicatedQueue)
        {
            _messageQueue = Channel.CreateBounded<WorkItem>(
                new BoundedChannelOptions(ExecutionOptions.QueueCapacity)
                {
                    FullMode = ExecutionOptions.BackpressureMode switch
                    {
                        ServiceBackpressureMode.Block => BoundedChannelFullMode.Wait,
                        ServiceBackpressureMode.DropNewest => BoundedChannelFullMode.DropWrite,
                        ServiceBackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
                        ServiceBackpressureMode.ThrowException => BoundedChannelFullMode.DropWrite,
                        _ => BoundedChannelFullMode.Wait
                    },
                    SingleReader = true,
                    SingleWriter = false
                },
                itemDropped: item => item.Reject(new InvalidOperationException(
                    $"Service mailbox is full and rejected a request ({ExecutionOptions.BackpressureMode}): " +
                    ((IPulseService)this).ServiceAddress)));
        }
        // ThreadAffinity 模式使用共享调度器，不创建私有队列

        // 初始化访问时间
        _lastAccessTimeTicks = DateTime.UtcNow.Ticks;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (State != ServiceLifecycleState.Created && State != ServiceLifecycleState.Stopped)
            {
                Logger.LogWarning(
                    "Cannot start service in state {State}: {ServiceAddress}",
                    State, ((IPulseService)this).ServiceAddress);
                return;
            }

            State = ServiceLifecycleState.Starting;
            Logger.LogInformation("Starting service: {ServiceAddress}", ((IPulseService)this).ServiceAddress);

            try
            {
                // 启动消息处理循环
                if (_messageQueue != null)
                {
                    _processingCts = new CancellationTokenSource();
                    _messageProcessingTask = ProcessMessagesAsync(_processingCts.Token);
                }

                // 调用子类启动逻辑
                await OnStartingAsync(cancellationToken);

                State = ServiceLifecycleState.Running;
                Logger.LogInformation("Service started: {ServiceAddress}", ((IPulseService)this).ServiceAddress);

                // 启动固定帧驱动（若类上标注了 [Tick]）——须在 State=Running 之后，
                // 否则第一帧的 EnqueueAsync 会因服务未运行而被拒绝。
                StartTickLoop();
            }
            catch (Exception ex)
            {
                State = ServiceLifecycleState.Faulted;
                Logger.LogError(ex, "Failed to start service: {ServiceAddress}", ((IPulseService)this).ServiceAddress);
                throw;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (State != ServiceLifecycleState.Running)
            {
                Logger.LogWarning(
                    "Cannot stop service in state {State}: {ServiceAddress}",
                    State, ((IPulseService)this).ServiceAddress);
                return;
            }

            State = ServiceLifecycleState.Stopping;
            Logger.LogInformation("Stopping service: {ServiceAddress}", ((IPulseService)this).ServiceAddress);

            try
            {
                // 先停止固定帧驱动，避免在队列关闭后仍尝试投递 tick。
                await StopTickLoopAsync();

                // 停止接收新消息
                _messageQueue?.Writer.Complete();

                // 等待消息处理完成
                if (_messageProcessingTask != null)
                {
                    try
                    {
                        await _messageProcessingTask.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        Logger.LogWarning("Message processing did not complete within timeout");
                        _processingCts?.Cancel();
                    }
                }

                // 调用子类停止逻辑
                await OnStoppingAsync(cancellationToken);

                State = ServiceLifecycleState.Stopped;
                Logger.LogInformation("Service stopped: {ServiceAddress}", ((IPulseService)this).ServiceAddress);
            }
            catch (Exception ex)
            {
                State = ServiceLifecycleState.Faulted;
                Logger.LogError(ex, "Error during service stop: {ServiceAddress}", ((IPulseService)this).ServiceAddress);
                throw;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 消息队列操作
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 将工作项提交到服务队列执行
    /// </summary>
    /// <param name="work">要执行的工作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// <para>
    /// 根据服务配置的 <see cref="ServiceSchedulingMode"/> 选择执行方式：
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceSchedulingMode.DedicatedQueue"/>：排队到专属队列中顺序执行</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.ThreadAffinity"/>：通过调度器路由到固定工作线程</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.DefaultPool"/>：立即在当前线程执行</description></item>
    /// </list>
    /// <para>
    /// 此方法会自动捕获当前的 <see cref="PulseContext"/>，
    /// 并在工作项执行时恢复，确保上下文在队列处理中正确传播。
    /// </para>
    /// </remarks>
    public Task EnqueueAsync(Func<Task> work, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(work, reentrant: false, cancellationToken);

    /// <summary>
    /// 将工作项提交到服务队列执行，可选择以可重入（只读）方式执行。
    /// </summary>
    /// <param name="work">要执行的工作项</param>
    /// <param name="reentrant">
    /// 为 <see langword="true"/> 时，工作项作为读者并发执行（仅 DedicatedQueue 模式生效）；
    /// 为 <see langword="false"/> 时作为写者独占执行。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task EnqueueAsync(Func<Task> work, bool reentrant, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(work, reentrant, cancellationToken);

    /// <summary>
    /// 以可重入（只读）方式将工作项提交到服务队列执行。
    /// </summary>
    /// <remarks>
    /// 在 DedicatedQueue（Actor）模式下，读者之间可并发执行，但绝不会与写者重叠。
    /// 其它调度模式下与 <see cref="EnqueueAsync(Func{Task}, CancellationToken)"/> 行为一致。
    /// </remarks>
    public Task EnqueueReadAsync(Func<Task> work, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(work, reentrant: true, cancellationToken);

    private async Task EnqueueCoreAsync(Func<Task> work, bool reentrant, CancellationToken cancellationToken)
    {
        if (State != ServiceLifecycleState.Running)
        {
            throw new InvalidOperationException($"Service is not running: {((IPulseService)this).ServiceAddress}");
        }

        // 更新访问时间（用于空闲清理）
        UpdateAccessTime();

        if (_messageQueue != null)
        {
            // DedicatedQueue 模式：使用专属队列
            var capturedContext = PulseContext.Current;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Func<Task> wrapped = async () =>
            {
                try
                {
                    if (capturedContext != null)
                    {
                        using var _ = PulseContext.SetContext(capturedContext);
                        await work();
                    }
                    else
                    {
                        await work();
                    }

                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                    throw;
                }
            };

            await _messageQueue.Writer.WriteAsync(
                new WorkItem(wrapped, reentrant, exception => completion.TrySetException(exception)),
                cancellationToken);
            await completion.Task;
        }
        else if (_affinityScheduler != null && ExecutionOptions.SchedulingMode == ServiceSchedulingMode.ThreadAffinity)
        {
            // ThreadAffinity 模式：使用共享调度器路由到固定工作线程（可重入标志不适用，按串行处理）
            var key = new ServiceSchedulingKey(ServiceType, ServiceId);
            var capturedContext = PulseContext.Current;

            await _affinityScheduler.ScheduleAsync(key, async () =>
            {
                if (capturedContext != null)
                {
                    using var _ = PulseContext.SetContext(capturedContext);
                    await work();
                }
                else
                {
                    await work();
                }
            }, cancellationToken);
        }
        else
        {
            // DefaultPool 模式或 ThreadAffinity fallback：直接执行
            await work();
        }
    }

    /// <summary>
    /// 将工作项提交到服务队列执行并返回结果
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>性能优化</strong>：此方法使用池化的 <see cref="PooledValueTaskSource{TResult}"/>
    /// 来减少内存分配，相比传统的 <see cref="TaskCompletionSource{TResult}"/>，
    /// 在高频调用场景下可以显著降低 GC 压力。
    /// </para>
    /// <para>
    /// 根据服务配置的 <see cref="ServiceSchedulingMode"/> 选择执行方式：
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="ServiceSchedulingMode.DedicatedQueue"/>：排队到专属队列中顺序执行</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.ThreadAffinity"/>：通过调度器路由到固定工作线程</description></item>
    /// <item><description><see cref="ServiceSchedulingMode.DefaultPool"/>：立即在当前线程执行</description></item>
    /// </list>
    /// </remarks>
    public Task<TResult> EnqueueAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(work, reentrant: false, cancellationToken);

    /// <summary>
    /// 将工作项提交到服务队列执行并返回结果，可选择以可重入（只读）方式执行。
    /// </summary>
    /// <param name="work">要执行的工作项</param>
    /// <param name="reentrant">
    /// 为 <see langword="true"/> 时，工作项作为读者并发执行（仅 DedicatedQueue 模式生效）；
    /// 为 <see langword="false"/> 时作为写者独占执行。
    /// </param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task<TResult> EnqueueAsync<TResult>(Func<Task<TResult>> work, bool reentrant, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(work, reentrant, cancellationToken);

    /// <summary>
    /// 以可重入（只读）方式将工作项提交到服务队列执行并返回结果。
    /// </summary>
    /// <remarks>
    /// 在 DedicatedQueue（Actor）模式下，读者之间可并发执行，但绝不会与写者重叠。
    /// 其它调度模式下与 <see cref="EnqueueAsync{TResult}(Func{Task{TResult}}, CancellationToken)"/> 行为一致。
    /// </remarks>
    public Task<TResult> EnqueueReadAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
        => EnqueueCoreAsync(work, reentrant: true, cancellationToken);

    private async Task<TResult> EnqueueCoreAsync<TResult>(Func<Task<TResult>> work, bool reentrant, CancellationToken cancellationToken)
    {
        if (State != ServiceLifecycleState.Running)
        {
            throw new InvalidOperationException($"Service is not running: {((IPulseService)this).ServiceAddress}");
        }

        // 更新访问时间
        UpdateAccessTime();

        if (_messageQueue != null)
        {
            // DedicatedQueue 模式：使用专属队列
            var capturedContext = PulseContext.Current;
            var valueTaskSource = PooledValueTaskSource<TResult>.Create();

            Func<Task> wrapped = async () =>
            {
                try
                {
                    TResult result;
                    if (capturedContext != null)
                    {
                        using var _ = PulseContext.SetContext(capturedContext);
                        result = await work();
                    }
                    else
                    {
                        result = await work();
                    }
                    valueTaskSource.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    valueTaskSource.TrySetException(ex);
                }
            };

            await _messageQueue.Writer.WriteAsync(
                new WorkItem(wrapped, reentrant, exception => valueTaskSource.TrySetException(exception)),
                cancellationToken);

            return await valueTaskSource.GetValueTask();
        }
        else if (_affinityScheduler != null && ExecutionOptions.SchedulingMode == ServiceSchedulingMode.ThreadAffinity)
        {
            // ThreadAffinity 模式：使用共享调度器路由到固定工作线程（可重入标志不适用，按串行处理）
            var key = new ServiceSchedulingKey(ServiceType, ServiceId);
            var capturedContext = PulseContext.Current;

            return await _affinityScheduler.ScheduleAsync(key, async () =>
            {
                if (capturedContext != null)
                {
                    using var _ = PulseContext.SetContext(capturedContext);
                    return await work();
                }
                else
                {
                    return await work();
                }
            }, cancellationToken);
        }
        else
        {
            // DefaultPool 模式或 ThreadAffinity fallback：直接执行
            return await work();
        }
    }

    /// <inheritdoc/>
    public virtual Task OnStartingAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public virtual Task OnStoppingAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// 固定帧驱动回调 - 当服务类标注了 <see cref="TickAttribute"/>（<c>[Tick(hz)]</c>）时，
    /// 框架会以指定频率周期性地在服务的串行邮箱内调用此方法。
    /// </summary>
    /// <param name="cancellationToken">随服务停止而取消的令牌。</param>
    /// <returns>表示本次帧处理的异步任务。</returns>
    /// <remarks>
    /// <para>
    /// 默认实现为空操作。子类可覆写以实现每帧逻辑（如世界状态推进、定时聚合等）。
    /// </para>
    /// <para>
    /// 此回调经由 <see cref="EnqueueAsync(Func{Task}, CancellationToken)"/> 投递到邮箱，
    /// 因此在 <see cref="ServiceSchedulingMode.DedicatedQueue"/> /
    /// <see cref="ServiceSchedulingMode.ThreadAffinity"/> 模式下与其它消息处理串行执行，无需加锁。
    /// </para>
    /// <para>
    /// 回调执行时的 <see cref="PulseContext"/> 调用来源为 <c>CallSourceType.SystemTimer</c>，
    /// 因此天然接入既有权限绕过设计（<c>AllowSystem</c>），无需为定时逻辑单独放行。
    /// </para>
    /// </remarks>
    protected virtual Task OnTickAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// 读取类上的 <see cref="TickAttribute"/> 并启动固定帧驱动循环（若已标注）。
    /// </summary>
    private void StartTickLoop()
    {
        var tick = GetType().GetCustomAttribute<TickAttribute>(inherit: true);
        if (tick is null)
        {
            return;
        }

        _tickCts = new CancellationTokenSource();
        _tickLoopTask = TickLoopAsync(tick.Interval, _tickCts.Token);
        Logger.LogInformation(
            "Tick loop started at {Hz}Hz (interval {Interval}) for {ServiceAddress}",
            tick.Hz, tick.Interval, ((IPulseService)this).ServiceAddress);
    }

    /// <summary>
    /// 停止固定帧驱动循环并等待其结束。
    /// </summary>
    private async Task StopTickLoopAsync()
    {
        if (_tickCts is null)
        {
            return;
        }

        _tickCts.Cancel();

        if (_tickLoopTask is not null)
        {
            try
            {
                await _tickLoopTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                Logger.LogWarning("Tick loop did not stop within timeout for {ServiceAddress}", ((IPulseService)this).ServiceAddress);
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
        }

        _tickCts.Dispose();
        _tickCts = null;
        _tickLoopTask = null;
    }

    /// <summary>
    /// 固定帧驱动主循环：按固定间隔把 <see cref="OnTickAsync"/> 投递到串行邮箱。
    /// </summary>
    private async Task TickLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        // 复用同一个「系统定时器」上下文与 tick 委托，避免高频 tick 下每帧新建 context/闭包
        // 造成的分配。该上下文为不可变 record、跨帧共享安全（_cts 为 null，Dispose 为空操作，
        // 且邮箱路径从不释放 context 对象）。唯一随帧变化的 StartTimestamp 仅供
        // IPulseContext.GetElapsedTime() 使用，而该方法当前无生产消费，故固定为「开始 tick 的
        // 时刻」可接受。
        var systemContext = PulseContextData.CreateSystemContext($"Tick:{ServiceType}");
        Func<Task> tickWork = () => OnTickAsync(cancellationToken);

        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // 仅在运行态投递；停止过程中的漏帧直接跳过，等待下一次取消退出。
                if (State != ServiceLifecycleState.Running)
                {
                    continue;
                }

                try
                {
                    // 每次 tick 作为一次「系统定时器」调用：绑定 SystemTimer 来源上下文，
                    // 接入既有权限设计（RequirePermission/RequireRole 的 AllowSystem 绕过、
                    // ClientFacingGate 对非 ExternalUser 放行）。上下文在入队时被捕获，
                    // 并在邮箱内执行 OnTickAsync 时恢复，三种调度模式均适用。
                    using (PulseContext.SetContext(systemContext))
                    {
                        await EnqueueAsync(tickWork, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    // 服务正在停止（邮箱已不接受新工作项），忽略本次 tick。
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Tick handler failed for {ServiceAddress}", ((IPulseService)this).ServiceAddress);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
    }

    /// <inheritdoc/>
    public virtual Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var isHealthy = State == ServiceLifecycleState.Running;
        return Task.FromResult(isHealthy
            ? ServiceHealthCheckResult.Healthy()
            : ServiceHealthCheckResult.Unhealthy($"Service state: {State}"));
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        if (State == ServiceLifecycleState.Running)
        {
            await StopAsync();
        }

        _tickCts?.Dispose();
        _processingCts?.Dispose();
        _stateLock.Dispose();

        GC.SuppressFinalize(this);
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        if (_messageQueue == null) return;

        Logger.LogDebug("Message processing started for {ServiceAddress}", ((IPulseService)this).ServiceAddress);

        // 在途读者（可重入方法）集合：读者之间并发执行，但绝不与写者重叠。
        var inFlightReaders = new List<Task>();

        try
        {
            await foreach (var item in _messageQueue.Reader.ReadAllAsync(cancellationToken))
            {
                if (item.Reentrant)
                {
                    // 读者：并发派发，不等待其完成即可继续读取下一项。
                    PruneCompleted(inFlightReaders);
                    inFlightReaders.Add(RunWorkSafeAsync(item.Work));
                }
                else
                {
                    // 写者：先排空所有在途读者，再独占执行，确保读写不重叠且写者按 FIFO 顺序执行。
                    await DrainReadersAsync(inFlightReaders);
                    await RunWorkSafeAsync(item.Work);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Message processing failed for {ServiceAddress}", ((IPulseService)this).ServiceAddress);
        }
        finally
        {
            // 停止/取消时排空剩余在途读者，避免遗留悬空任务。
            try
            {
                await DrainReadersAsync(inFlightReaders);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error draining in-flight readers for {ServiceAddress}", ((IPulseService)this).ServiceAddress);
            }
        }

        Logger.LogDebug("Message processing stopped for {ServiceAddress}", ((IPulseService)this).ServiceAddress);
    }

    /// <summary>
    /// 执行工作项并吞掉异常（记录日志），使其可安全地用于 <see cref="Task.WhenAll(IEnumerable{Task})"/>。
    /// </summary>
    private async Task RunWorkSafeAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing message in {ServiceAddress}", ((IPulseService)this).ServiceAddress);
        }
    }

    /// <summary>
    /// 移除已完成的在途读者任务，防止列表无限增长。
    /// </summary>
    private static void PruneCompleted(List<Task> tasks)
    {
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            if (tasks[i].IsCompleted)
            {
                tasks.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 等待所有在途读者完成并清空集合。
    /// </summary>
    private static async Task DrainReadersAsync(List<Task> tasks)
    {
        if (tasks.Count == 0) return;
        await Task.WhenAll(tasks);
        tasks.Clear();
    }

    /// <summary>
    /// 邮箱工作项，携带可重入（只读）标志。
    /// </summary>
    private readonly struct WorkItem
    {
        /// <summary>要执行的工作。</summary>
        public readonly Func<Task> Work;

        /// <summary>是否为可重入（只读）工作项，可与其它读者并发执行。</summary>
        public readonly bool Reentrant;

        private readonly Action<Exception> _reject;

        public WorkItem(Func<Task> work, bool reentrant, Action<Exception> reject)
        {
            Work = work;
            Reentrant = reentrant;
            _reject = reject;
        }

        public void Reject(Exception exception) => _reject(exception);
    }
}

/// <summary>
/// 服务配置（已废弃）
/// </summary>
/// <remarks>
/// <para>
/// <strong>此类已废弃</strong>，请使用 <see cref="ServiceExecutionOptions"/> 替代。
/// </para>
/// <para>
/// 迁移指南：
/// </para>
/// <list type="bullet">
/// <item><description>使用 <see cref="ServiceExecutionOptions.FromScenario"/> 从场景创建配置</description></item>
/// <item><description>使用 <see cref="ServiceExecutionOptions.FromAttribute"/> 从 Attribute 创建配置</description></item>
/// <item><description>使用预定义配置如 <see cref="ServiceExecutionOptions.Actor"/>、<see cref="ServiceExecutionOptions.StatefulIO"/> 等</description></item>
/// </list>
/// </remarks>
[Obsolete("使用 ServiceExecutionOptions 替代。此类将在未来版本中移除。")]
public sealed class ServiceConfiguration
{
    /// <summary>
    /// 调度模式
    /// </summary>
    public ServiceSchedulingMode SchedulingMode { get; init; } = ServiceSchedulingMode.DedicatedQueue;

    /// <summary>
    /// 队列容量
    /// </summary>
    public int QueueCapacity { get; init; } = 1000;

    /// <summary>
    /// 背压模式
    /// </summary>
    public ServiceBackpressureMode BackpressureMode { get; init; } = ServiceBackpressureMode.Block;

    /// <summary>
    /// 默认配置
    /// </summary>
    public static ServiceConfiguration Default { get; } = new();

    /// <summary>
    /// 从 Attribute 创建配置
    /// </summary>
    [Obsolete("使用 ServiceExecutionOptions.FromAttribute 替代")]
    public static ServiceConfiguration FromAttribute(PulseServiceAttribute? attribute)
    {
        if (attribute == null) return Default;

        return new ServiceConfiguration
        {
            SchedulingMode = attribute.SchedulingMode,
            QueueCapacity = attribute.QueueCapacity,
            BackpressureMode = attribute.BackpressureMode
        };
    }

    /// <summary>
    /// 转换为 ServiceExecutionOptions
    /// </summary>
    public ServiceExecutionOptions ToExecutionOptions()
    {
        return new ServiceExecutionOptions
        {
            SchedulingMode = SchedulingMode,
            QueueCapacity = QueueCapacity,
            BackpressureMode = BackpressureMode,
            MaxConcurrency = 1,
            EnableYielding = false
        };
    }
}
