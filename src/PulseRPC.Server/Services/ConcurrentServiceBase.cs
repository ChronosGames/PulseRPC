using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;

// 类型别名 - 服务间认证上下文
// 使用统一的请求上下文

namespace PulseRPC.Server;

/// <summary>
/// [已废弃] 并发服务基类 - 支持并发处理多个消息
/// </summary>
/// <remarks>
/// <para><strong>⚠️ 此类已废弃</strong>，请使用 <see cref="PulseRPC.Server.Services.UnifiedPulseServiceBase"/> + SchedulingMode.DefaultPool 替代。</para>
/// <para>
/// 与 <see cref="BaseService"/> 的区别：
/// - BaseService: 单线程串行处理（Actor 模型），保证消息有序性
/// - ConcurrentServiceBase: 多线程并发处理，适合 IO 密集型场景
/// </para>
/// <para>
/// 使用场景：
/// - 数据库查询服务
/// - HTTP API 网关
/// - 无状态的纯查询操作
/// - IO 密集型操作
/// </para>
/// <para>
/// 注意事项：
/// ⚠️ Service 内部状态需要线程安全保护（使用锁或 Interlocked）
/// ⚠️ 消息处理顺序不保证（即使同优先级）
/// ⚠️ 不适合有状态依赖的业务逻辑
/// </para>
/// </remarks>
[Obsolete("使用 UnifiedPulseServiceBase + SchedulingMode.DefaultPool 替代。")]
public abstract class ConcurrentServiceBase : IService, IPulseHub
{
    public PID ServicePID { get; private set; }

    protected readonly ILogger Logger;
    protected readonly ConcurrentServiceOptions Options;

    private AuthenticatedServiceMessageQueue? _messageQueue;
    private readonly IAuthenticationService _authenticationService;
    private readonly PermissionValidator _permissionValidator;
    private string? _serviceSecret;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="authenticationService">认证服务</param>
    /// <param name="permissionValidator">权限验证器</param>
    /// <param name="options">并发配置选项（可选，默认为 4 并发度）</param>
    protected ConcurrentServiceBase(
        ILogger logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator,
        ConcurrentServiceOptions? options = null)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _permissionValidator = permissionValidator ?? throw new ArgumentNullException(nameof(permissionValidator));

        Options = options ?? ConcurrentServiceOptions.Default;
        Options.Validate(); // 验证配置有效性
    }

    /// <summary>
    /// 设置服务 PID（内部方法，由 ServiceLocator 调用）
    /// </summary>
    internal void SetPID(PID pid)
    {
        ServicePID = pid;

        // 生成服务密钥
        _serviceSecret = _authenticationService.GenerateServiceSecret(pid);

        // 创建支持并发的消息队列
        _messageQueue = new AuthenticatedServiceMessageQueue(
            GetType().Name,
            pid,
            GetType(),
            Logger,
            _permissionValidator,
            capacity: Options.QueueCapacity,
            maxConcurrency: Options.MaxConcurrency, // ✅ 传递并发度
            backpressureStrategy: Options.BackpressureStrategy); // ✅ 传递背压策略

        // 启动消息处理循环
        _messageQueue.Start(ProcessMessageAsync);

        Logger.LogInformation(
            "Concurrent service initialized - Service: {ServiceName}, PID: {PID}, MaxConcurrency: {MaxConcurrency}, BackpressureStrategy: {Strategy}",
            GetType().Name, pid, Options.MaxConcurrency, Options.BackpressureStrategy);
    }

    /// <summary>
    /// RPC 调用 - 自动附加认证上下文
    /// </summary>
    public virtual Task InvokeAsync(PulseRPC.Protocol.ProtocolId protocolId, object?[] args, CancellationToken cancellationToken = default)
    {
        if (_messageQueue == null)
            throw new InvalidOperationException("Service is not properly initialized. SetPID must be called first.");

        // 获取当前认证上下文或创建服务上下文
        var authContext = ServiceRequestContextProvider.Current
            ?? ServiceRequestContext.CreateServiceContext(ServicePID, _serviceSecret!);

        return _messageQueue.SendMethodInvocationAsync(protocolId, args, authContext, cancellationToken);
    }

    /// <summary>
    /// 获取当前调用者信息
    /// </summary>
    protected IServiceRequestContext GetCurrentCaller()
    {
        return ServiceRequestContextProvider.RequireCurrent();
    }

    /// <summary>
    /// 检查权限
    /// </summary>
    protected bool HasPermission(string permission)
    {
        return ServiceRequestContextProvider.Current?.HasPermission(permission) ?? false;
    }

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

    /// <summary>
    /// 消息处理方法（由生成器实现或手动实现）
    /// </summary>
    /// <remarks>
    /// 此方法会并发执行，需要确保线程安全
    /// </remarks>
    protected virtual async Task ProcessMessageAsync(ServiceMessage message)
    {
        if (message is MethodInvocationMessage methodMsg)
        {
            // 调用生成的方法调度器或手动实现的路由逻辑
            await ProcessMethodInvocationAsync(methodMsg);
        }
    }

    /// <summary>
    /// 处理方法调用（子类可覆盖）
    /// </summary>
    protected virtual async Task ProcessMethodInvocationAsync(MethodInvocationMessage message)
    {
        var serviceType = GetType();
        var methodInfo = PulseRPC.Generated.ProtocolIdMapping.GetMethod(serviceType, message.ProtocolId);

        if (methodInfo == null)
        {
            message.CompletionSource.TrySetException(
                new InvalidOperationException($"Method with ProtocolId '{message.ProtocolId}' (0x{message.ProtocolId.Value:X4}) not found"));
            return;
        }

        // 使用表达式树编译调用
        var result = await CompiledAsyncMethodInvoker.InvokeAsync(this, methodInfo, message.Arguments);
        message.CompletionSource.TrySetResult(result);
    }

    // 生命周期方法
    public virtual Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public virtual Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    protected virtual Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    protected virtual Task OnStopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual async ValueTask DisposeAsync()
    {
        if (_messageQueue != null)
        {
            await _messageQueue.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
