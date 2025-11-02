using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Models;
using System.Linq;

// 类型别名 - 服务间认证上下文
using AuthenticationContext = PulseRPC.Server.ServiceAuthenticationContext;
using AuthenticationContextProvider = PulseRPC.Server.ServiceAuthenticationContextProvider;

namespace PulseRPC.Server;

// 认证上下文定义已移至: src/PulseRPC.Server/Authentication/ServiceAuthenticationContext.cs

// ========================
// 2. 认证服务接口
// ========================

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthenticationService
{
    /// <summary>验证内部服务认证</summary>
    Task<AuthenticationContext?> AuthenticateServiceAsync(PID servicePID, string serviceSecret);

    /// <summary>验证外部用户Token</summary>
    Task<AuthenticationContext?> AuthenticateUserAsync(string token);

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
    Task<bool> CheckPermissionAsync(AuthenticationContext context, string permission);

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
    public async Task<AuthenticationContext?> AuthenticateServiceAsync(PID servicePID, string serviceSecret)
    {
        try
        {
            // 方案1: 验证服务密钥（基于PID生成）
            if (ValidateServiceSecret(servicePID, serviceSecret))
            {
                _logger.LogDebug("Service authenticated - PID: {PID}", servicePID);

                return AuthenticationContext.CreateServiceContext(servicePID, serviceSecret);
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
    public async Task<AuthenticationContext?> AuthenticateUserAsync(string token)
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

            var userId = claims.FirstOrDefault(c => c.Type is "sub" or "userId")?.Value;
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

            return AuthenticationContext.CreateUserContext(
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
    public AuthenticationContext? AuthContext { get; set; }
}

/// <summary>
/// 方法调用消息（带认证）
/// </summary>
public class MethodInvocationMessage : ServiceMessage
{
    public string MethodName { get; set; } = string.Empty;
    public object?[] Arguments { get; set; } = Array.Empty<object?>();
    public Type? ReturnType { get; set; }
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
        AuthenticationContext? authContext,
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
// 7. 增强的Actor消息队列（带认证）
// ========================

/// <summary>
/// 带认证的Actor消息队列
/// </summary>
internal class AuthenticatedServiceMessageQueue : IAsyncDisposable
{
    // MethodInfo 缓存 - 用于优化权限验证时的反射性能
    private static readonly ConcurrentDictionary<(Type ServiceType, string MethodName), System.Reflection.MethodInfo?> _methodInfoCache = new();

    private readonly Channel<ServiceMessage> _messageChannel;
    private readonly ILogger _logger;
    private readonly string _actorName;
    private readonly PID _actorPID;
    private readonly Type _actorType;
    private readonly PermissionValidator _permissionValidator;

    private readonly ConcurrentDictionary<string, ServiceTimer> _timers = new();

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
        int capacity = 10000)
    {
        _actorName = actorName;
        _actorPID = actorPID;
        _actorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        _logger = logger;
        _permissionValidator = permissionValidator;

        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _messageChannel = Channel.CreateBounded<ServiceMessage>(options);
    }

    public void Start(Func<ServiceMessage, Task> messageHandler)
    {
        if (_processingTask != null)
            throw new InvalidOperationException("Message queue processing is already started");

        _processingTask = Task.Run(async () => await ProcessMessagesAsync(messageHandler));

        _logger.LogInformation(
            "Authenticated message queue started - Actor: {ActorName}, PID: {PID}",
            _actorName, _actorPID);
    }

    /// <summary>
    /// 发送方法调用消息（带认证上下文）
    /// </summary>
    public async Task<TResult> SendMethodInvocationAsync<TResult>(
        string methodName,
        object?[] arguments,
        AuthenticationContext? authContext,
        CancellationToken cancellationToken = default)
    {
        var message = new MethodInvocationMessage
        {
            MethodName = methodName,
            Arguments = arguments,
            ReturnType = typeof(TResult),
            CancellationToken = cancellationToken,
            AuthContext = authContext
        };

        await _messageChannel.Writer.WriteAsync(message, cancellationToken);

        var result = await message.CompletionSource.Task;

        if (result is TResult typedResult)
            return typedResult;

        if (result == null && typeof(TResult).IsValueType)
            return default!;

        return (TResult)result!;
    }

    public async Task SendMethodInvocationAsync(
        string methodName,
        object?[] arguments,
        AuthenticationContext? authContext,
        CancellationToken cancellationToken = default)
    {
        var message = new MethodInvocationMessage
        {
            MethodName = methodName,
            Arguments = arguments,
            ReturnType = null,
            CancellationToken = cancellationToken,
            AuthContext = authContext
        };

        await _messageChannel.Writer.WriteAsync(message, cancellationToken);
        await message.CompletionSource.Task;
    }

    /// <summary>
    /// 消息处理循环（带权限验证）
    /// </summary>
    private async Task ProcessMessagesAsync(Func<ServiceMessage, Task> messageHandler)
    {
        _logger.LogInformation("Message processing loop started - Actor: {ActorName}", _actorName);

        try
        {
            await foreach (var message in _messageChannel.Reader.ReadAllAsync(_disposalCts.Token))
            {
                var processingStart = Stopwatch.GetTimestamp();

                try
                {
                    // 设置认证上下文
                    using (AuthenticationContextProvider.SetContext(message.AuthContext!))
                    {
                        // 对于方法调用消息，进行权限验证
                        if (message is MethodInvocationMessage methodMsg)
                        {
                            if (!await ValidateMethodCallAsync(methodMsg))
                            {
                                Interlocked.Increment(ref _authorizationFailures);
                                continue; // 验证失败，跳过处理
                            }
                        }

                        await messageHandler(message);
                        Interlocked.Increment(ref _totalProcessed);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _totalFailed);

                    _logger.LogError(ex,
                        "Message processing failed - Actor: {ActorName}, Type: {Type}, MessageId: {MessageId}",
                        _actorName, message.Type, message.MessageId);

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
    /// 验证方法调用权限
    /// </summary>
    private async Task<bool> ValidateMethodCallAsync(MethodInvocationMessage message)
    {
        try
        {
            // 从缓存获取 MethodInfo，提升性能
            var actorType = GetActorType();
            var methodInfo = _methodInfoCache.GetOrAdd(
                (actorType, message.MethodName),
                static key => key.ServiceType.GetMethod(
                    key.MethodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));

            if (methodInfo == null)
            {
                _logger.LogError("Method not found - Method: {Method}", message.MethodName);
                message.CompletionSource.TrySetException(
                    new InvalidOperationException($"Method '{message.MethodName}' not found"));
                return false;
            }

            // 验证权限
            if (!_permissionValidator.ValidateMethodCall(methodInfo, message.AuthContext, out var errorMessage))
            {
                _logger.LogWarning(
                    "Authorization failed - Method: {Method}, Caller: {Caller}, Error: {Error}",
                    message.MethodName,
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
            _logger.LogError(ex, "Error validating method call");
            message.CompletionSource.TrySetException(ex);
            return false;
        }
    }

    private Type GetActorType()
    {
        return _actorType;
    }

    // 定时器管理（省略，与之前相同）
    public string ScheduleOnce(TimeSpan delay, Func<Task> callback) => string.Empty;
    public string ScheduleRecurring(TimeSpan initialDelay, TimeSpan interval, Func<Task> callback) => string.Empty;
    public bool CancelTimer(string timerId) => false;

    public ActorStatistics GetStatistics()
    {
        return new ActorStatistics
        {
            ProcessedCount = (int)_totalProcessed,
            FailedCount = (int)_totalFailed,
            AuthenticationFailures = (int)_authenticationFailures,
            AuthorizationFailures = (int)_authorizationFailures
        };
    }

    public async ValueTask DisposeAsync()
    {
        _messageChannel.Writer.Complete();
        _disposalCts.Cancel();

        if (_processingTask != null)
        {
            try { await _processingTask; }
            catch (OperationCanceledException) { }
        }

        _disposalCts.Dispose();
    }
}
