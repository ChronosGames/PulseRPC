using System.Diagnostics;
using System.Security.Claims;
using PulseRPC.Authentication;
using PulseRPC.Server.Health; using PulseRPC.Server.Processing; using PulseRPC.Server.Channels; using PulseRPC.Server.Services; using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Shared;

namespace PulseRPC.Server.Contexts;

/// <summary>
/// Request context data that combines RPC, authentication, and transport information
/// into a single source of truth for all request context information.
/// </summary>
public sealed record class PulseContextData : IPulseContext
{
    private readonly CancellationTokenSource? _cts;
    private bool _disposed;

    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata =
        new Dictionary<string, string>();

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>();

    // ═══════════════════════════════════════════════════════════════════════════
    // RPC Information
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public Guid RequestId { get; init; }

    /// <inheritdoc/>
    public string? ConnectionId { get; init; }

    /// <inheritdoc/>
    public string ServiceName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string ServiceKey { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string MethodName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = EmptyMetadata;

    /// <inheritdoc/>
    public CancellationToken CancellationToken { get; init; }

    /// <inheritdoc/>
    public long StartTimestamp { get; init; }

    /// <inheritdoc/>
    public ActivityContext TraceContext { get; init; }

    /// <inheritdoc/>
    public ClaimsPrincipal? User { get; init; }

    /// <inheritdoc/>
    public bool IsCancelled => CancellationToken.IsCancellationRequested;

    // ═══════════════════════════════════════════════════════════════════════════
    // Identity Information (from IServiceRequestContext)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public CallSourceType SourceType { get; init; } = CallSourceType.ExternalUser;

    /// <inheritdoc/>
    public string CallerId { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string? UserId { get; init; }

    /// <inheritdoc/>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <inheritdoc/>
    public string? Token { get; init; }

    /// <inheritdoc/>
    public IReadOnlySet<string> Permissions { get; init; } = EmptySet;

    /// <inheritdoc/>
    public IReadOnlySet<string> Roles { get; init; } = EmptySet;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Claims { get; init; } = EmptyMetadata;

    /// <inheritdoc/>
    public string? IpAddress { get; init; }

    /// <inheritdoc/>
    public DateTime AuthenticatedAt { get; init; } = DateTime.UtcNow;

    /// <inheritdoc/>
    public DateTime? ExpiresAt { get; init; }

    /// <inheritdoc/>
    public IAuthenticationContext? AuthenticationContext { get; init; }

    /// <inheritdoc/>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    // ═══════════════════════════════════════════════════════════════════════════
    // Transport Information
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public IServerTransport? Transport { get; init; }

    /// <inheritdoc/>
    public string? ClientAddress => Transport?.RemoteEndPoint?.ToString() ?? IpAddress;

    // ═══════════════════════════════════════════════════════════════════════════
    // Custom Properties
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Custom properties dictionary for storing additional context data.
    /// </summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();

    // ═══════════════════════════════════════════════════════════════════════════
    // Constructor
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new PulseContextData instance.
    /// </summary>
    public PulseContextData()
    {
    }

    /// <summary>
    /// Creates a new PulseContextData instance with a CancellationTokenSource for timeout management.
    /// </summary>
    private PulseContextData(CancellationTokenSource? cts)
    {
        _cts = cts;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public TimeSpan GetElapsedTime()
    {
        if (StartTimestamp == 0)
            return TimeSpan.Zero;

        var elapsed = Stopwatch.GetTimestamp() - StartTimestamp;
        return TimeSpan.FromTicks((long)(elapsed * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency)));
    }

    /// <inheritdoc/>
    public string? GetHeader(string name)
    {
        return Metadata.TryGetValue(name, out var value) ? value : null;
    }

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

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        _cts?.Dispose();
        _disposed = true;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Factory Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a new PulseContextData from an RPC message and transport.
    /// </summary>
    /// <param name="message">The RPC message</param>
    /// <param name="transport">The server transport</param>
    /// <param name="authContext">Optional authentication context</param>
    /// <param name="timeout">Request timeout</param>
    /// <param name="activity">Optional distributed tracing activity</param>
    /// <returns>A new PulseContextData instance</returns>
    public static PulseContextData Create(
        RpcMessage message,
        IServerTransport? transport,
        IAuthenticationContext? authContext,
        TimeSpan timeout,
        Activity? activity = null)
    {
        var cts = new CancellationTokenSource(timeout);

        var permissions = new HashSet<string>();
        var roles = new HashSet<string>();
        var claims = new Dictionary<string, string>();

        // Extract permissions from auth context
        if (authContext?.Scopes != null)
        {
            foreach (var scope in authContext.Scopes)
            {
                permissions.Add(scope);
            }
        }

        // Extract roles from principal
        if (authContext?.Principal != null)
        {
            foreach (var claim in authContext.Principal.Claims)
            {
                if (claim.Type == ClaimTypes.Role)
                {
                    roles.Add(claim.Value);
                }
                else
                {
                    claims[claim.Type] = claim.Value;
                }
            }
        }

        return new PulseContextData(cts)
        {
            // RPC info
            RequestId = message.RequestId,
            ConnectionId = transport?.Id,
            ServiceName = message.ServiceName,
            MethodName = message.MethodName,
            Metadata = message.Metadata ?? EmptyMetadata,
            CancellationToken = cts.Token,
            StartTimestamp = Stopwatch.GetTimestamp(),
            TraceContext = activity?.Context ?? default,
            User = authContext?.Principal,

            // Identity info
            SourceType = authContext?.IsAuthenticated == true
                ? CallSourceType.ExternalUser
                : CallSourceType.InternalService,
            CallerId = authContext?.Identity ?? transport?.Id ?? "Unknown",
            UserId = authContext?.Identity,
            Token = authContext?.Token,
            Permissions = permissions,
            Roles = roles,
            Claims = claims,
            IpAddress = transport?.RemoteEndPoint?.ToString(),
            AuthenticatedAt = authContext?.AuthenticationTime ?? DateTime.UtcNow,
            AuthenticationContext = authContext,

            // Transport
            Transport = transport
        };
    }

    /// <summary>
    /// Creates a user context for authenticated users.
    /// </summary>
    public static PulseContextData CreateUserContext(
        string userId,
        string? connectionId = null,
        IReadOnlySet<string>? permissions = null,
        IReadOnlySet<string>? roles = null,
        string? token = null,
        DateTime? expiresAt = null,
        IServerTransport? transport = null)
    {
        return new PulseContextData
        {
            SourceType = CallSourceType.ExternalUser,
            UserId = userId,
            CallerId = userId,
            ConnectionId = connectionId ?? transport?.Id,
            Permissions = permissions ?? EmptySet,
            Roles = roles ?? EmptySet,
            Token = token,
            ExpiresAt = expiresAt,
            Transport = transport,
            StartTimestamp = Stopwatch.GetTimestamp()
        };
    }

    /// <summary>
    /// Creates a service context for service-to-service calls.
    /// </summary>
    public static PulseContextData CreateServiceContext(
        string serviceType,
        string serviceId,
        string? token = null,
        IServerTransport? transport = null)
    {
        return new PulseContextData
        {
            SourceType = CallSourceType.InternalService,
            CallerId = $"{serviceType}:{serviceId}",
            Token = token,
            Permissions = new HashSet<string> { "*" },
            Roles = new HashSet<string> { "Service" },
            Transport = transport,
            StartTimestamp = Stopwatch.GetTimestamp()
        };
    }

    /// <summary>
    /// Creates a system context for timer tasks and system operations.
    /// </summary>
    public static PulseContextData CreateSystemContext(string taskName = "SystemTask")
    {
        return new PulseContextData
        {
            SourceType = CallSourceType.SystemTimer,
            CallerId = $"System:{taskName}",
            StartTimestamp = Stopwatch.GetTimestamp()
        };
    }

    /// <summary>
    /// Creates a context from an authentication context.
    /// </summary>
    public static PulseContextData FromAuthenticationContext(
        IAuthenticationContext authContext,
        IServerTransport? transport = null)
    {
        var permissions = new HashSet<string>();
        var roles = new HashSet<string>();

        if (authContext.Scopes != null)
        {
            foreach (var scope in authContext.Scopes)
            {
                permissions.Add(scope);
            }
        }

        if (authContext.Principal != null)
        {
            foreach (var claim in authContext.Principal.Claims)
            {
                if (claim.Type == ClaimTypes.Role)
                {
                    roles.Add(claim.Value);
                }
            }
        }

        return new PulseContextData
        {
            SourceType = CallSourceType.ExternalUser,
            CallerId = authContext.Identity ?? "Unknown",
            UserId = authContext.Identity,
            Token = authContext.Token,
            AuthenticatedAt = authContext.AuthenticationTime ?? DateTime.UtcNow,
            AuthenticationContext = authContext,
            Permissions = permissions,
            Roles = roles,
            User = authContext.Principal,
            Transport = transport,
            ConnectionId = transport?.Id,
            StartTimestamp = Stopwatch.GetTimestamp()
        };
    }
}
