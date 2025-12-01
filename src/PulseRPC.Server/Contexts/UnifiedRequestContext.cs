using System.Runtime.CompilerServices;
using PulseRPC.Authentication;

namespace PulseRPC.Server.Contexts;

/// <summary>
/// 统一的请求上下文 - 合并 RequestContext 和 ServiceRequestContextProvider 的功能
/// </summary>
/// <remarks>
/// <para>
/// <strong>设计说明</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>替代原有的两套上下文系统（RequestContext 和 ServiceRequestContextProvider）</description></item>
/// <item><description>使用 AsyncLocal 保证异步流程安全</description></item>
/// <item><description>支持嵌套上下文（通过 Scope 自动恢复）</description></item>
/// <item><description>提供便捷的静态访问方法</description></item>
/// </list>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// // 设置上下文
/// using (UnifiedRequestContext.SetContext(new UnifiedContextData { UserId = "user-123" }))
/// {
///     // 在此范围内访问上下文
///     var userId = UnifiedRequestContext.Current?.UserId;
///
///     // 嵌套范围
///     using (UnifiedRequestContext.SetContext(new UnifiedContextData { UserId = "inner-user" }))
///     {
///         // 这里 UserId = "inner-user"
///     }
///     // 恢复为 "user-123"
/// }
/// </code>
/// </remarks>
public static class UnifiedRequestContext
{
    private static readonly AsyncLocal<UnifiedContextData?> _current = new();

    /// <summary>
    /// 当前请求上下文
    /// </summary>
    public static UnifiedContextData? Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>
    /// 当前用户 ID（便捷访问）
    /// </summary>
    public static string? CurrentUserId => _current.Value?.UserId;

    /// <summary>
    /// 当前连接 ID（便捷访问）
    /// </summary>
    public static string? CurrentConnectionId => _current.Value?.ConnectionId;

    /// <summary>
    /// 当前调用者 ID（便捷访问）
    /// </summary>
    public static string? CurrentCallerId => _current.Value?.CallerId;

    /// <summary>
    /// 是否存在有效上下文
    /// </summary>
    public static bool HasContext => _current.Value != null;

    /// <summary>
    /// 确保有请求上下文，否则抛出异常
    /// </summary>
    /// <exception cref="InvalidOperationException">当没有上下文时抛出</exception>
    public static UnifiedContextData RequireCurrent()
    {
        return _current.Value ?? throw new InvalidOperationException(
            "No request context available. Ensure the request is being processed within a valid context scope.");
    }

    /// <summary>
    /// 设置上下文并返回 Scope 用于自动清理
    /// </summary>
    /// <param name="context">上下文数据</param>
    /// <returns>Disposable scope，用于 using 语句</returns>
    public static ContextScope SetContext(UnifiedContextData context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new ContextScope(previous);
    }

    /// <summary>
    /// 从现有的 IServiceRequestContext 创建上下文
    /// </summary>
    public static ContextScope SetContext(IServiceRequestContext serviceContext)
    {
        var contextData = UnifiedContextData.FromServiceRequestContext(serviceContext);
        return SetContext(contextData);
    }

    /// <summary>
    /// 清除当前上下文
    /// </summary>
    public static void Clear()
    {
        _current.Value = null;
    }

    /// <summary>
    /// 上下文 Scope - 用于自动恢复之前的上下文
    /// </summary>
    public readonly struct ContextScope : IDisposable
    {
        private readonly UnifiedContextData? _previous;

        internal ContextScope(UnifiedContextData? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            _current.Value = _previous;
        }
    }
}

/// <summary>
/// 统一的上下文数据 - 包含请求处理所需的所有信息
/// </summary>
public sealed class UnifiedContextData : IServiceRequestContext
{
    // ═══════════════════════════════════════════════════════════════════════════
    // 基础标识信息
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 连接 ID
    /// </summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// 用户 ID（已认证的用户）
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// 调用者 ID（可能是用户 ID 或服务 PID）
    /// </summary>
    public string CallerId { get; init; } = string.Empty;

