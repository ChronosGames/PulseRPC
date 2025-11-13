using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

// 类型别名 - 服务间认证上下文
using AuthenticationContext = PulseRPC.Server.ServiceAuthenticationContext;
using AuthenticationContextProvider = PulseRPC.Server.ServiceAuthenticationContextProvider;

namespace PulseRPC.Server;

// ========================
// 4. 服务接口和基类
// ========================

/// <summary>
/// 服务基础接口
/// </summary>
public interface IService : IAsyncDisposable
{
    /// <summary>服务PID</summary>
    PID ServicePID { get; }

    /// <summary>启动服务</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>停止服务</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

// ========================
// 8. 带认证的BaseService
// ========================

/// <summary>
/// 带认证的Actor服务基类
/// </summary>
/// <remarks>
/// 支持灵活配置：
/// - 单线程/并发处理
/// - 优先级队列
/// - 背压流控
///
/// 默认配置：单线程 Actor 模型（严格有序）
/// </remarks>
public abstract class BaseService : IService
{
    // MethodInfo 缓存 - 用于优化反射性能
    // TODO: 协议号到方法的映射将由 SourceGenerator 生成
    private static readonly ConcurrentDictionary<(Type ServiceType, PulseRPC.Protocol.ProtocolId ProtocolId), System.Reflection.MethodInfo?> _methodInfoCache = new();

    public PID ServicePID { get; private set; }

    protected readonly ILogger Logger;
    protected readonly ServiceQueueOptions Options;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private bool _isRunning;
    private AuthenticatedServiceMessageQueue? _messageQueue;
    private readonly IAuthenticationService _authenticationService;
    private readonly PermissionValidator _permissionValidator;
    private string? _serviceSecret;

    protected BaseService(
        ILogger logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator,
        ServiceQueueOptions? options = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authenticationService = authenticationService;
        _permissionValidator = permissionValidator;
        Options = options ?? ServiceQueueOptions.Default;
        Options.Validate();
    }

    internal void SetPID(PID pid)
    {
        ServicePID = pid;

        // 生成服务密钥
        _serviceSecret = _authenticationService.GenerateServiceSecret(pid);

        // 根据配置创建消息队列（支持并发、优先级、背压策略）
        _messageQueue = new AuthenticatedServiceMessageQueue(
            GetType().Name,
            pid,
            GetType(),
            Logger,
            _permissionValidator,
            capacity: Options.QueueCapacity,
            maxConcurrency: Options.MaxConcurrency,
            backpressureStrategy: Options.BackpressureStrategy);

        // 启动消息处理循环
        _messageQueue.Start(ProcessMessageAsync);

        Logger.LogInformation(
            "Service initialized - Service: {ServiceName}, PID: {PID}, MaxConcurrency: {MaxConcurrency}, BackpressureStrategy: {Strategy}",
            GetType().Name, pid, Options.MaxConcurrency, Options.BackpressureStrategy);
    }

    // 启动/停止方法（省略）
    public virtual Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// RPC调用 - 自动附加认证上下文
    /// </summary>
    public virtual Task InvokeAsync(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken = default)
    {
        if (_messageQueue == null)
            throw new InvalidOperationException("Service is not properly initialized");

        // 获取当前认证上下文或创建服务上下文
        var authContext = AuthenticationContextProvider.Current
            ?? AuthenticationContext.CreateServiceContext(ServicePID, _serviceSecret!);

        return _messageQueue.SendMethodInvocationAsync(protocolId, args, authContext, cancellationToken);
    }

    public virtual Task<TResult> InvokeAsync<TResult>(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken = default)
    {
        if (_messageQueue == null)
            throw new InvalidOperationException("Service is not properly initialized");

        var authContext = AuthenticationContextProvider.Current
            ?? AuthenticationContext.CreateServiceContext(ServicePID, _serviceSecret!);

        return _messageQueue.SendMethodInvocationAsync<TResult>(protocolId, args, authContext, cancellationToken);
    }

    /// <summary>
    /// 获取当前调用者信息
    /// </summary>
    protected AuthenticationContext GetCurrentCaller()
    {
        return AuthenticationContextProvider.RequireCurrent();
    }

    /// <summary>
    /// 检查当前调用者是否有权限
    /// </summary>
    protected bool HasPermission(string permission)
    {
        var context = AuthenticationContextProvider.Current;
        return context?.HasPermission(permission) ?? false;
    }

    /// <summary>
    /// 检查当前调用者是否有角色
    /// </summary>
    protected bool HasRole(string role)
    {
        var context = AuthenticationContextProvider.Current;
        return context?.HasRole(role) ?? false;
    }

    // 定时器方法（省略）
    protected string ScheduleOnce(TimeSpan delay, Func<Task> callback) => string.Empty;
    protected string ScheduleRecurring(TimeSpan initialDelay, TimeSpan interval, Func<Task> callback) => string.Empty;
    protected bool CancelTimer(string timerId) => false;

    /// <summary>
    /// 获取队列监控指标快照
    /// </summary>
    public ServiceQueueMetricsSnapshot? GetMetricsSnapshot()
    {
        return _messageQueue?.GetMetricsSnapshot();
    }

    /// <summary>
    /// 获取当前队列深度
    /// </summary>
    public int GetCurrentQueueDepth()
    {
        return _messageQueue?.GetCurrentQueueDepth() ?? 0;
    }


