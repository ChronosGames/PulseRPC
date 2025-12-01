using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Abstractions;

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
    private Task? _messageProcessingTask;
    private CancellationTokenSource? _processingCts;

    // ════════════════════════════════════════════════════════════════════════
    // 认证上下文（从 BaseService 迁移）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 当前请求的认证上下文（AsyncLocal）
    /// </summary>
    private static readonly AsyncLocal<IServiceRequestContext?> _currentContext = new();

    /// <summary>
    /// 获取当前请求的调用者信息
    /// </summary>
    /// <returns>请求上下文，如果没有则返回 null</returns>
    /// <remarks>
    /// 此方法在消息队列的处理循环中自动设置上下文。
    /// 在服务方法中可以使用此方法获取调用者信息。
    /// </remarks>
    protected IServiceRequestContext? GetCurrentContext() => _currentContext.Value;

    /// <summary>
    /// 获取当前调用者的 UserId
    /// </summary>
    protected string? CurrentUserId => _currentContext.Value?.UserId;

    /// <summary>
    /// 获取当前调用者的 CallerId
    /// </summary>
    protected string? CurrentCallerId => _currentContext.Value?.CallerId;

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
    /// 服务配置（从 Attribute 读取或默认值）
    /// </summary>
    protected ServiceConfiguration Configuration { get; }

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
    /// <param name="configuration">服务配置（可选）</param>
    protected UnifiedPulseServiceBase(
        string serviceType,
        string serviceId,
        ILogger? logger = null,
        ServiceConfiguration? configuration = null)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
            throw new ArgumentException("ServiceType cannot be null or whitespace", nameof(serviceType));
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId cannot be null or whitespace", nameof(serviceId));

        ServiceType = serviceType;
        ServiceId = serviceId;
        Logger = logger ?? NullLogger.Instance;
        Configuration = configuration ?? ServiceConfiguration.Default;

        // 根据调度模式创建消息队列
        if (Configuration.SchedulingMode == ServiceSchedulingMode.DedicatedQueue)
        {
            _messageQueue = Channel.CreateBounded<Func<Task>>(
                new BoundedChannelOptions(Configuration.QueueCapacity)
                {
                    FullMode = Configuration.BackpressureMode switch
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
    /// 如果服务配置为 <see cref="ServiceSchedulingMode.DedicatedQueue"/>，
    /// 则工作项会被排队到专属队列中顺序执行；
    /// 否则，工作项会立即在当前线程执行。
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
            await _messageQueue.Writer.WriteAsync(work, cancellationToken);
        }
        else
        {
            // 无队列模式，直接执行
            await work();
        }
    }

    /// <summary>
    /// 将工作项提交到服务队列执行（带上下文传播）
    /// </summary>
    /// <param name="work">要执行的工作</param>
    /// <param name="context">请求上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// 此方法会将请求上下文传播到工作项执行时，
    /// 使得在服务方法中可以通过 <see cref="GetCurrentContext"/> 获取调用者信息。
    /// </remarks>
    public async Task EnqueueWithContextAsync(
        Func<Task> work,
        IServiceRequestContext? context,
        CancellationToken cancellationToken = default)
    {
        if (State != ServiceLifecycleState.Running)
        {
            throw new InvalidOperationException($"Service is not running: {((IUnifiedPulseService)this).ServiceAddress}");
        }

        if (_messageQueue != null)
        {
            await _messageQueue.Writer.WriteAsync(async () =>
            {
                var previousContext = _currentContext.Value;
                try
                {
                    _currentContext.Value = context;
                    await work();
                }
                finally
                {
                    _currentContext.Value = previousContext;
                }
            }, cancellationToken);
        }
        else
        {
            var previousContext = _currentContext.Value;
            try
            {
                _currentContext.Value = context;
                await work();
            }
            finally
            {
                _currentContext.Value = previousContext;
            }
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
            // 使用池化的 ValueTaskSource 减少内存分配
            var valueTaskSource = PooledValueTaskSource<TResult>.Create();

            await _messageQueue.Writer.WriteAsync(async () =>
            {
                try
                {
                    var result = await work();
                    valueTaskSource.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    valueTaskSource.TrySetException(ex);
                }
            }, cancellationToken);

            return await valueTaskSource.GetValueTask();
        }
        else
        {
            return await work();
        }
    }

    /// <summary>
    /// 将工作项提交到服务队列执行并返回结果（带上下文传播）
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>性能优化</strong>：此方法使用池化的 <see cref="PooledValueTaskSource{TResult}"/>
    /// 来减少内存分配。
    /// </para>
    /// </remarks>
    public async Task<TResult> EnqueueWithContextAsync<TResult>(
        Func<Task<TResult>> work,
        IServiceRequestContext? context,
        CancellationToken cancellationToken = default)
    {
        if (State != ServiceLifecycleState.Running)
        {
            throw new InvalidOperationException($"Service is not running: {((IUnifiedPulseService)this).ServiceAddress}");
        }

        // 更新访问时间
        UpdateAccessTime();

        if (_messageQueue != null)
        {
            // 使用池化的 ValueTaskSource 减少内存分配
            var valueTaskSource = PooledValueTaskSource<TResult>.Create();

            await _messageQueue.Writer.WriteAsync(async () =>
            {
                var previousContext = _currentContext.Value;
                try
                {
                    _currentContext.Value = context;
                    var result = await work();
                    valueTaskSource.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    valueTaskSource.TrySetException(ex);
                }
                finally
                {
                    _currentContext.Value = previousContext;
                }
            }, cancellationToken);

            return await valueTaskSource.GetValueTask();
        }
        else
        {
            var previousContext = _currentContext.Value;
            try
            {
                _currentContext.Value = context;
                return await work();
            }
            finally
            {
                _currentContext.Value = previousContext;
            }
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
/// 服务配置
/// </summary>
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
}