    /// <summary>
    /// 会话 ID
    /// </summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    // ═══════════════════════════════════════════════════════════════════════════
    // 调用来源信息
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 调用来源类型
    /// </summary>
    public CallSourceType SourceType { get; init; } = CallSourceType.ExternalUser;

    /// <summary>
    /// 服务 PID（内部服务调用时有值）
    /// </summary>
    public PID? ServicePID { get; init; }

    /// <summary>
    /// 认证 Token
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// IP 地址
    /// </summary>
    public string? IpAddress { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // 权限和认证信息
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 权限列表
    /// </summary>
    public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>();

    /// <summary>
    /// 角色列表
    /// </summary>
    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>();

    /// <summary>
    /// 额外声明
    /// </summary>
    public IReadOnlyDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// 认证时间
    /// </summary>
    public DateTime AuthenticatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// 底层认证上下文
    /// </summary>
    public IAuthenticationContext? AuthenticationContext { get; init; }

    // ═══════════════════════════════════════════════════════════════════════════
    // 请求头和自定义属性
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 请求头
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// 自定义属性（可变）
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    // ═══════════════════════════════════════════════════════════════════════════
    // 方法实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    /// <inheritdoc/>
    public bool HasPermission(string permission)
    {
        return Permissions.Contains(permission);
    }

    /// <inheritdoc/>
    public bool HasRole(string role)
    {
        return Roles.Contains(role);
    }

    /// <inheritdoc/>
    public bool HasAnyPermission(params string[] permissions)
    {
        return permissions.Any(p => Permissions.Contains(p));
    }

    /// <inheritdoc/>
    public bool HasAllPermissions(params string[] permissions)
    {
        return permissions.All(p => Permissions.Contains(p));
    }

    /// <summary>
    /// 获取请求头
    /// </summary>
    public string? GetHeader(string name)
    {
        if (Headers == null) return null;
        return Headers.TryGetValue(name, out var value) ? value : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 工厂方法
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从 IServiceRequestContext 创建
    /// </summary>
    public static UnifiedContextData FromServiceRequestContext(IServiceRequestContext context)
    {
        return new UnifiedContextData
        {
            SourceType = context.SourceType,
            CallerId = context.CallerId,
            UserId = context.UserId,
            ServicePID = context.ServicePID,
            Token = context.Token,
            Permissions = context.Permissions,
            Roles = context.Roles,
            Claims = context.Claims,
            IpAddress = context.IpAddress,
            AuthenticatedAt = context.AuthenticatedAt,
            ExpiresAt = context.ExpiresAt,
            SessionId = context.SessionId,
            AuthenticationContext = context.AuthenticationContext
        };
    }

    /// <summary>
    /// 创建用户上下文
    /// </summary>
    public static UnifiedContextData CreateUserContext(
        string userId,
        string? connectionId = null,
        IReadOnlySet<string>? permissions = null,
        IReadOnlySet<string>? roles = null)
    {
        return new UnifiedContextData
        {
            SourceType = CallSourceType.ExternalUser,
            UserId = userId,
            CallerId = userId,
            ConnectionId = connectionId,
            Permissions = permissions ?? new HashSet<string>(),
            Roles = roles ?? new HashSet<string>()
        };
    }

    /// <summary>
    /// 创建服务上下文（用于服务间调用）
    /// </summary>
    public static UnifiedContextData CreateServiceContext(PID servicePID, string? token = null)
    {
        return new UnifiedContextData
        {
            SourceType = CallSourceType.InternalService,
            ServicePID = servicePID,
            CallerId = servicePID.ToString(),
            Token = token
        };
    }

    /// <summary>
    /// 创建系统上下文（用于定时器等系统任务）
    /// </summary>
    public static UnifiedContextData CreateSystemContext(string taskName = "SystemTask")
    {
        return new UnifiedContextData
        {
            SourceType = CallSourceType.SystemTimer,
            CallerId = $"System:{taskName}"
        };
    }
}