    // public virtual async Task BroadcastAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    // {
    //     // 广播到所有订阅者
    //     var receivers = ServiceLocator.Instance.GetAllReceivers();
    //     var tasks = receivers.Select(r => r.OnBroadcastAsync(method, args, cancellationToken));
    //     await Task.WhenAll(tasks);
    // }

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
                    "Unknown message type - Actor: {PID}, Type: {Type}",
                    ServicePID, message.GetType().Name);
                break;
        }
    }

    protected virtual async Task ProcessMethodInvocationAsync(MethodInvocationMessage message)
    {
        try
        {
            message.CancellationToken.ThrowIfCancellationRequested();

            // 从缓存获取 MethodInfo（使用 SourceGenerator 生成的映射表）
            var serviceType = GetType();
            var methodInfo = _methodInfoCache.GetOrAdd(
                (serviceType, message.ProtocolId),
                key => PulseRPC.Generated.ProtocolIdMapping.GetMethod(key.ServiceType, key.ProtocolId));

            if (methodInfo == null)
            {
                throw new InvalidOperationException(
                    $"Method with ProtocolId '{message.ProtocolId}' (0x{message.ProtocolId.Value:X4}) not found on service {ServicePID}");
            }

            // ✅ 使用表达式树编译调用（性能提升 ~50 倍）
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
    }

    private async Task ProcessTimerMessageAsync(TimerMessage message)
    {
        try
        {
            Logger.LogTrace(
                "Executing timer callback - Actor: {PID}, TimerId: {TimerId}",
                ServicePID, message.TimerId);

            await message.Callback();

            // 如果是一次性定时器，自动取消
            if (!message.IsRecurring)
            {
                CancelTimer(message.TimerId);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Timer callback failed - Actor: {PID}, TimerId: {TimerId}",
                ServicePID, message.TimerId);
        }
    }

    private Task ProcessSystemMessageAsync(SystemMessage message)
    {
        Logger.LogDebug(
            "Processing system message - Actor: {PID}, Command: {Command}",
            ServicePID, message.Command);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 通过编译后的表达式树调用方法（已优化）
    /// </summary>
    private async Task InvokeMethodAsync(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken)
    {
        var serviceType = GetType();
        var methodInfo = _methodInfoCache.GetOrAdd(
            (serviceType, protocolId),
            key => PulseRPC.Generated.ProtocolIdMapping.GetMethod(key.ServiceType, key.ProtocolId));

        if (methodInfo == null)
            throw new InvalidOperationException($"Method with ProtocolId '{protocolId}' (0x{protocolId.Value:X4}) not found on service {ServicePID}");

        // ✅ 使用表达式树编译调用
        await CompiledAsyncMethodInvoker.InvokeAsync(this, methodInfo, args);
    }

    private async Task<TResult> InvokeMethodAsync<TResult>(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken)
    {
        var serviceType = GetType();
        var methodInfo = _methodInfoCache.GetOrAdd(
            (serviceType, protocolId),
            key => PulseRPC.Generated.ProtocolIdMapping.GetMethod(key.ServiceType, key.ProtocolId));

        if (methodInfo == null)
            throw new InvalidOperationException($"Method with ProtocolId '{protocolId}' (0x{protocolId.Value:X4}) not found on service {ServicePID}");

        // ✅ 使用表达式树编译调用
        var result = await CompiledAsyncMethodInvoker.InvokeAsync<TResult>(this, methodInfo, args);

        // 处理 null 返回值
        if (result == null && typeof(TResult).IsValueType)
            return default!;

        return result!;
    }

    public virtual async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stateLock.Dispose();
        GC.SuppressFinalize(this);
    }
}


// ========================
// 5. 对象池实现
// ========================

/// <summary>
/// 服务对象池
/// </summary>
public class ServiceObjectPool<TService> where TService : BaseService
{
    private readonly ConcurrentBag<TService> _pool = new();
    private readonly Func<TService> _serviceFactory;
    private readonly int _maxSize;
    private int _currentSize;
    private readonly ILogger _logger;

    public ServiceObjectPool(Func<TService> serviceFactory, int maxSize, ILogger logger)
    {
        _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        _maxSize = maxSize > 0 ? maxSize : 100;
        _logger = logger;
    }

    public TService Rent()
    {
        if (_pool.TryTake(out var service))
        {
            _logger.LogDebug("Rented service from pool, remaining: {Count}", _pool.Count);
            return service;
        }

        if (Interlocked.Increment(ref _currentSize) <= _maxSize)
        {
            var newService = _serviceFactory();
            _logger.LogDebug("Created new pooled service, total: {Count}", _currentSize);
            return newService;
        }

        Interlocked.Decrement(ref _currentSize);

        // 等待可用实例
        SpinWait.SpinUntil(() => _pool.TryTake(out service), TimeSpan.FromSeconds(5));

        return service ?? throw new InvalidOperationException("Pool exhausted and timeout reached");
    }

    public void Return(TService service)
    {
        if (service == null) return;

        _pool.Add(service);
        _logger.LogDebug("Returned service to pool, available: {Count}", _pool.Count);
    }

    public async ValueTask DisposeAsync()
    {
        while (_pool.TryTake(out var service))
        {
            await service.DisposeAsync();
        }
        _currentSize = 0;
    }
}
