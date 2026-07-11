using System;
using System.Diagnostics.CodeAnalysis;
using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Security;

/// <summary>
/// 源生成授权要求的种类。
/// </summary>
public enum AuthorizationRequirementKind : byte
{
    Role,
    Permission,
    Scope,
}

/// <summary>
/// 单条角色、权限或 scope 要求。
/// </summary>
public readonly struct AuthorizationRequirement
{
    public AuthorizationRequirementKind Kind { get; }
    public string Value { get; }
    public bool AllowInternal { get; }
    public bool AllowSystem { get; }

    public AuthorizationRequirement(
        AuthorizationRequirementKind kind,
        string value,
        bool allowInternal = false,
        bool allowSystem = false)
    {
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        AllowInternal = allowInternal;
        AllowSystem = allowSystem;
    }
}

/// <summary>
/// 编译期生成的单个 Hub 方法有效授权策略。
/// </summary>
/// <remarks>
/// 数组由生成代码静态创建且不向外暴露；每次调用只传递一个 descriptor 引用，不产生临时集合。
/// </remarks>
public sealed class AuthorizationDescriptor
{
    public static AuthorizationDescriptor None { get; } = new();

    internal AuthorizationRequirement[] Requirements { get; }
    internal string[] Policies { get; }

    public bool AllowAnonymous { get; }
    public bool RequireAuthentication { get; }
    public bool InternalOnly { get; }
    public bool ExternalOnly { get; }

    public AuthorizationDescriptor(
        bool allowAnonymous = false,
        bool requireAuthentication = false,
        bool internalOnly = false,
        bool externalOnly = false,
        AuthorizationRequirement[]? requirements = null,
        string[]? policies = null)
    {
        AllowAnonymous = allowAnonymous;
        RequireAuthentication = requireAuthentication;
        InternalOnly = internalOnly;
        ExternalOnly = externalOnly;
        Requirements = requirements ?? Array.Empty<AuthorizationRequirement>();
        Policies = policies ?? Array.Empty<string>();
    }

    internal bool RequiresContext =>
        RequireAuthentication || InternalOnly || ExternalOnly ||
        Requirements.Length != 0 || Policies.Length != 0;

    internal bool RequiresValidAuthentication =>
        RequireAuthentication || Requirements.Length != 0 || Policies.Length != 0;
}

/// <summary>
/// 解析应用自定义的 <see cref="PulseRPC.AuthorizeAttribute.Policy"/>。
/// </summary>
public interface IPulseAuthorizationPolicyEvaluator
{
    /// <summary>返回当前调用上下文是否满足指定策略。</summary>
    bool Evaluate(string policy, IPulseContext context);
}

