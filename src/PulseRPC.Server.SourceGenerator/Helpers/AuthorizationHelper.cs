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
                case "Role":
                    authModel.Role = namedArg.Value.Value?.ToString();
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

        // 提取构造函数参数 (Role 字符串)
        if (authorizeAttr.ConstructorArguments.Length > 0)
        {
            var firstArg = authorizeAttr.ConstructorArguments[0];

            if (firstArg.Type?.SpecialType == SpecialType.System_String && firstArg.Value is string role)
            {
                authModel.Role = role;
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

                // 使用逗号分隔的角色列表存储到 Role
                authModel.Role = string.Join(",", roles);
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
