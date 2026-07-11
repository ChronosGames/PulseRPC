using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using PulseRPC.Server.SourceGenerator.Models;

namespace PulseRPC.Server.SourceGenerator.Helpers;

/// <summary>
/// 提取并合并 Hub 方法的声明式授权策略。
/// </summary>
internal static class AuthorizationHelper
{
    private static readonly string[] AuthorizeAttributeNames =
    {
        "PulseRPC.AuthorizeAttribute",
    };

    private static readonly string[] AllowAnonymousAttributeNames =
    {
        "PulseRPC.AllowAnonymousAttribute",
    };

    private static readonly string[] RequireRoleAttributeNames =
    {
        "PulseRPC.Server.RequireRoleAttribute",
        "PulseRPC.RequireRoleAttribute",
    };

    private static readonly string[] RequirePermissionAttributeNames =
    {
        "PulseRPC.Server.RequirePermissionAttribute",
        "PulseRPC.RequirePermissionAttribute",
    };

    private static readonly string[] InternalAttributeNames =
    {
        "PulseRPC.Server.InternalAttribute",
        "PulseRPC.InternalAttribute",
    };

    private static readonly string[] ExternalOnlyAttributeNames =
    {
        "PulseRPC.Server.ExternalOnlyAttribute",
        "PulseRPC.ExternalOnlyAttribute",
    };

    /// <summary>读取单个符号直接声明的授权元数据。</summary>
    public static AuthorizationModel? GetAuthorization(ISymbol symbol)
    {
        var model = new AuthorizationModel();

        foreach (var attribute in symbol.GetAttributes())
        {
            if (Matches(attribute, AllowAnonymousAttributeNames))
            {
                model.AllowAnonymous = true;
                continue;
            }

            if (Matches(attribute, AuthorizeAttributeNames))
            {
                ExtractAuthorize(attribute, model);
                continue;
            }

            if (Matches(attribute, RequireRoleAttributeNames))
            {
                AddRequirement(attribute, model, AuthorizationRequirementKindModel.Role);
                continue;
            }

            if (Matches(attribute, RequirePermissionAttributeNames))
            {
                AddRequirement(attribute, model, AuthorizationRequirementKindModel.Permission);
                continue;
            }

            if (Matches(attribute, InternalAttributeNames))
            {
                model.InternalOnly = true;
                continue;
            }

            if (Matches(attribute, ExternalOnlyAttributeNames))
            {
                model.ExternalOnly = true;
            }
        }

        return model.IsEmpty ? null : model;
    }

    /// <summary>
    /// 合并路由 Hub 接口、方法声明接口（含其基接口）和方法自身的有效策略。
    /// 方法上的 <c>[AllowAnonymous]</c> 只取消“必须已认证”要求，不移除角色、权限、来源或 Policy 要求。
    /// </summary>
    public static AuthorizationModel? GetEffectiveAuthorization(
        INamedTypeSymbol serviceInterface,
        IMethodSymbol method)
    {
        var effective = new AuthorizationModel();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var baseInterface in method.ContainingType.AllInterfaces.Reverse())
        {
            MergeInterface(baseInterface);
        }

        MergeInterface(method.ContainingType);
        MergeInterface(serviceInterface);

        var methodAuthorization = GetAuthorization(method);
        Merge(effective, methodAuthorization);

        if (methodAuthorization?.AllowAnonymous == true)
        {
            effective.AllowAnonymous = true;
            effective.RequireAuthentication = false;
        }

        return effective.IsEmpty ? null : effective;

        void MergeInterface(INamedTypeSymbol interfaceSymbol)
        {
            if (visited.Add(interfaceSymbol))
            {
                Merge(effective, GetAuthorization(interfaceSymbol));
            }
        }
    }

    private static void ExtractAuthorize(AttributeData attribute, AuthorizationModel model)
    {
        model.RequireAuthentication = true;

        string? role = null;
        string? policy = null;
        string[]? scopes = null;

        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is string constructorRole)
        {
            role = constructorRole;
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "Role":
                    role = namedArgument.Value.Value as string;
                    break;
                case "Policy":
                    policy = namedArgument.Value.Value as string;
                    break;
                case "Scopes":
                    scopes = ReadStringArray(namedArgument.Value);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            model.Role = role;
            AddRequirement(model, AuthorizationRequirementKindModel.Role, role!, false, false);
        }

        if (!string.IsNullOrWhiteSpace(policy))
        {
            model.Policy = policy;
            AddPolicy(model, policy!);
        }

        if (scopes is { Length: > 0 })
        {
            model.Scopes = scopes;
            foreach (var scope in scopes)
            {
                if (!string.IsNullOrWhiteSpace(scope))
                {
                    AddRequirement(model, AuthorizationRequirementKindModel.Scope, scope, false, false);
                }
            }
        }
    }

    private static void AddRequirement(
        AttributeData attribute,
        AuthorizationModel model,
        AuthorizationRequirementKindModel kind)
    {
        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not string value ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var allowInternal = ReadBooleanNamedArgument(attribute, "AllowInternal", defaultValue: true);
        var allowSystem = ReadBooleanNamedArgument(attribute, "AllowSystem", defaultValue: true);
        model.RequireAuthentication = true;
        AddRequirement(model, kind, value, allowInternal, allowSystem);
    }

    private static void AddRequirement(
        AuthorizationModel model,
        AuthorizationRequirementKindModel kind,
        string value,
        bool allowInternal,
        bool allowSystem)
    {
        if (model.Requirements.Any(existing =>
                existing.Kind == kind &&
                string.Equals(existing.Value, value, StringComparison.Ordinal) &&
                existing.AllowInternal == allowInternal &&
                existing.AllowSystem == allowSystem))
        {
            return;
        }

        model.Requirements.Add(new AuthorizationRequirementModel
        {
            Kind = kind,
            Value = value,
            AllowInternal = allowInternal,
            AllowSystem = allowSystem,
        });
    }

    private static void Merge(AuthorizationModel target, AuthorizationModel? source)
    {
        if (source is null)
        {
            return;
        }

        target.AllowAnonymous |= source.AllowAnonymous;
        target.RequireAuthentication |= source.RequireAuthentication;
        target.InternalOnly |= source.InternalOnly;
        target.ExternalOnly |= source.ExternalOnly;

        foreach (var policy in source.Policies)
        {
            AddPolicy(target, policy);
        }

        foreach (var requirement in source.Requirements)
        {
            AddRequirement(target, requirement.Kind, requirement.Value,
                requirement.AllowInternal, requirement.AllowSystem);
        }
    }

    private static void AddPolicy(AuthorizationModel model, string policy)
    {
        if (!model.Policies.Contains(policy, StringComparer.Ordinal))
        {
            model.Policies.Add(policy);
        }
    }

    private static bool Matches(AttributeData attribute, IReadOnlyCollection<string> metadataNames)
    {
        for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
        {
            if (metadataNames.Contains(type.ToDisplayString(), StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadBooleanNamedArgument(
        AttributeData attribute,
        string name,
        bool defaultValue)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == name && namedArgument.Value.Value is bool value)
            {
                return value;
            }
        }

        return defaultValue;
    }

    private static string[]? ReadStringArray(TypedConstant value)
    {
        if (value.Kind != TypedConstantKind.Array)
        {
            return null;
        }

        return value.Values
            .Select(item => item.Value as string)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }
}
