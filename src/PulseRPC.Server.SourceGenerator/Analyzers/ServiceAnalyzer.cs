using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PulseRPC.Server.SourceGenerator.Models;

namespace PulseRPC.Server.SourceGenerator.Analyzers;

/// <summary>
/// 服务接口语法分析器
/// </summary>
public static class ServiceAnalyzer
{
    /// <summary>
    /// 分析接口声明，提取服务元数据
    /// </summary>
    public static ServiceModel? AnalyzeInterface(InterfaceDeclarationSyntax interfaceDeclaration,
        SemanticModel semanticModel)
    {
        var interfaceSymbol = semanticModel.GetDeclaredSymbol(interfaceDeclaration) as INamedTypeSymbol;
        if (interfaceSymbol == null) return null;

        // 检查是否标记为PulseHub或继承IPulseHub
        if (!HasPulseServiceAttribute(interfaceSymbol) && !InheritsFromIPulseHub(interfaceSymbol))
            return null;

        var interfaceName = interfaceSymbol.Name;
        var interfaceFullName = interfaceSymbol.ToDisplayString();
        var namespaceName = interfaceSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var channelName = GetChannelName(interfaceSymbol) ?? "DefaultChannel";
        var serviceName = GetServiceName(interfaceSymbol) ?? interfaceName;

        var methods = new List<MethodModel>();

        // 分析接口方法
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
            {
                var methodModel = AnalyzeMethod(methodSymbol);

                if (methodModel != null)
                    methods.Add(methodModel);
            }
        }

