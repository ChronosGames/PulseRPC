using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PulseRPC.Server.SourceGenerator.Helpers;
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

        // 只识别继承 IPulseHub 的接口
        if (!InheritsFromIPulseHub(interfaceSymbol))
            return null;

        // 统一标记模型：[Channel("CLIENT")] 表示客户端实现的推送接收器，不作为服务端可调用服务分析
        // （其服务端 Fan-out 代理由 ReceiverProxyGenerator 生成）。
        if (string.Equals(GetChannelName(interfaceSymbol), ClientChannelConstants.ClientChannelName, System.StringComparison.OrdinalIgnoreCase))
            return null;

        // §5.2-C 显式覆盖：[PulseHub(Provide=false)] 表示本编译侧（服务端）不生成被调方骨架
        if (PulseHubOverrideHelper.TryGetOverride(interfaceSymbol, out var provide, out _) && !provide)
            return null;

        var interfaceName = interfaceSymbol.Name;
        var interfaceFullName = interfaceSymbol.ToDisplayString();
        var namespaceName = interfaceSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var channelName = GetChannelName(interfaceSymbol) ?? "DefaultChannel";
        var serviceName = GetServiceName(interfaceSymbol) ?? interfaceName;
        var facetClientFacing = ClientFacingHelper.GetClientFacing(interfaceSymbol) ?? false;

        var methods = new List<MethodModel>();

        // 分析接口方法（含直接成员 + 继承接口成员，与客户端生成器方法收集范围对齐，见 §11.2 风险 #1）
        foreach (var member in ProtocolIdHelper.GetAllPublicMethods(interfaceSymbol))
        {
            if (member is IMethodSymbol methodSymbol && methodSymbol.MethodKind == MethodKind.Ordinary)
            {
                var methodModel = AnalyzeMethod(interfaceSymbol, methodSymbol, facetClientFacing);

                if (methodModel != null)
                    methods.Add(methodModel);
            }
        }

        var authorization = AuthorizationHelper.GetAuthorization(interfaceSymbol);

        return new ServiceModel
        {
            InterfaceName = interfaceName,
            InterfaceFullName = interfaceFullName,
            Namespace = namespaceName,
            ChannelName = channelName,
            ServiceName = serviceName,
            Methods = methods,
            Authorization = authorization
        };
    }

    /// <summary>
    /// 检查接口是否继承 IPulseHub
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
    private static MethodModel? AnalyzeMethod(
        INamedTypeSymbol serviceInterface,
        IMethodSymbol methodSymbol,
        bool facetClientFacing)
    {
        var returnType = methodSymbol.ReturnType;
        var returnTypeName = GetReturnTypeName(returnType);
        var returnTypeFullName = returnType.ToDisplayString();

        var isAsync = IsAsyncMethod(returnType);

        // 仅支持 Task/ValueTask 返回类型，跳过非异步方法
        if (!isAsync)
        {
            return null;
        }

        var responseTypeFullName = GetResponseTypeFullName(returnType);
        var isGenericTask = IsGenericTaskType(returnType);
        var isResponseMemoryPackable = responseTypeFullName is null || IsMemoryPackSerializableResponse(returnType);

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
        var authorization = AuthorizationHelper.GetEffectiveAuthorization(serviceInterface, methodSymbol);
        var isReentrant = methodSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name is "ReentrantAttribute" or "Reentrant");
        var isClientFacing = ClientFacingHelper.GetClientFacing(methodSymbol) ?? facetClientFacing;

        return new MethodModel
        {
            MethodName = methodSymbol.Name,
            ReturnTypeName = returnTypeName,
            ReturnTypeFullName = returnTypeFullName,
            Parameters = parameters,
            IsAsync = isAsync,
            IsGenericTask = isGenericTask,
            ChannelName = methodChannelName,
            DeclaringInterfaceFullName = methodSymbol.ContainingType.ToDisplayString(),
            ResponseTypeFullName = responseTypeFullName,
            IsResponseMemoryPackable = isResponseMemoryPackable,
            Authorization = authorization,
            IsReentrant = isReentrant,
            IsClientFacing = isClientFacing,
            Location = methodSymbol.Locations.FirstOrDefault()
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

    private static bool IsMemoryPackSerializableResponse(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
        {
            return IsMemoryPackSerializable(namedType.TypeArguments[0]);
        }

        return IsMemoryPackSerializable(returnType);
    }

    private static bool IsMemoryPackSerializable(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return IsMemoryPackSerializable(arrayType.ElementType);
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                namedType.TypeArguments.Length == 1)
            {
                return IsMemoryPackSerializable(namedType.TypeArguments[0]);
            }

            if (namedType.IsTupleType)
            {
                return namedType.TupleElements.All(e => IsMemoryPackSerializable(e.Type));
            }

            if (IsMemoryPackable(namedType))
            {
                return true;
            }

            var fullName = namedType.ToDisplayString();
            if (fullName is "System.Guid" or "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan")
            {
                return true;
            }

            if (fullName.StartsWith("System.Collections.Generic.", StringComparison.Ordinal) &&
                namedType.TypeArguments.All(IsMemoryPackSerializable))
            {
                return true;
            }
        }

        return typeSymbol.SpecialType
            is SpecialType.System_Boolean
            or SpecialType.System_Byte
            or SpecialType.System_SByte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal
            or SpecialType.System_Char
            or SpecialType.System_String;
    }

    private static string? GetResponseTypeFullName(ITypeSymbol returnType)
    {
        if (returnType is INamedTypeSymbol namedType)
        {
            // 处理 Task<T> 和 ValueTask<T>
            if (namedType.IsGenericType && namedType.ConstructedFrom.Name is "Task" or "ValueTask")
            {
                if (namedType.TypeArguments.Length > 0)
                {
                    // 使用完全限定格式确保包含命名空间
                    return namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                return null;
            }

            // 处理非泛型 Task 和 ValueTask
            if (namedType.Name is "Task" or "ValueTask" && !namedType.IsGenericType)
            {
                return null;
            }
        }

        // 其他同步返回类型，使用完全限定格式
        return returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

}
