using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Authentication;

namespace PulseRPC.Server;

/// <summary>
/// [已废弃] 支持让出的 Service 基类
/// </summary>
/// <remarks>
/// <para><strong>⚠️ 此类已废弃</strong>，请使用 <see cref="PulseRPC.Server.Services.UnifiedPulseServiceBase"/> 替代。</para>
/// <para>新体系默认支持 async/await 并自动在队列中恢复执行。</para>
/// <para><strong>核心特性</strong>：</para>
/// <list type="bullet">
/// <item><description><strong>自动让出</strong>：await 时自动让出队列执行权</description></item>
/// <item><description><strong>自动恢复</strong>：IO 完成后自动回到队列线程</description></item>
/// <item><description><strong>线程安全</strong>：恢复后仍在队列线程，保证 Actor 模型</description></item>
/// <item><description><strong>零心智负担</strong>：开发者按正常 async/await 写代码</description></item>
/// </list>
///
/// <para><strong>使用示例</strong>：</para>
/// <code>
/// public class GameHub : YieldingService, IGameHub
/// {
///     private readonly AccountRepository _accountRepository;
///     private readonly Dictionary&lt;string, PlayerState&gt; _playerStates = new();
///
///     public async Task&lt;LoginResponse&gt; LoginAsync(LoginRequest request)
///     {
///         // ✅ await 时自动让出队列，其他消息可以处理
///         var account = await _accountRepository.GetByUserIdAsync(request.Account);
///
///         // ✅ IO 完成后自动回到队列线程，可以安全修改状态
///         _playerStates[account.UserId] = new PlayerState();
///
///         return new LoginResponse { Success = true };
///     }
/// }
/// </code>
///
/// <para><strong>与 BaseService 的区别</strong>：</para>
/// <list type="bullet">
/// <item><description>BaseService: await 阻塞队列，其他消息等待</description></item>
/// <item><description>YieldingService: await 让出队列，其他消息可以处理</description></item>
/// </list>
/// </remarks>
[Obsolete("使用 UnifiedPulseServiceBase 替代。")]
public abstract class YieldingService : IService
{
    // MethodInfo 缓存 - 用于优化反射性能
    private static readonly ConcurrentDictionary<(Type ServiceType, PulseRPC.Protocol.ProtocolId ProtocolId), System.Reflection.MethodInfo?> _methodInfoCache = new();

    public PID ServicePID { get; private set; }

    protected readonly ILogger Logger;
    private YieldingServiceMessageQueue? _messageQueue;
    private readonly IAuthenticationService _authenticationService;
    private readonly PermissionValidator _permissionValidator;
    private string? _serviceSecret;

    protected YieldingService(
        ILogger logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _permissionValidator = permissionValidator ?? throw new ArgumentNullException(nameof(permissionValidator));
    }

    internal void SetPID(PID pid)
    {
        ServicePID = pid;

        // 生成服务密钥
        _serviceSecret = _authenticationService.GenerateServiceSecret(pid);

        // 创建支持让出的消息队列
        _messageQueue = new YieldingServiceMessageQueue(
            GetType().Name,
            pid,
            Logger,
            capacity: -1); // 无界队列

        // 启动消息处理循环
        _messageQueue.Start(ProcessMessageAsync);

        Logger.LogInformation(
            "YieldingService initialized - Service: {ServiceName}, PID: {PID}",
            GetType().Name, pid);
    }

    // 生命周期方法
    public virtual Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// RPC 调用 - 自动附加认证上下文
    /// </summary>
    public virtual async Task InvokeAsync(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken = default)
    {
        if (_messageQueue == null)
            throw new InvalidOperationException("Service is not properly initialized. SetPID must be called first.");

        var message = new MethodInvocationMessage
        {
            ProtocolId = protocolId,
            Arguments = args,
            CancellationToken = cancellationToken
        };

        await _messageQueue.SendMessageAsync(message, cancellationToken);
        await message.CompletionSource.Task;
    }

    public virtual async Task<TResult> InvokeAsync<TResult>(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken = default)
    {
        if (_messageQueue == null)
            throw new InvalidOperationException("Service is not properly initialized. SetPID must be called first.");

        var message = new MethodInvocationMessage
        {
            ProtocolId = protocolId,
            Arguments = args,
            CancellationToken = cancellationToken,
            ReturnType = typeof(TResult)
        };

        await _messageQueue.SendMessageAsync(message, cancellationToken);

        // 等待结果
        var result = await message.CompletionSource.Task;
        return (TResult)result!;
    }