        return new ServiceModel
        {
            InterfaceName = interfaceName,
            InterfaceFullName = interfaceFullName,
            Namespace = namespaceName,
            ChannelName = channelName,
            ServiceName = serviceName,
            Methods = methods
        };
    }

    /// <summary>
    /// 检查接口是否有PulseService特性
    /// </summary>
    private static bool HasPulseServiceAttribute(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name is "PulseServiceAttribute" or "PulseService");
    }

    /// <summary>
    /// 检查接口是否继承IPulseHub
    /// </summary>
    private static bool InheritsFromIPulseHub(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.AllInterfaces
            .Any(i => i.Name == "IPulseHub" ||
                     i.ToDisplayString().EndsWith(".IPulseHub"));
    }

    /// <summary>
    /// 获取通道名称
    /// </summary>
    private static string? GetChannelName(INamedTypeSymbol interfaceSymbol)
    {
        var channelAttr = interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "ChannelAttribute" or "Channel");

        if (channelAttr?.ConstructorArguments.Length > 0)
        {
            var channelName = channelAttr.ConstructorArguments[0].Value?.ToString();
            return channelName;
        }

        return null;
    }

    /// <summary>
    /// 获取服务名称（用于线程调度）
    /// </summary>
    private static string? GetServiceName(INamedTypeSymbol interfaceSymbol)
    {
        var channelAttr = interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "ChannelAttribute" or "Channel");

        if (channelAttr != null)
        {
            // 检查命名参数中是否有 ServiceName
            var serviceNameArg = channelAttr.NamedArguments
                .FirstOrDefault(na => na.Key == "ServiceName");

            if (!serviceNameArg.Equals(default(KeyValuePair<string, TypedConstant>)))
            {
                var serviceName = serviceNameArg.Value.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(serviceName))
                    return serviceName;
            }

            // 检查构造函数的第二个参数（如果存在）
            if (channelAttr.ConstructorArguments.Length > 1)
            {
                var serviceName = channelAttr.ConstructorArguments[1].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(serviceName))
                    return serviceName;
            }
        }

        return null;
    }

    /// <summary>
    /// 分析方法元数据
    /// </summary>
    private static MethodModel? AnalyzeMethod(IMethodSymbol methodSymbol)
    {
        var returnType = methodSymbol.ReturnType;
        var returnTypeName = GetReturnTypeName(returnType);
        var returnTypeFullName = returnType.ToDisplayString();

        var isAsync = IsAsyncMethod(returnType);
        var isGenericTask = IsGenericTaskType(returnType);
        var responseTypeFullName = GetResponseTypeFullName(returnType);
        var isResponseMemoryPackable = responseTypeFullName != null && IsMemoryPackable(returnType, responseTypeFullName);

        var parameters = new List<ParameterModel>();

        foreach (var param in methodSymbol.Parameters)
        {
            var paramModel = new ParameterModel
            {
                Name = param.Name,
                TypeName = param.Type.Name,
                TypeFullName = param.Type.ToDisplayString(),
                IsMemoryPackable = IsMemoryPackable(param.Type)
            };
            parameters.Add(paramModel);
        }

        var methodChannelName = GetMethodChannelName(methodSymbol);

        return new MethodModel
        {
            MethodName = methodSymbol.Name,
            ReturnTypeName = returnTypeName,
            ReturnTypeFullName = returnTypeFullName,
            Parameters = parameters,
            IsAsync = isAsync,
            IsGenericTask = isGenericTask,
            ChannelName = methodChannelName,
            ResponseTypeFullName = responseTypeFullName,
            IsResponseMemoryPackable = responseTypeFullName != null,
        };
    }

    /// <summary>
    /// 获取方法的通道名称
    /// </summary>
    private static string? GetMethodChannelName(IMethodSymbol methodSymbol)
    {
        var channelAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name is "ChannelAttribute" or "Channel");

        if (channelAttr?.ConstructorArguments.Length > 0)
        {
            return channelAttr.ConstructorArguments[0].Value?.ToString();
        }

        return null;
    }

    /// <summary>
    /// 获取返回类型名称（去除Task/ValueTask包装）
    /// </summary>
    private static string GetReturnTypeName(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType)
        {
            // 处理 Task<T> 和 ValueTask<T>
            if (namedType.IsGenericType && namedType.ConstructedFrom.Name is "Task" or "ValueTask")
            {
                if (namedType.TypeArguments.Length > 0)
                {
                    return namedType.TypeArguments[0].Name;
                }
                return "void";
            }
        }

        return returnType.Name;
    }

    /// <summary>
    /// 判断是否为异步方法
    /// </summary>
    private static bool IsAsyncMethod(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType)
        {
            var typeName = namedType.ConstructedFrom?.Name ?? namedType.Name;
            return typeName is "Task" or "ValueTask";
        }
        return false;
    }

    /// <summary>
    /// 判断是否为泛型Task类型
    /// </summary>
    private static bool IsGenericTaskType(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var typeName = namedType.ConstructedFrom.Name;
            return typeName is "Task" or "ValueTask" && namedType.TypeArguments.Length > 0;
        }
        return false;
    }

    /// <summary>
    /// 检查类型是否标记为MemoryPackable
    /// </summary>
    private static bool IsMemoryPackable(ITypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name is "MemoryPackableAttribute" or "MemoryPackable");
    }

    private static bool IsMemoryPackable(ITypeSymbol returnType, string responseTypeFullName)
    {
        if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
        {
            var typeArg = namedType.TypeArguments[0];
            return IsMemoryPackable(typeArg);
        }

        if (returnType.ToDisplayString() == responseTypeFullName)
        {
            return IsMemoryPackable(returnType);
        }

        return false;
    }

    private static string? GetResponseTypeFullName(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType)
        {
            // 处理 Task<T> 和 ValueTask<T>
            if (namedType.IsGenericType && namedType.ConstructedFrom.Name is "Task" or "ValueTask")
            {
                return namedType.TypeArguments.Length > 0 ? namedType.TypeArguments[0].ToDisplayString() : null;
            }

            // 处理非泛型 Task 和 ValueTask
            if (namedType.Name is "Task" or "ValueTask" && !namedType.IsGenericType)
            {
                return null;
            }
        }

        // 其他同步返回类型
        return returnType.ToDisplayString();


        // if (returnType is INamedTypeSymbol namedType)
        // {
        //     if (namedType.IsGenericType && namedType.ConstructedFrom.Name is "Task" or "ValueTask")
        //     {
        //         return namedType.TypeArguments.Length > 0 ? namedType.TypeArguments[0].ToDisplayString() : null;
        //     }
        //
        //     if (!namedType.IsGenericType)
        //     {
        //         return returnType.SpecialType == SpecialType.System_Void ? null : returnType.ToDisplayString();
        //     }
        // }
        //
        // return returnType.ToDisplayString() == "void" ? null : returnType.ToDisplayString();
    }

    /// <summary>
    /// 查找项目中所有的PulseService接口
    /// </summary>
    public static IEnumerable<InterfaceDeclarationSyntax> FindPulseServiceInterfaces(
        IEnumerable<SyntaxNode> syntaxNodes)
    {
        return syntaxNodes
            .OfType<InterfaceDeclarationSyntax>()
            .Where(i => HasPulseServiceAttributeSyntax(i));
    }

    /// <summary>
    /// 检查接口语法是否包含PulseService特性或继承IPulseHub
    /// </summary>
    private static bool HasPulseServiceAttributeSyntax(InterfaceDeclarationSyntax interfaceDeclaration)
    {
        // 检查特性
        var hasAttribute = interfaceDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr =>
            {
                var name = attr.Name.ToString();
                return name == "PulseService" ||
                       name == "PulseServiceAttribute" ||
                       name.EndsWith("PulseService") ||
                       name.EndsWith("PulseServiceAttribute");
            });

        if (hasAttribute)
            return true;

        // 检查是否继承IPulseHub接口
        if (interfaceDeclaration.BaseList?.Types != null)
        {
            return interfaceDeclaration.BaseList.Types
                .Any(baseType =>
                {
                    var typeName = baseType.Type.ToString();
                    return typeName == "IPulseHub" ||
                           typeName.EndsWith(".IPulseHub") ||
                           typeName.Contains("IPulseHub");
                });
        }

        return false;
    }
}
