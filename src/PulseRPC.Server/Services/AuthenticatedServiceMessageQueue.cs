using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Models;
using PulseRPC.Transport;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// 类型别名 - 服务间认证上下文
// 使用统一的请求上下文

namespace PulseRPC.Server;

// 认证上下文定义已移至: src/PulseRPC.Server/Authentication/ServiceServiceRequestContext.cs

// ========================
// 2. 认证服务接口
// ========================

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthenticationService
{
    /// <summary>验证内部服务认证</summary>
    Task<ServiceRequestContext?> AuthenticateServiceAsync(PID servicePID, string serviceSecret);

    /// <summary>验证外部用户Token</summary>
    Task<ServiceRequestContext?> AuthenticateUserAsync(string token);

    /// <summary>生成服务密钥</summary>
    string GenerateServiceSecret(PID servicePID);

    /// <summary>验证服务密钥</summary>
    bool ValidateServiceSecret(PID servicePID, string secret);
}

/// <summary>
/// 授权服务接口
/// </summary>
public interface IAuthorizationService
{
    /// <summary>检查是否有权限</summary>
    Task<bool> CheckPermissionAsync(ServiceRequestContext context, string permission);

    /// <summary>获取用户权限列表</summary>
    Task<HashSet<string>> GetUserPermissionsAsync(string userId);

    /// <summary>获取用户角色列表</summary>
    Task<HashSet<string>> GetUserRolesAsync(string userId);
}

// ========================
// 3. 认证服务实现
// ========================

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;
    private readonly string _clusterSecret; // 集群共享密钥
    private readonly ConcurrentDictionary<PID, string> _serviceSecrets = new();
    private readonly IJwtTokenService _jwtTokenService;

    public AuthenticationService(
        ILogger<AuthenticationService> logger,
        IConfiguration configuration,
        IJwtTokenService jwtTokenService)
    {
        _logger = logger;
        _clusterSecret = configuration["ClusterSecret"] ?? throw new InvalidOperationException("ClusterSecret not configured");
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// 验证内部服务 - 使用共享密钥或证书
    /// </summary>
    public async Task<ServiceRequestContext?> AuthenticateServiceAsync(PID servicePID, string serviceSecret)
    {
        try
        {
            // 方案1: 验证服务密钥（基于PID生成）
            if (ValidateServiceSecret(servicePID, serviceSecret))
            {
                _logger.LogDebug("Service authenticated - PID: {PID}", servicePID);

                return ServiceRequestContext.CreateServiceContext(servicePID, serviceSecret);
            }

            _logger.LogWarning("Service authentication failed - PID: {PID}", servicePID);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating service - PID: {PID}", servicePID);
            return null;
        }
    }

    /// <summary>
    /// 验证外部用户 - JWT Token
    /// </summary>
    public async Task<ServiceRequestContext?> AuthenticateUserAsync(string token)
    {
        try
        {
            // 验证JWT Token
            var claims = await _jwtTokenService.ValidateTokenAsync(token);
            if (claims == null)
            {
                _logger.LogWarning("Invalid user token");
                return null;
            }

            // 提取用户 ID (JWT 的 sub claim 会被自动映射为 ClaimTypes.NameIdentifier)
            var userId = claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Token missing user ID");
                return null;
            }


            // 提取权限和角色
            var permissions = claims
                .Where(c => c.Type == "permission")
                .Select(c => c.Value)
                .ToHashSet();

            var roles = claims
                .Where(c => c.Type == "role")
                .Select(c => c.Value)
                .ToHashSet();

            var expiresAt = claims.FirstOrDefault(x => x.Type is "exp")?.Value;
            var expirationTime = expiresAt != null
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiresAt)).UtcDateTime
                : (DateTime?)null;

            _logger.LogDebug("User authenticated - UserId: {UserId}, Roles: {Roles}", userId, string.Join(",", roles));

            return ServiceRequestContext.CreateUserContext(
                userId,
                token,
                permissions,
                roles,
                expiresIn: expirationTime.HasValue ? expirationTime.Value - DateTime.UtcNow : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error authenticating user token");
            return null;
        }
    }

    /// <summary>
    /// 生成服务密钥 - 基于PID和集群密钥的HMAC
    /// </summary>
    public string GenerateServiceSecret(PID servicePID)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(_clusterSecret));

        var data = System.Text.Encoding.UTF8.GetBytes(servicePID.ToString());
        var hash = hmac.ComputeHash(data);

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 验证服务密钥
    /// </summary>
    public bool ValidateServiceSecret(PID servicePID, string secret)
    {
        var expectedSecret = GenerateServiceSecret(servicePID);
        return expectedSecret == secret;
    }
}