    /// <summary>
    /// 获取当前调用者信息
    /// </summary>
    protected IServiceRequestContext GetCurrentCaller()
    {
        return ServiceRequestContextProvider.RequireCurrent();
    }

    /// <summary>
    /// 检查当前调用者是否有权限
    /// </summary>
    protected bool HasPermission(string permission)
    {
        var context = ServiceRequestContextProvider.Current;
        return context?.HasPermission(permission) ?? false;
    }

    /// <summary>
    /// 检查当前调用者是否有角色
    /// </summary>
    protected bool HasRole(string role)
    {
        var context = ServiceRequestContextProvider.Current;
        return context?.HasRole(role) ?? false;
    }

    #region 原子操作辅助方法

    // 细粒度锁管理
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _resourceLocks = new();

    /// <summary>
    /// 执行原子操作（使用细粒度锁保证原子性，但不阻塞队列）
    /// </summary>
    /// <remarks>
    /// <para><strong>核心特性</strong>：</para>
    /// <list type="bullet">
    /// <item><description>✅ 使用锁保证原子性</description></item>
    /// <item><description>✅ await 仍会让出队列，不阻塞其他消息</description></item>
    /// <item><description>✅ 细粒度锁（按资源 ID），只锁定涉及的资源</description></item>
    /// <item><description>✅ 自动按 ID 排序加锁，避免死锁</description></item>
    /// </list>
    ///
    /// <para><strong>使用示例</strong>：</para>
    /// <code>
    /// // 转账操作 - 锁定两个玩家
    /// public async Task&lt;bool&gt; TransferGoldAsync(string from, string to, int amount)
    /// {
    ///     return await AtomicAsync(new[] { from, to }, async () =&gt;
    ///     {
    ///         var fromPlayer = await _repo.GetAsync(from);
    ///         fromPlayer.Gold -= amount;
    ///         await _repo.UpdateAsync(fromPlayer);
    ///
    ///         var toPlayer = await _repo.GetAsync(to);
    ///         toPlayer.Gold += amount;
    ///         await _repo.UpdateAsync(toPlayer);
    ///
    ///         return true;
    ///     });
    /// }
    /// </code>
    /// </remarks>
    /// <param name="resourceIds">涉及的资源 ID（如玩家 ID），会按 ID 排序加锁避免死锁</param>
    /// <param name="operation">原子操作</param>
    protected async Task<T> AtomicAsync<T>(string[] resourceIds, Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        ArgumentNullException.ThrowIfNull(operation);

        if (resourceIds.Length == 0)
            throw new ArgumentException("At least one resource ID is required", nameof(resourceIds));

        // 按 ID 排序，避免死锁
        var sortedIds = resourceIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var locks = sortedIds
            .Select(id => _resourceLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1)))
            .ToArray();

        // 依次获取锁（await 会让出队列）
        foreach (var lockObj in locks)
        {
            await lockObj.WaitAsync();
        }

        try
        {
            // 在锁保护下执行操作（await 仍会让出队列）
            return await operation();
        }
        finally
        {
            // 释放所有锁
            foreach (var lockObj in locks)
            {
                lockObj.Release();
            }
        }
    }

    /// <summary>
    /// 执行原子操作（单个资源）
    /// </summary>
    protected Task<T> AtomicAsync<T>(string resourceId, Func<Task<T>> operation)
    {
        return AtomicAsync(new[] { resourceId }, operation);
    }

    /// <summary>
    /// 执行原子操作（无返回值）
    /// </summary>
    protected Task AtomicAsync(string[] resourceIds, Func<Task> operation)
    {
        return AtomicAsync(resourceIds, async () =>
        {
            await operation();
            return 0; // Dummy return
        });
    }

    /// <summary>
    /// 执行原子操作（单个资源，无返回值）
    /// </summary>
    protected Task AtomicAsync(string resourceId, Func<Task> operation)
    {
        return AtomicAsync(new[] { resourceId }, operation);
    }

    /// <summary>
    /// 执行阻塞操作（会阻塞消息队列，谨慎使用）
    /// </summary>
    /// <remarks>
    /// <para><strong>⚠️ 严重警告</strong>：</para>
    /// <list type="bullet">
    /// <item><description>❌ 会阻塞整个消息队列</description></item>
    /// <item><description>❌ 其他消息无法处理</description></item>
    /// <item><description>❌ 严重降低吞吐量（可能降低 80x）</description></item>
    /// <item><description>⚠️ 仅在极少数场景使用</description></item>
    /// </list>
    ///
    /// <para><strong>推荐替代方案</strong>：</para>
    /// <list type="bullet">
    /// <item><description>✅ 使用 AtomicAsync（细粒度锁）</description></item>
    /// <item><description>✅ 使用数据库事务</description></item>
    /// <item><description>✅ 重构为非阻塞实现</description></item>
    /// </list>
    /// </remarks>
    [Obsolete("此方法会阻塞消息队列，严重降低性能。推荐使用 AtomicAsync 或数据库事务。")]
    protected async Task<T> BlockingAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Logger.LogWarning(
            "Using BlockingAsync will block the message queue - Service: {ServiceName}, PID: {PID}. " +
            "Consider using AtomicAsync instead.",
            GetType().Name, ServicePID);

        // 临时切换到无同步上下文（使用线程池）
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            return await operation();
        }
        finally
        {
            // 恢复原来的同步上下文
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    /// <summary>
    /// 执行阻塞操作（无返回值）
    /// </summary>
    [Obsolete("此方法会阻塞消息队列，严重降低性能。推荐使用 AtomicAsync 或数据库事务。")]
    protected Task BlockingAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return BlockingAsync(async () =>
        {
            await operation();
            return 0; // Dummy return
        });
    }

    #endregion

    /// <summary>
    /// 获取队列监控指标
    /// </summary>
    public YieldingQueueMetrics? GetMetrics()
    {
        return _messageQueue?.GetMetrics();
    }

    /// <summary>
    /// 获取当前队列深度
    /// </summary>
    public int GetCurrentQueueDepth()
    {
        return _messageQueue?.GetCurrentQueueDepth() ?? 0;
    }

    private async Task ProcessMessageAsync(ServiceMessage message)
    {
        switch (message)
        {
            case MethodInvocationMessage methodMsg:
                await ProcessMethodInvocationAsync(methodMsg);
                break;

            case TimerMessage timerMsg:
                await ProcessTimerMessageAsync(timerMsg);
                break;

            case SystemMessage sysMsg:
                await ProcessSystemMessageAsync(sysMsg);
                break;

            default:
                Logger.LogWarning(
                    "Unknown message type - Service: {ServiceName}, PID: {PID}, Type: {Type}",
                    GetType().Name, ServicePID, message.GetType().Name);
                break;
        }
    }

    protected virtual async Task ProcessMethodInvocationAsync(MethodInvocationMessage message)
    {
        try
        {
            message.CancellationToken.ThrowIfCancellationRequested();

            // ✅ 设置 RequestContext（用于方法内访问连接信息）
            RequestContext.SetCurrent(message.Sender);

            // 从缓存获取 MethodInfo
            var serviceType = GetType();
            var methodInfo = _methodInfoCache.GetOrAdd(
                (serviceType, message.ProtocolId),
                key => PulseRPC.Generated.ProtocolIdMapping.GetMethod(key.ServiceType, key.ProtocolId));

            if (methodInfo == null)
            {
                throw new InvalidOperationException(
                    $"Method with ProtocolId '{message.ProtocolId}' (0x{message.ProtocolId.Value:X4}) not found on service {ServicePID}");
            }

            // ✅ 使用表达式树编译调用（await 会自动让出队列）
            var result = await CompiledAsyncMethodInvoker.InvokeAsync(this, methodInfo, message.Arguments);
            message.CompletionSource.TrySetResult(result);
        }
        catch (Exception ex)
        {
            var actualException = ex is System.Reflection.TargetInvocationException tie
                ? tie.InnerException ?? ex
                : ex;

            message.CompletionSource.TrySetException(actualException);
            throw actualException;
        }
        finally
        {
            // ✅ 清除 RequestContext
            RequestContext.SetCurrent(null);
        }
    }

    private async Task ProcessTimerMessageAsync(TimerMessage message)
    {
        try
        {
            Logger.LogTrace(
                "Executing timer callback - Service: {ServiceName}, PID: {PID}, TimerId: {TimerId}",
                GetType().Name, ServicePID, message.TimerId);

            // ✅ 定时器回调也支持 await 让出
            await message.Callback();

            if (!message.IsRecurring)
            {
                // 一次性定时器自动取消
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Timer callback failed - Service: {ServiceName}, PID: {PID}, TimerId: {TimerId}",
                GetType().Name, ServicePID, message.TimerId);
        }
    }

    private Task ProcessSystemMessageAsync(SystemMessage message)
    {
        Logger.LogDebug(
            "Processing system message - Service: {ServiceName}, PID: {PID}, Command: {Command}",
            GetType().Name, ServicePID, message.Command);

        return Task.CompletedTask;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_messageQueue != null)
        {
            await _messageQueue.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
