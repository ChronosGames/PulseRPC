using System.Linq;
using Microsoft.CodeAnalysis;
using PulseRPC.Server.SourceGenerator.Models;

namespace PulseRPC.Server.SourceGenerator.Helpers;

/// <summary>
/// 授权信息提取辅助类
/// </summary>
internal static class AuthorizationHelper
{
    /// <summary>
    /// 从符号中提取授权信息
    /// </summary>
    public static AuthorizationModel? GetAuthorization(ISymbol symbol)
    {
        // 检查 AllowAnonymous 特性
        var allowAnonymous = symbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name is "AllowAnonymousAttribute" or "AllowAnonymous");

        if (allowAnonymous)
        {
            return new AuthorizationModel { AllowAnonymous = true };
        }

        // 检查 RequireRole 特性 (优先级高于 Authorize)
        var requireRoleAttr = symbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "RequireRoleAttribute" or "RequireRole");

        if (requireRoleAttr != null)
        {
            return ExtractRequireRoleInfo(requireRoleAttr);
        }

        // 检查 Authorize 特性
        var authorizeAttr = symbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "AuthorizeAttribute" or "Authorize");

        if (authorizeAttr == null)
        {
            return null;
        }

        var authModel = new AuthorizationModel();

        // 提取命名参数
        foreach (var namedArg in authorizeAttr.NamedArguments)
        {
            switch (namedArg.Key)
            {
                case "AuthType":
                    // AuthType 是枚举，需要转换为字符串
                    if (namedArg.Value.Value is int authTypeValue)
                    {
                        authModel.AuthType = authTypeValue switch
                        {
                            0 => "Client",
                            1 => "Service",
                            2 => "Internal",
                            3 => "Any",
                            _ => null
                        };
                    }
                    break;
                case "Role":
                    authModel.Role = namedArg.Value.Value?.ToString();
                    break;
                case "Roles":
                    authModel.Roles = namedArg.Value.Value?.ToString();
                    break;
                case "Policy":
                    authModel.Policy = namedArg.Value.Value?.ToString();
                    break;
                case "Scopes":
                    if (namedArg.Value.Values.Length > 0)
                    {
                        authModel.Scopes = namedArg.Value.Values
                            .Select(v => v.Value?.ToString())
                            .Where(s => s != null)
                            .Cast<string>()
                            .ToArray();
                    }
                    break;
            }
        }

        // 提取构造函数参数
        if (authorizeAttr.ConstructorArguments.Length > 0)
        {
            var firstArg = authorizeAttr.ConstructorArguments[0];

            // 可能是 AuthType 枚举
            if (firstArg.Type?.Name == "AuthType" && firstArg.Value is int authTypeValue)
            {
                authModel.AuthType = authTypeValue switch
                {
                    0 => "Client",
                    1 => "Service",
                    2 => "Internal",
                    3 => "Any",
                    _ => null
                };
            }
            // 可能是 Role 字符串
            else if (firstArg.Type?.SpecialType == SpecialType.System_String && firstArg.Value is string roleOrRoles)
            {
                // 优先赋值给 Role（新的单一角色类型）
                // 如果包含逗号，则是 Roles（传统的角色列表）
                if (roleOrRoles.Contains(','))
                {
                    authModel.Roles = roleOrRoles;
                }
                else
                {
                    authModel.Role = roleOrRoles;
                }
            }
        }

        return authModel;
    }

    /// <summary>
    /// 从 RequireRole 特性中提取授权信息
    /// </summary>
    private static AuthorizationModel ExtractRequireRoleInfo(AttributeData requireRoleAttr)
    {
        var authModel = new AuthorizationModel();

        // 提取构造函数参数 (roles array)
        if (requireRoleAttr.ConstructorArguments.Length > 0)
        {
            var rolesArg = requireRoleAttr.ConstructorArguments[0];

            // 处理 params string[] 参数
            if (rolesArg.Kind == TypedConstantKind.Array && rolesArg.Values.Length > 0)
            {
                var roles = rolesArg.Values
                    .Select(v => v.Value?.ToString())
                    .Where(s => s != null)
                    .Cast<string>()
                    .ToArray();

                authModel.Roles = string.Join(",", roles);
            }
        }

        // 提取命名参数 RequireAll
        var requireAllArg = requireRoleAttr.NamedArguments
            .FirstOrDefault(na => na.Key == "RequireAll");

        if (!requireAllArg.Equals(default(KeyValuePair<string, TypedConstant>)))
        {
            if (requireAllArg.Value.Value is bool requireAll)
            {
                // 存储 RequireAll 信息到 Policy 字段 (复用现有字段)
                authModel.Policy = requireAll ? "RequireAll" : "RequireAny";
            }
        }

        return authModel;
    }
}