/// <summary>
/// JWT Token服务接口
/// </summary>
public interface IJwtTokenService
{
    Task<IEnumerable<System.Security.Claims.Claim>?> ValidateTokenAsync(string token);
    Task<string> GenerateTokenAsync(string userId, HashSet<string> permissions, HashSet<string> roles);
}

/// <summary>
/// JWT Token服务实现（简化版）
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly string _secretKey;
    private readonly ILogger<JwtTokenService> _logger;

    public JwtTokenService(IConfiguration configuration, ILogger<JwtTokenService> logger)
    {
        _secretKey = configuration["JwtSecret"] ?? throw new InvalidOperationException("JwtSecret not configured");
        _logger = logger;
    }

    public Task<IEnumerable<System.Security.Claims.Claim>?> ValidateTokenAsync(string token)
    {
        // 实际实现中使用 System.IdentityModel.Tokens.Jwt 验证
        // 这里简化处理
        try
        {
            // 模拟JWT验证
            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim("sub", "user123"),
                new System.Security.Claims.Claim("role", "User"),
                new System.Security.Claims.Claim("permission", "read:users")
            };

            return Task.FromResult<IEnumerable<System.Security.Claims.Claim>?>(claims);
        }
        catch
        {
            return Task.FromResult<IEnumerable<System.Security.Claims.Claim>?>(null);
        }
    }

    public Task<string> GenerateTokenAsync(string userId, HashSet<string> permissions, HashSet<string> roles)
    {
        // 实际实现中使用 System.IdentityModel.Tokens.Jwt 生成
        return Task.FromResult($"jwt_token_for_{userId}");
    }
}

// ========================
// 1. 消息类型定义
// ========================

/// <summary>
/// 消息类型枚举
/// </summary>
public enum ActorMessageType
{
    /// <summary>方法调用消息</summary>
    MethodInvocation,

    /// <summary>定时器消息</summary>
    Timer,

    /// <summary>系统消息</summary>
    System
}

// ========================
// 4. 带认证的消息定义
// ========================

/// <summary>
/// 带认证上下文的服务消息基类
/// </summary>
public abstract class ServiceMessage
{
    public Guid MessageId { get; } = Guid.NewGuid();
    public ActorMessageType Type { get; init; }
    public DateTime EnqueueTime { get; } = DateTime.UtcNow;
    public CancellationToken CancellationToken { get; init; }

    /// <summary>认证上下文</summary>
    public IServiceRequestContext? AuthContext { get; set; }

    /// <summary>发送者连接（用于 RequestContext）</summary>
    public IServerTransport? Sender { get; set; }

    /// <summary>消息优先级（默认为 Normal）</summary>
    public PulseRPC.MessagePriority Priority { get; set; } = PulseRPC.MessagePriority.Normal;
}

/// <summary>
/// 方法调用消息（带认证）
/// </summary>
public class MethodInvocationMessage : ServiceMessage
{
    /// <summary>
    /// 协议号 - 用于路由到具体方法
    /// </summary>
    public PulseRPC.Protocol.ProtocolId ProtocolId { get; set; }

    /// <summary>
    /// 方法参数
    /// </summary>
    public object?[] Arguments { get; set; } = Array.Empty<object?>();

    /// <summary>
    /// 返回值类型（可选）
    /// </summary>
    public Type? ReturnType { get; set; }

    /// <summary>
    /// 完成源 - 用于异步返回结果
    /// </summary>
    public TaskCompletionSource<object?> CompletionSource { get; } = new();

    public MethodInvocationMessage()
    {
        Type = ActorMessageType.MethodInvocation;
    }
}

// ========================
// 5. 权限验证特性
// ========================