/// <summary>
/// 所有源生成协议路由共同经过的授权强制点。
/// </summary>
public static class AuthorizationGate
{
    /// <summary>
    /// 在反序列化请求或解析服务实例前强制执行有效授权策略。
    /// </summary>
    public static void Enforce(
        IServiceProvider serviceProvider,
        AuthorizationDescriptor descriptor,
        ushort protocolId,
        string methodDisplayName)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!descriptor.RequiresContext)
        {
            return;
        }

        var context = PulseContext.Current;
        if (context is null)
        {
            Deny(protocolId, methodDisplayName, "request context is unavailable");
        }

        if (descriptor.InternalOnly && context.SourceType != CallSourceType.InternalService)
        {
            Deny(protocolId, methodDisplayName, "the method only accepts internal service calls");
        }

        if (descriptor.ExternalOnly && context.SourceType != CallSourceType.ExternalUser)
        {
            Deny(protocolId, methodDisplayName, "the method only accepts external user calls");
        }

        if (descriptor.RequiresValidAuthentication && context.IsExpired)
        {
            Deny(protocolId, methodDisplayName, "the authentication context has expired");
        }

        if (descriptor.RequireAuthentication && !IsAuthenticated(context))
        {
            Deny(protocolId, methodDisplayName, "authentication is required");
        }

        foreach (var requirement in descriptor.Requirements)
        {
            if (requirement.AllowInternal && context.SourceType == CallSourceType.InternalService)
            {
                continue;
            }

            if (requirement.AllowSystem && context.SourceType == CallSourceType.SystemTimer)
            {
                continue;
            }

            var satisfied = requirement.Kind switch
            {
                AuthorizationRequirementKind.Role => HasRole(context, requirement.Value),
                AuthorizationRequirementKind.Permission => HasPermission(context, requirement.Value),
                AuthorizationRequirementKind.Scope => HasPermission(context, requirement.Value),
                _ => false,
            };

            if (!satisfied)
            {
                Deny(protocolId, methodDisplayName,
                    $"missing required {requirement.Kind.ToString().ToLowerInvariant()} '{requirement.Value}'");
            }
        }

        if (descriptor.Policies.Length == 0)
        {
            return;
        }

        var evaluator = serviceProvider.GetService(typeof(IPulseAuthorizationPolicyEvaluator))
            as IPulseAuthorizationPolicyEvaluator;
        if (evaluator is null)
        {
            Deny(protocolId, methodDisplayName,
                "an authorization policy was declared but no IPulseAuthorizationPolicyEvaluator is registered");
        }

        foreach (var policy in descriptor.Policies)
        {
            if (!evaluator.Evaluate(policy, context))
            {
                Deny(protocolId, methodDisplayName, $"authorization policy '{policy}' was not satisfied");
            }
        }
    }

    private static bool IsAuthenticated(IPulseContext context)
    {
        if (context.SourceType is CallSourceType.InternalService or CallSourceType.SystemTimer or CallSourceType.AdminConsole)
        {
            return true;
        }

        return context.AuthenticationContext?.IsAuthenticated == true ||
               context.User?.Identity?.IsAuthenticated == true ||
               !string.IsNullOrEmpty(context.UserId);
    }

    private static bool HasRole(IPulseContext context, string role)
    {
        if (string.Equals(role, RoleTypes.External, StringComparison.Ordinal))
        {
            return context.SourceType == CallSourceType.ExternalUser;
        }

        if (string.Equals(role, RoleTypes.Internal, StringComparison.Ordinal))
        {
            return context.SourceType == CallSourceType.InternalService;
        }

        if (string.Equals(role, RoleTypes.System, StringComparison.Ordinal))
        {
            return context.SourceType == CallSourceType.SystemTimer;
        }

        return context.HasRole(role) || context.User?.IsInRole(role) == true;
    }

    private static bool HasPermission(IPulseContext context, string permission)
    {
        if (context.HasPermission(permission) || context.AuthenticationContext?.HasScope(permission) == true)
        {
            return true;
        }

        var principal = context.User;
        if (principal is not null)
        {
            foreach (var claim in principal.Claims)
            {
                if ((claim.Type == "permission" || claim.Type == "scope") &&
                    ContainsSpaceDelimitedValue(claim.Value, permission))
                {
                    return true;
                }
            }
        }

        if (context.Claims.TryGetValue("permission", out var permissionClaim) &&
            ContainsSpaceDelimitedValue(permissionClaim, permission))
        {
            return true;
        }

        return context.Claims.TryGetValue("scope", out var scopeClaim) &&
               ContainsSpaceDelimitedValue(scopeClaim, permission);
    }

    private static bool ContainsSpaceDelimitedValue(string values, string expected)
    {
        var remaining = values.AsSpan();
        while (!remaining.IsEmpty)
        {
            var separator = remaining.IndexOf(' ');
            var value = separator < 0 ? remaining : remaining[..separator];
            if (value.Equals(expected.AsSpan(), StringComparison.Ordinal))
            {
                return true;
            }

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..].TrimStart();
        }

        return false;
    }

    [DoesNotReturn]
    private static void Deny(ushort protocolId, string methodDisplayName, string reason)
    {
        throw new UnauthorizedAccessException(
            $"Authorization denied for '{methodDisplayName}' (protocol 0x{protocolId:X4}): {reason}.");
    }
}
