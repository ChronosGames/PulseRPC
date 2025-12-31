using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Scheduling;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Scheduling;
using PulseRPC.Transport;

namespace PulseRPC.Server.Services;

/// <summary>
/// 统一的 PulseService 基类实现
/// </summary>
/// <remarks>
/// <para>
/// 提供 <see cref="IUnifiedPulseService"/> 的默认实现，包括：
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
/// public class ChatRoomService : UnifiedPulseServiceBase, IChatRoomHub
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
public abstract class UnifiedPulseServiceBase : IUnifiedPulseService, IUnifiedServiceLifecycle, IUnifiedServiceHealthCheck
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Channel<Func<Task>>? _messageQueue;
    private readonly IThreadAffinityScheduler? _affinityScheduler;
    private Task? _messageProcessingTask;
    private CancellationTokenSource? _processingCts;

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
    /// 创建新的 UnifiedPulseServiceBase 实例
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <param name="serviceId">服务实例 ID</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="executionOptions">执行配置（可选，推荐使用 <see cref="ServiceExecutionOptions"/> 的预定义配置）</param>
    /// <param name="affinityScheduler">线程亲和性调度器（可选，仅 ThreadAffinity 模式需要）</param>
    /// <example>
    /// <code>
    /// // 使用预定义配置
    /// public class PlayerService : UnifiedPulseServiceBase
    /// {
    ///     public PlayerService(string playerId, ILogger&lt;PlayerService&gt; logger)
    ///         : base("Player", playerId, logger, ServiceExecutionOptions.StatefulIO)
    ///     {
    ///     }
    /// }
    ///
    /// // 使用场景枚举
    /// public class ChatRoomService : UnifiedPulseServiceBase
    /// {
    ///     public ChatRoomService(string roomId, ILogger&lt;ChatRoomService&gt; logger)
    ///         : base("ChatRoom", roomId, logger, ServiceExecutionOptions.FromScenario(ServiceScenario.Actor))
    ///     {
    ///     }
    /// }
    /// </code>
    /// </example>
    protected UnifiedPulseServiceBase(
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
            _messageQueue = Channel.CreateBounded<Func<Task>>(
                new BoundedChannelOptions(ExecutionOptions.QueueCapacity)
                {
                    FullMode = ExecutionOptions.BackpressureMode switch
                    {
                        ServiceBackpressureMode.Block => BoundedChannelFullMode.Wait,
                        ServiceBackpressureMode.DropNewest => BoundedChannelFullMode.DropNewest,
                        ServiceBackpressureMode.DropOldest => BoundedChannelFullMode.DropOldest,
                        _ => BoundedChannelFullMode.Wait
                    },
                    SingleReader = true,
                    SingleWriter = false
                });
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
                    State, ((IUnifiedPulseService)this).ServiceAddress);
                return;
            }

            State = ServiceLifecycleState.Starting;
            Logger.LogInformation("Starting service: {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);

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
                Logger.LogInformation("Service started: {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
            }
            catch (Exception ex)
            {
                State = ServiceLifecycleState.Faulted;
                Logger.LogError(ex, "Failed to start service: {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
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
                    State, ((IUnifiedPulseService)this).ServiceAddress);
                return;
            }

            State = ServiceLifecycleState.Stopping;
            Logger.LogInformation("Stopping service: {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);

            try
            {
                // 停止接收新消息
                _messageQueue?.Writer.Complete();

                // 等待消息处理完成
                if (_messageProcessingTask != null)
                {
                    _processingCts?.Cancel();
                    try
                    {
                        await _messageProcessingTask.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        Logger.LogWarning("Message processing did not complete within timeout");
                    }
                }

                // 调用子类停止逻辑
                await OnStoppingAsync(cancellationToken);

                State = ServiceLifecycleState.Stopped;
                Logger.LogInformation("Service stopped: {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
            }
            catch (Exception ex)
            {
                State = ServiceLifecycleState.Faulted;
                Logger.LogError(ex, "Error during service stop: {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
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
    public async Task EnqueueAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        if (State != ServiceLifecycleState.Running)
        {
            throw new InvalidOperationException($"Service is not running: {((IUnifiedPulseService)this).ServiceAddress}");
        }

        // 更新访问时间（用于空闲清理）
        UpdateAccessTime();

        if (_messageQueue != null)
        {
            // DedicatedQueue 模式：使用专属队列
            var capturedContext = PulseContext.Current;

            await _messageQueue.Writer.WriteAsync(async () =>
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
        else if (_affinityScheduler != null && ExecutionOptions.SchedulingMode == ServiceSchedulingMode.ThreadAffinity)
        {
            // ThreadAffinity 模式：使用共享调度器路由到固定工作线程
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
    public async Task<TResult> EnqueueAsync<TResult>(Func<Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        if (State != ServiceLifecycleState.Running)
        {
            throw new InvalidOperationException($"Service is not running: {((IUnifiedPulseService)this).ServiceAddress}");
        }

        // 更新访问时间
        UpdateAccessTime();

        if (_messageQueue != null)
        {
            // DedicatedQueue 模式：使用专属队列
            var capturedContext = PulseContext.Current;
            var valueTaskSource = PooledValueTaskSource<TResult>.Create();

            await _messageQueue.Writer.WriteAsync(async () =>
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
            }, cancellationToken);

            return await valueTaskSource.GetValueTask();
        }
        else if (_affinityScheduler != null && ExecutionOptions.SchedulingMode == ServiceSchedulingMode.ThreadAffinity)
        {
            // ThreadAffinity 模式：使用共享调度器路由到固定工作线程
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

        _processingCts?.Dispose();
        _stateLock.Dispose();

        GC.SuppressFinalize(this);
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        if (_messageQueue == null) return;

        Logger.LogDebug("Message processing started for {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);

        try
        {
            await foreach (var work in _messageQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error processing message in {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Message processing failed for {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
        }

        Logger.LogDebug("Message processing stopped for {ServiceAddress}", ((IUnifiedPulseService)this).ServiceAddress);
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