/// <summary>
/// 权限验证特性 - 标记在方法上
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute
{
    public string Permission { get; }
    public bool AllowInternal { get; set; } = true; // 是否允许内部服务绕过
    public bool AllowSystem { get; set; } = true;   // 是否允许系统调用绕过

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// 角色验证特性
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RequireRoleAttribute : Attribute
{
    public string Role { get; }
    public bool AllowInternal { get; set; } = true;
    public bool AllowSystem { get; set; } = true;

    public RequireRoleAttribute(string role)
    {
        Role = role;
    }
}

/// <summary>
/// 仅内部服务可调用
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class InternalOnlyAttribute : Attribute
{
}

/// <summary>
/// 仅外部用户可调用
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ExternalOnlyAttribute : Attribute
{
}

// ========================
// 6. 权限验证器
// ========================

/// <summary>
/// 权限验证器
/// </summary>
public class PermissionValidator
{
    private readonly ILogger<PermissionValidator> _logger;

    public PermissionValidator(ILogger<PermissionValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 验证方法调用权限
    /// </summary>
    public bool ValidateMethodCall(
        MethodInfo methodInfo,
        IServiceRequestContext? authContext,
        out string? errorMessage)
    {
        errorMessage = null;

        // 没有认证上下文
        if (authContext == null)
        {
            // 检查是否需要认证
            if (HasAnyAuthenticationAttribute(methodInfo))
            {
                errorMessage = "Authentication required";
                _logger.LogWarning("Unauthenticated call to {Method}", methodInfo.Name);
                return false;
            }
            return true; // 方法不需要认证
        }

        // 检查Token是否过期
        if (authContext.IsExpired)
        {
            errorMessage = "Authentication token expired";
            _logger.LogWarning("Expired token for {Caller}", authContext.CallerId);
            return false;
        }

        // 检查InternalOnly特性
        var internalOnly = methodInfo.GetCustomAttribute<InternalOnlyAttribute>();
        if (internalOnly != null && authContext.SourceType != CallSourceType.InternalService)
        {
            errorMessage = "This method can only be called by internal services";
            _logger.LogWarning(
                "Unauthorized internal-only call - Method: {Method}, Caller: {Caller}, Source: {Source}",
                methodInfo.Name, authContext.CallerId, authContext.SourceType);
            return false;
        }

        // 检查ExternalOnly特性
        var externalOnly = methodInfo.GetCustomAttribute<ExternalOnlyAttribute>();
        if (externalOnly != null && authContext.SourceType != CallSourceType.ExternalUser)
        {
            errorMessage = "This method can only be called by external users";
            _logger.LogWarning(
                "Unauthorized external-only call - Method: {Method}, Caller: {Caller}, Source: {Source}",
                methodInfo.Name, authContext.CallerId, authContext.SourceType);
            return false;
        }

        // 检查权限要求
        var permissionAttrs = methodInfo.GetCustomAttributes<RequirePermissionAttribute>();
        foreach (var attr in permissionAttrs)
        {
            // 内部服务绕过检查
            if (attr.AllowInternal && authContext.SourceType == CallSourceType.InternalService)
                continue;

            // 系统调用绕过检查
            if (attr.AllowSystem && authContext.SourceType == CallSourceType.SystemTimer)
                continue;

            if (!authContext.HasPermission(attr.Permission))
            {
                errorMessage = $"Missing required permission: {attr.Permission}";
                _logger.LogWarning(
                    "Permission denied - Method: {Method}, Caller: {Caller}, Required: {Permission}",
                    methodInfo.Name, authContext.CallerId, attr.Permission);
                return false;
            }
        }

        // 检查角色要求
        var roleAttrs = methodInfo.GetCustomAttributes<RequireRoleAttribute>();
        foreach (var attr in roleAttrs)
        {
            if (attr.AllowInternal && authContext.SourceType == CallSourceType.InternalService)
                continue;

            if (attr.AllowSystem && authContext.SourceType == CallSourceType.SystemTimer)
                continue;

            if (!authContext.HasRole(attr.Role))
            {
                errorMessage = $"Missing required role: {attr.Role}";
                _logger.LogWarning(
                    "Role denied - Method: {Method}, Caller: {Caller}, Required: {Role}",
                    methodInfo.Name, authContext.CallerId, attr.Role);
                return false;
            }
        }

        _logger.LogDebug(
            "Method call authorized - Method: {Method}, Caller: {Caller}, Source: {Source}",
            methodInfo.Name, authContext.CallerId, authContext.SourceType);

        return true;
    }

    private bool HasAnyAuthenticationAttribute(MethodInfo methodInfo)
    {
        return methodInfo.GetCustomAttribute<RequirePermissionAttribute>() != null
            || methodInfo.GetCustomAttribute<RequireRoleAttribute>() != null
            || methodInfo.GetCustomAttribute<InternalOnlyAttribute>() != null
            || methodInfo.GetCustomAttribute<ExternalOnlyAttribute>() != null;
    }
}

// ========================
// 7. 优先级消息包装
// ========================

/// <summary>
/// 优先级消息包装类 - 用于优先级队列排序
/// </summary>
internal sealed class PriorityServiceMessage : IComparable<PriorityServiceMessage>
{
    public ServiceMessage Message { get; }
    public long Sequence { get; } // 序列号，用于同优先级 FIFO

    public PriorityServiceMessage(ServiceMessage message, long sequence)
    {
        Message = message;
        Sequence = sequence;
    }

    /// <summary>
    /// 比较优先级：优先级高的优先（Critical=0 最高），同优先级按序列号排序（FIFO）
    /// </summary>
    public int CompareTo(PriorityServiceMessage? other)
    {
        if (other == null) return 1;

        // 优先级数值越小，优先级越高（Critical=0, High=1, Normal=2, Low=3, Bulk=4）
        // PriorityQueue 是最小堆，所以较小值优先出队
        var priorityCompare = Message.Priority.CompareTo(other.Message.Priority);
        if (priorityCompare != 0) return priorityCompare;

        // 同优先级按序列号排序（FIFO）
        return Sequence.CompareTo(other.Sequence);
    }
}

// ========================
// 8. 增强的Actor消息队列（带认证 + 优先级）
// ========================

/// <summary>
/// 带认证的Actor消息队列（支持优先级）
/// </summary>
internal class AuthenticatedServiceMessageQueue : IAsyncDisposable
{
    // MethodInfo 缓存 - 用于优化权限验证时的反射性能
    // TODO: 协议号到方法的映射将由 SourceGenerator 生成
    private static readonly ConcurrentDictionary<(Type ServiceType, Protocol.ProtocolId ProtocolId), MethodInfo?> _methodInfoCache = new();

    private readonly PriorityQueue<PriorityServiceMessage, PriorityServiceMessage> _priorityQueue;
    private readonly SemaphoreSlim _signal; // 信号量，通知有新消息
    private readonly object _queueLock = new(); // 队列锁（PriorityQueue 不是线程安全的）
    private long _sequenceCounter; // 序列号计数器，确保同优先级 FIFO
    private readonly int _capacity; // 队列容量

    private readonly ILogger _logger;
    private readonly string _actorName;
    private readonly PID _actorPID;
    private readonly Type _actorType;
    private readonly PermissionValidator _permissionValidator;

    private readonly ConcurrentDictionary<string, ServiceTimer.TimerWrapper> _timers = new();

    // 并发控制
    private readonly int _maxConcurrency; // 最大并发度（1 表示单线程模型）
    private readonly SemaphoreSlim? _concurrencySemaphore; // 并发槽位信号量
    private readonly List<Task>? _runningTasks; // 运行中的任务列表（并发模式）
    private readonly object? _tasksLock; // 任务列表锁

    // 背压策略与监控
    private readonly Configuration.BackpressureStrategy _backpressureStrategy; // 背压策略
    private readonly ServiceQueueMetrics _metrics; // 队列监控指标

    private long _totalProcessed;
    private long _totalFailed;
    private long _authenticationFailures;
    private long _authorizationFailures;

    private Task? _processingTask;
    private readonly CancellationTokenSource _disposalCts = new();

    public AuthenticatedServiceMessageQueue(
        string actorName,
        PID actorPID,
        Type actorType,
        ILogger logger,
        PermissionValidator permissionValidator,
        int capacity = 10000,
        int maxConcurrency = 1, // 默认单线程模型
        Configuration.BackpressureStrategy backpressureStrategy = Configuration.BackpressureStrategy.Block) // 默认阻塞策略
    {
        _actorName = actorName;
        _actorPID = actorPID;
        _actorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        _logger = logger;
        _permissionValidator = permissionValidator;
        _capacity = capacity;
        _maxConcurrency = maxConcurrency;
        _backpressureStrategy = backpressureStrategy;

        if (_maxConcurrency < 1)
            throw new ArgumentException("MaxConcurrency must be at least 1", nameof(maxConcurrency));

        _priorityQueue = new PriorityQueue<PriorityServiceMessage, PriorityServiceMessage>();
        _signal = new SemaphoreSlim(0);
        _sequenceCounter = 0;

        // 初始化监控指标
        _metrics = new ServiceQueueMetrics(capacity);

        // 并发模式初始化
        if (_maxConcurrency > 1)
        {
            _concurrencySemaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
            _runningTasks = new List<Task>();
            _tasksLock = new object();

            _logger.LogInformation(
                "Concurrent message queue initialized - Actor: {ActorName}, MaxConcurrency: {MaxConcurrency}, BackpressureStrategy: {Strategy}",
                _actorName, _maxConcurrency, _backpressureStrategy);
        }
        else
        {
            _logger.LogInformation(
                "Sequential message queue initialized - Actor: {ActorName}, BackpressureStrategy: {Strategy}",
                _actorName, _backpressureStrategy);
        }
    }

    public void Start(Func<ServiceMessage, Task> messageHandler)
    {
        if (_processingTask != null)
            throw new InvalidOperationException("Message queue processing is already started");

        // 根据并发模式选择不同的处理循环
        if (_maxConcurrency > 1)
        {
            _processingTask = Task.Run(async () => await ProcessMessagesConcurrentlyAsync(messageHandler));
        }
        else
        {
            _processingTask = Task.Run(async () => await ProcessMessagesAsync(messageHandler));
        }

        _logger.LogInformation(
            "Authenticated message queue started - Actor: {ActorName}, PID: {PID}, Mode: {Mode}",
            _actorName, _actorPID, _maxConcurrency > 1 ? "Concurrent" : "Sequential");
    }

    /// <summary>
    /// 发送方法调用消息（带认证上下文 + 优先级）
    /// </summary>
    public async Task<TResult> SendMethodInvocationAsync<TResult>(
        PulseRPC.Protocol.ProtocolId protocolId,
        object?[] arguments,
        IServiceRequestContext? authContext,
        CancellationToken cancellationToken = default)
    {
        var message = new MethodInvocationMessage
        {
            ProtocolId = protocolId,
            Arguments = arguments,
            ReturnType = typeof(TResult),
            CancellationToken = cancellationToken,
            AuthContext = authContext,
            Priority = ExtractMethodPriority(protocolId) // ✅ 从方法特性中提取优先级
        };

        // 入队消息（带优先级）
        EnqueueMessage(message);

        var result = await message.CompletionSource.Task;

        if (result is TResult typedResult)
            return typedResult;

        if (result == null && typeof(TResult).IsValueType)
            return default!;

        return (TResult)result!;
    }

    public async Task SendMethodInvocationAsync(
        PulseRPC.Protocol.ProtocolId protocolId,
        object?[] arguments,
        IServiceRequestContext? authContext,
        CancellationToken cancellationToken = default)
    {
        var message = new MethodInvocationMessage
        {
            ProtocolId = protocolId,
            Arguments = arguments,
            ReturnType = null,
            CancellationToken = cancellationToken,
            AuthContext = authContext,
            Priority = ExtractMethodPriority(protocolId) // ✅ 从方法特性中提取优先级
        };

        // 入队消息（带优先级）
        EnqueueMessage(message);

        await message.CompletionSource.Task;
    }

    /// <summary>
    /// 入队消息（线程安全）
    /// </summary>
    private void EnqueueMessage(ServiceMessage message)
    {
        lock (_queueLock)
        {
            var currentCount = _priorityQueue.Count;

            // 检查队列容量
            if (currentCount >= _capacity)
            {
                // 根据背压策略处理队列满的情况
                switch (_backpressureStrategy)
                {
                    case Configuration.BackpressureStrategy.Block:
                        // Block 策略：抛出异常，让调用者决定是否重试
                        _metrics.RecordRejected();
                        _logger.LogWarning(
                            "Message queue is full (capacity: {Capacity}) - Strategy: Block, MessageId: {MessageId}",
                            _capacity, message.MessageId);
                        throw new InvalidOperationException($"Message queue is full (capacity: {_capacity})");

                    case Configuration.BackpressureStrategy.DropOldest:
                        // DropOldest 策略：移除最旧消息，插入新消息
                        if (_priorityQueue.TryDequeue(out var droppedOldest, out _))
                        {
                            _metrics.RecordDroppedOldest();
                            _logger.LogWarning(
                                "Dropping oldest message - Queue full, Strategy: DropOldest, DroppedMessageId: {DroppedMessageId}, NewMessageId: {NewMessageId}",
                                droppedOldest.Message.MessageId, message.MessageId);

                            // 如果被丢弃的消息有 CompletionSource，设置为取消
                            if (droppedOldest.Message is MethodInvocationMessage droppedMethod)
                            {
                                droppedMethod.CompletionSource.TrySetCanceled();
                            }
                        }
                        break;

                    case Configuration.BackpressureStrategy.DropNewest:
                        // DropNewest 策略：拒绝新消息，保留队列中的消息
                        _metrics.RecordDroppedNewest();
                        _logger.LogWarning(
                            "Dropping newest message - Queue full, Strategy: DropNewest, DroppedMessageId: {MessageId}",
                            message.MessageId);

                        // 如果消息有 CompletionSource，设置为取消
                        if (message is MethodInvocationMessage methodMsg)
                        {
                            methodMsg.CompletionSource.TrySetCanceled();
                        }
                        return; // 不入队，直接返回

                    case Configuration.BackpressureStrategy.Reject:
                        // Reject 策略：拒绝新消息并抛出异常
                        _metrics.RecordRejected();
                        _logger.LogWarning(
                            "Rejecting message - Queue full, Strategy: Reject, MessageId: {MessageId}",
                            message.MessageId);
                        throw new InvalidOperationException($"Message rejected - queue is full (capacity: {_capacity})");

                    default:
                        throw new NotSupportedException($"Backpressure strategy '{_backpressureStrategy}' is not supported");
                }
            }

            // 生成序列号（确保同优先级 FIFO）
            var sequence = Interlocked.Increment(ref _sequenceCounter);
            var priorityMessage = new PriorityServiceMessage(message, sequence);

            // 入队
            _priorityQueue.Enqueue(priorityMessage, priorityMessage);

            // 记录入队指标
            _metrics.RecordEnqueue();
        }

        // 释放信号量，通知有新消息
        _signal.Release();
    }

    /// <summary>
    /// 消息处理循环（并发模式）
    /// </summary>
    private async Task ProcessMessagesConcurrentlyAsync(Func<ServiceMessage, Task> messageHandler)
    {
        _logger.LogInformation("Concurrent message processing loop started - Actor: {ActorName}, MaxConcurrency: {MaxConcurrency}",
            _actorName, _maxConcurrency);

        try
        {
            while (!_disposalCts.Token.IsCancellationRequested)
            {
                // 等待新消息
                await _signal.WaitAsync(_disposalCts.Token);

                PriorityServiceMessage? priorityMessage;
                lock (_queueLock)
                {
                    // 从优先级队列中出队
                    if (!_priorityQueue.TryDequeue(out priorityMessage, out _))
                        continue;

                    // 记录出队指标
                    _metrics.RecordDequeue();
                }

                // 等待并发槽位
                await _concurrencySemaphore!.WaitAsync(_disposalCts.Token);

                var message = priorityMessage.Message;

                // 启动并发处理任务
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // 设置认证上下文
                        using (ServiceRequestContextProvider.SetContext(message.AuthContext!))
                        {
                            // 对于方法调用消息，进行权限验证
                            if (message is MethodInvocationMessage methodMsg)
                            {
                                if (!await ValidateMethodCallAsync(methodMsg))
                                {
                                    Interlocked.Increment(ref _authorizationFailures);
                                    _metrics.RecordError(); // 记录权限验证失败
                                    return; // 验证失败，跳过处理
                                }
                            }

                            await messageHandler(message);
                            Interlocked.Increment(ref _totalProcessed);
                            _metrics.RecordProcessed(); // 记录处理成功
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _totalFailed);
                        _metrics.RecordError(); // 记录处理错误

                        _logger.LogError(ex,
                            "Message processing failed - Actor: {ActorName}, Type: {Type}, MessageId: {MessageId}, Priority: {Priority}",
                            _actorName, message.Type, message.MessageId, message.Priority);

                        if (message is MethodInvocationMessage methodMsg)
                        {
                            methodMsg.CompletionSource.TrySetException(ex);
                        }
                    }
                    finally
                    {
                        // 释放并发槽位
                        _concurrencySemaphore!.Release();
                    }
                }, _disposalCts.Token);

                // 添加到运行中的任务列表
                lock (_tasksLock!)
                {
                    _runningTasks!.Add(task);

                    // 清理已完成的任务（每 10 个任务清理一次，避免频繁锁）
                    if (_runningTasks.Count > _maxConcurrency * 2)
                    {
                        _runningTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Concurrent message processing cancelled - Actor: {ActorName}", _actorName);
        }
        finally
        {
            // 等待所有运行中的任务完成
            Task[]? tasksToWait;
            lock (_tasksLock!)
            {
                tasksToWait = _runningTasks!.ToArray();
            }

            if (tasksToWait.Length > 0)
            {
                _logger.LogInformation("Waiting for {Count} running tasks to complete - Actor: {ActorName}",
                    tasksToWait.Length, _actorName);

                try
                {
                    await Task.WhenAll(tasksToWait);
                }
                catch
                {
                    // 忽略任务异常（已在任务内部处理）
                }
            }
        }
    }

    /// <summary>
    /// 消息处理循环（单线程模式 + 权限验证 + 优先级）
    /// </summary>
    private async Task ProcessMessagesAsync(Func<ServiceMessage, Task> messageHandler)
    {
        _logger.LogInformation("Message processing loop started - Actor: {ActorName}", _actorName);

        try
        {
            while (!_disposalCts.Token.IsCancellationRequested)
            {
                // 等待新消息
                await _signal.WaitAsync(_disposalCts.Token);

                PriorityServiceMessage? priorityMessage;
                lock (_queueLock)
                {
                    // 从优先级队列中出队（优先级高的优先）
                    if (!_priorityQueue.TryDequeue(out priorityMessage, out _))
                        continue;

                    // 记录出队指标
                    _metrics.RecordDequeue();
                }

                var message = priorityMessage.Message;
                var processingStart = Stopwatch.GetTimestamp();

                try
                {
                    // 设置认证上下文
                    using (ServiceRequestContextProvider.SetContext(message.AuthContext!))
                    {
                        // 对于方法调用消息，进行权限验证
                        if (message is MethodInvocationMessage methodMsg)
                        {
                            if (!await ValidateMethodCallAsync(methodMsg))
                            {
                                Interlocked.Increment(ref _authorizationFailures);
                                _metrics.RecordError(); // 记录权限验证失败
                                continue; // 验证失败，跳过处理
                            }
                        }

                        await messageHandler(message);
                        Interlocked.Increment(ref _totalProcessed);
                        _metrics.RecordProcessed(); // 记录处理成功
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _totalFailed);
                    _metrics.RecordError(); // 记录处理错误

                    _logger.LogError(ex,
                        "Message processing failed - Actor: {ActorName}, Type: {Type}, MessageId: {MessageId}, Priority: {Priority}",
                        _actorName, message.Type, message.MessageId, message.Priority);

                    if (message is MethodInvocationMessage methodMsg)
                    {
                        methodMsg.CompletionSource.TrySetException(ex);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled - Actor: {ActorName}", _actorName);
        }
    }

    /// <summary>
    /// 验证方法调用权限 + 提取优先级
    /// </summary>
    private async Task<bool> ValidateMethodCallAsync(MethodInvocationMessage message)
    {
        try
        {
            // 从缓存获取 MethodInfo（使用 SourceGenerator 生成的映射表）
            var actorType = GetActorType();
            var methodInfo = _methodInfoCache.GetOrAdd(
                (actorType, message.ProtocolId),
                key => PulseRPC.Generated.ProtocolIdMapping.GetMethod(key.ServiceType, key.ProtocolId));

            if (methodInfo == null)
            {
                _logger.LogError("Method not found - ProtocolId: {ProtocolId} (0x{ProtocolIdHex})",
                    message.ProtocolId.Value, message.ProtocolId.Value.ToString("X4"));
                message.CompletionSource.TrySetException(
                    new InvalidOperationException($"Method with ProtocolId '{message.ProtocolId}' (0x{message.ProtocolId.Value:X4}) not found"));
                return false;
            }

            // 验证权限
            if (!_permissionValidator.ValidateMethodCall(methodInfo, message.AuthContext, out var errorMessage))
            {
                _logger.LogWarning(
                    "Authorization failed - ProtocolId: {ProtocolId}, Caller: {Caller}, Error: {Error}",
                    message.ProtocolId,
                    message.AuthContext?.CallerId ?? "Anonymous",
                    errorMessage);

                message.CompletionSource.TrySetException(
                    new UnauthorizedAccessException(errorMessage));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating method call - ProtocolId: {ProtocolId}", message.ProtocolId);
            message.CompletionSource.TrySetException(ex);
            return false;
        }
    }

    /// <summary>
    /// 从方法中提取优先级特性
    /// </summary>
    private PulseRPC.MessagePriority ExtractMethodPriority(PulseRPC.Protocol.ProtocolId protocolId)
    {
        try
        {
            var actorType = GetActorType();
            var methodInfo = _methodInfoCache.GetOrAdd(
                (actorType, protocolId),
                key => PulseRPC.Generated.ProtocolIdMapping.GetMethod(key.ServiceType, key.ProtocolId));

            if (methodInfo == null)
                return PulseRPC.MessagePriority.Normal; // 默认优先级

            // 查找 PriorityAttribute
            var priorityAttr = methodInfo.GetCustomAttribute<PriorityAttribute>();
            if (priorityAttr != null)
            {
                return priorityAttr.Priority;
            }

            return PulseRPC.MessagePriority.Normal; // 默认优先级
        }
        catch
        {
            return PulseRPC.MessagePriority.Normal;
        }
    }

    private Type GetActorType()
    {
        return _actorType;
    }

    // 定时器管理
    public string ScheduleOnce(TimeSpan delay, Func<Task> callback)
    {
        var timerId = Guid.NewGuid().ToString("N");

        // 创建一个 System.Threading.Timer 来延迟执行
        var timer = new System.Threading.Timer(_ =>
        {
            try
            {
                // 将定时器消息添加到优先队列
                var message = new TimerMessage
                {
                    TimerId = timerId,
                    Callback = callback,
                    IsRecurring = false
                };
                Enqueue(message, PulseRPC.MessagePriority.Normal);

                // 一次性定时器，执行后取消
                CancelTimer(timerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in one-time timer callback - TimerId: {TimerId}", timerId);
            }
        }, null, delay, Timeout.InfiniteTimeSpan);

        if (_timers.TryAdd(timerId, new ServiceTimer.TimerWrapper(timer)))
        {
            _logger.LogDebug("Scheduled one-time timer - TimerId: {TimerId}, Delay: {Delay}ms",
                timerId, delay.TotalMilliseconds);
            return timerId;
        }

        timer.Dispose();
        _logger.LogWarning("Failed to schedule timer - TimerId: {TimerId}", timerId);
        return string.Empty;
    }

    public string ScheduleRecurring(TimeSpan initialDelay, TimeSpan interval, Func<Task> callback)
    {
        var timerId = Guid.NewGuid().ToString("N");

        // 创建一个 System.Threading.Timer 来周期性执行
        var timer = new System.Threading.Timer(_ =>
        {
            try
            {
                // 将定时器消息添加到优先队列
                var message = new TimerMessage
                {
                    TimerId = timerId,
                    Callback = callback,
                    IsRecurring = true,
                    Interval = interval
                };
                Enqueue(message, PulseRPC.MessagePriority.Normal);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in recurring timer callback - TimerId: {TimerId}", timerId);
            }
        }, null, initialDelay, interval);

        if (_timers.TryAdd(timerId, new ServiceTimer.TimerWrapper(timer)))
        {
            _logger.LogDebug("Scheduled recurring timer - TimerId: {TimerId}, InitialDelay: {InitialDelay}ms, Interval: {Interval}ms",
                timerId, initialDelay.TotalMilliseconds, interval.TotalMilliseconds);
            return timerId;
        }

        timer.Dispose();
        _logger.LogWarning("Failed to schedule recurring timer - TimerId: {TimerId}", timerId);
        return string.Empty;
    }

    public bool CancelTimer(string timerId)
    {
        if (_timers.TryRemove(timerId, out var timerWrapper))
        {
            timerWrapper.Dispose();
            _logger.LogDebug("Timer cancelled - TimerId: {TimerId}", timerId);
            return true;
        }

        _logger.LogWarning("Timer not found - TimerId: {TimerId}", timerId);
        return false;
    }

    // 内部辅助方法：将消息排入队列
    private void Enqueue(ServiceMessage message, PulseRPC.MessagePriority priority)
    {
        // 设置消息优先级
        message.Priority = priority;

        var sequence = Interlocked.Increment(ref _sequenceCounter);
        var priorityMessage = new PriorityServiceMessage(message, sequence);

        lock (_queueLock)
        {
            _priorityQueue.Enqueue(priorityMessage, priorityMessage);
        }

        _signal.Release(); // 通知处理循环有新消息
    }

    /// <summary>
    /// 获取队列监控指标快照
    /// </summary>
    public ServiceQueueMetricsSnapshot GetMetricsSnapshot()
    {
        return _metrics.GetSnapshot();
    }

    /// <summary>
    /// 获取当前队列深度
    /// </summary>
    public int GetCurrentQueueDepth()
    {
        lock (_queueLock)
        {
            return _priorityQueue.Count;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposalCts.Cancel();

        if (_processingTask != null)
        {
            try { await _processingTask; }
            catch (OperationCanceledException) { }
        }

        _disposalCts.Dispose();
        _signal.Dispose();
        _concurrencySemaphore?.Dispose(); // 并发模式的信号量

        // 清空优先级队列
        lock (_queueLock)
        {
            _priorityQueue.Clear();
        }

        // 清空任务列表（并发模式）
        if (_runningTasks != null)
        {
            lock (_tasksLock!)
            {
                _runningTasks.Clear();
            }
        }
    }
}
