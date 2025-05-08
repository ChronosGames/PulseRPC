using Microsoft.CodeAnalysis;

namespace PulseRPC.Client.SourceGenerator;

/// <summary>
/// 生成器帮助类
/// </summary>
public static class GeneratorHelper
{
    /// <summary>
    /// 获取特性的命名构造参数值
    /// </summary>
    /// <typeparam name="T">参数类型</typeparam>
    /// <param name="attribute">属性数据</param>
    /// <param name="name">参数名称</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>参数值</returns>
    public static T? GetNamedArgumentValue<T>(AttributeData attribute, string name, T? defaultValue = default)
    {
        if (attribute == null)
            return defaultValue;

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == name)
            {
                if (namedArgument.Value.Value is T value)
                    return value;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// 检查类型是否实现了指定接口
    /// </summary>
    /// <param name="symbol">类型符号</param>
    /// <param name="interfaceName">接口名称</param>
    /// <param name="namespaceName">命名空间名称</param>
    /// <returns>是否实现了接口</returns>
    public static bool ImplementsInterface(INamedTypeSymbol symbol, string interfaceName, string namespaceName)
    {
        return symbol.AllInterfaces.Any(i => i.Name == interfaceName && i.ContainingNamespace.ToString() == namespaceName);
    }

    /// <summary>
    /// 查找类型的特性
    /// </summary>
    /// <param name="symbol">类型符号</param>
    /// <param name="attributeName">特性名称</param>
    /// <param name="namespaceName">命名空间名称</param>
    /// <returns>特性数据，如果未找到则返回null</returns>
    public static AttributeData? FindAttribute(INamedTypeSymbol symbol, string attributeName, string namespaceName)
    {
        return symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == attributeName &&
            a.AttributeClass.ContainingNamespace.ToString() == namespaceName);
    }
}
