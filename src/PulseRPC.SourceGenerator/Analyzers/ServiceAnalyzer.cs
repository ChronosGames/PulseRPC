using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PulseRPC.SourceGenerator.Models;

namespace PulseRPC.SourceGenerator.Analyzers;

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

        // 检查是否标记为PulseService
        if (!HasPulseServiceAttribute(interfaceSymbol))
            return null;

        var interfaceName = interfaceSymbol.Name;
        var interfaceFullName = interfaceSymbol.ToDisplayString();
        var namespaceName = interfaceSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var channelName = GetChannelName(interfaceSymbol) ?? "DefaultChannel";

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
            Methods = methods
        };
    }

    /// <summary>
    /// 检查接口是否有PulseService特性
    /// </summary>
    private static bool HasPulseServiceAttribute(INamedTypeSymbol interfaceSymbol)
    {
        return interfaceSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "PulseServiceAttribute" || 
                        attr.AttributeClass?.Name == "PulseService");
    }

    /// <summary>
    /// 获取通道名称
    /// </summary>
    private static string? GetChannelName(INamedTypeSymbol interfaceSymbol)
    {
        var channelAttr = interfaceSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "ChannelAttribute" || 
                                   attr.AttributeClass?.Name == "Channel");

        if (channelAttr?.ConstructorArguments.Length > 0)
        {
            var channelName = channelAttr.ConstructorArguments[0].Value?.ToString();
            return channelName;
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
            ChannelName = methodChannelName
        };
    }

    /// <summary>
    /// 获取方法的通道名称
    /// </summary>
    private static string? GetMethodChannelName(IMethodSymbol methodSymbol)
    {
        var channelAttr = methodSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == "ChannelAttribute" || 
                                   attr.AttributeClass?.Name == "Channel");

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
            if (namedType.IsGenericType && 
                (namedType.ConstructedFrom.Name == "Task" || namedType.ConstructedFrom.Name == "ValueTask"))
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
            return typeName == "Task" || typeName == "ValueTask";
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
            return (typeName == "Task" || typeName == "ValueTask") && namedType.TypeArguments.Length > 0;
        }
        return false;
    }

    /// <summary>
    /// 检查类型是否标记为MemoryPackable
    /// </summary>
    private static bool IsMemoryPackable(ITypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes()
            .Any(attr => attr.AttributeClass?.Name == "MemoryPackableAttribute" || 
                        attr.AttributeClass?.Name == "MemoryPackable");
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
    /// 检查接口语法是否包含PulseService特性
    /// </summary>
    private static bool HasPulseServiceAttributeSyntax(InterfaceDeclarationSyntax interfaceDeclaration)
    {
        return interfaceDeclaration.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attr => 
            {
                var name = attr.Name.ToString();
                return name == "PulseService" || 
                       name == "PulseServiceAttribute" ||
                       name.EndsWith("PulseService") ||
                       name.EndsWith("PulseServiceAttribute");
            });
    }
}