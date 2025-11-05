using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace PulseRPC.Generator.Generators;

/// <summary>
/// 协议号生成器 - 为 RPC 方法生成唯一的协议号（客户端版本）
/// 与服务端使用相同的算法，确保协议号一致性
/// </summary>
public static class ProtocolIdGenerator
{
    /// <summary>
    /// 为方法生成协议号
    /// </summary>
    /// <param name="method">方法符号</param>
    /// <param name="usedIds">已使用的协议号字典（用于冲突检测）</param>
    /// <param name="manualIds">手动指定的协议号集合</param>
    /// <returns>生成的协议号</returns>
    public static ushort GenerateProtocolId(
        IMethodSymbol method,
        Dictionary<ushort, (string service, string method)> usedIds,
        HashSet<ushort> manualIds)
    {
        // 构造方法签名字符串：InterfaceName.MethodName(ParameterTypes)
        var signature = BuildMethodSignature(method);

        // 使用 FNV-1a 哈希生成初始协议号
        var hash = ComputeFnv1aHash(signature);
        var protocolId = (ushort)(hash & 0xFFFF);

        // 如果冲突，使用线性探测找到下一个可用的协议号
        var attempts = 0;
        while (usedIds.ContainsKey(protocolId) || manualIds.Contains(protocolId))
        {
            protocolId = (ushort)((protocolId + 1) & 0xFFFF);
            attempts++;

            // 防止无限循环（理论上不应该发生，因为 ushort 有 65536 个可能值）
            if (attempts > 65536)
            {
                throw new InvalidOperationException(
                    $"Failed to generate unique protocol ID for {method.ContainingType.ToDisplayString()}.{method.Name}");
            }
        }

        return protocolId;
    }

    /// <summary>
    /// 构造方法签名字符串
    /// 格式：InterfaceFullName.MethodName(ParamType1,ParamType2,...)
    /// 必须与服务端的格式完全一致
    /// </summary>
    public static string BuildMethodSignature(IMethodSymbol method)
    {
        var sb = new StringBuilder();

        // 接口全限定名
        sb.Append(method.ContainingType.ToDisplayString());
        sb.Append('.');
        
        // 方法名
        sb.Append(method.Name);
        sb.Append('(');

        // 参数类型列表
        var parameters = method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                       p.Type.ToDisplayString() != "CancellationToken")
            .ToList();

        if (parameters.Count > 0)
        {
            sb.Append(string.Join(",", parameters.Select(p => p.Type.ToDisplayString())));
        }

        sb.Append(')');

        return sb.ToString();
    }

    /// <summary>
    /// 计算 FNV-1a 哈希（32位）
    /// FNV-1a 是一个简单但分布良好的非加密哈希算法
    /// 必须与服务端使用相同的算法
    /// </summary>
    private static uint ComputeFnv1aHash(string text)
    {
        const uint FnvPrime = 0x01000193;
        const uint FnvOffsetBasis = 0x811C9DC5;

        var hash = FnvOffsetBasis;

        foreach (var c in text)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        return hash;
    }

    /// <summary>
    /// 尝试从方法的 [Protocol] 特性获取手动指定的协议号
    /// </summary>
    /// <param name="method">方法符号</param>
    /// <returns>协议号，如果未指定则返回 0</returns>
    public static ushort GetManualProtocolId(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            // 检查是否是 [Protocol] 特性
            if (attribute.AttributeClass?.Name is "ProtocolAttribute" or "Protocol")
            {
                // 获取第一个构造参数（协议号）
                if (attribute.ConstructorArguments.Length > 0)
                {
                    var value = attribute.ConstructorArguments[0].Value;
                    if (value is ushort protocolId)
                    {
                        return protocolId;
                    }
                    if (value is int intValue && intValue >= 0 && intValue <= ushort.MaxValue)
                    {
                        return (ushort)intValue;
                    }
                }
            }
        }

        return 0; // 未指定
    }

    /// <summary>
    /// 报告协议号冲突的诊断信息
    /// </summary>
    public static void ReportProtocolIdConflict(
        SourceProductionContext context,
        IMethodSymbol method,
        ushort protocolId,
        (string service, string method) existing)
    {
        var descriptor = new DiagnosticDescriptor(
            "PRPC001",
            "Protocol ID collision detected",
            $"Protocol ID 0x{protocolId:X4} ({protocolId}) is already used by {existing.service}.{existing.method}. " +
            $"Method {method.ContainingType.ToDisplayString()}.{method.Name} cannot use the same protocol ID. " +
            $"Please manually specify a different protocol ID using [Protocol(0xXXXX)] attribute.",
            "PulseRPC.Client.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, method.Locations.FirstOrDefault()));
    }

    /// <summary>
    /// 生成协议号常量名称（旧版本 - 向后兼容）
    /// </summary>
    [Obsolete("Use GetProtocolIdConstantName(IMethodSymbol) instead to support method overloading")]
    public static string GetProtocolIdConstantName(string methodName)
    {
        return $"ProtocolId_{methodName}";
    }

    /// <summary>
    /// 生成协议号常量名称（支持方法重载）
    /// </summary>
    /// <param name="method">方法符号</param>
    /// <returns>唯一的协议号常量名</returns>
    public static string GetProtocolIdConstantName(IMethodSymbol method)
    {
        var baseName = $"ProtocolId_{method.Name}";
        
        // 如果方法没有参数，直接返回基础名称
        if (method.Parameters.Length == 0)
        {
            return baseName;
        }

        // 构建参数类型的简短描述
        var paramParts = method.Parameters.Select(p => GetSimpleTypeName(p.Type)).ToArray();
        var paramSuffix = string.Join("_", paramParts);

        return $"{baseName}_{paramSuffix}";
    }

    /// <summary>
    /// 获取类型的简化名称（用于生成常量名）
    /// </summary>
    private static string GetSimpleTypeName(ITypeSymbol type)
    {
        // 处理数组类型
        if (type is IArrayTypeSymbol arrayType)
        {
            return GetSimpleTypeName(arrayType.ElementType) + "Array";
        }

        // 处理泛型类型
        if (type is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            var baseName = genericType.Name;
            var typeArgs = string.Join("", genericType.TypeArguments.Select(GetSimpleTypeName));
            return $"{baseName}Of{typeArgs}";
        }

        // 使用不带命名空间的类型名
        var simpleName = type.Name;

        // 移除常见的后缀以缩短名称
        simpleName = simpleName
            .Replace("Event", "Evt")
            .Replace("Message", "Msg")
            .Replace("Request", "Req")
            .Replace("Response", "Rsp")
            .Replace("Data", "")
            .Replace("Info", "");

        return simpleName;
    }

    /// <summary>
    /// 为接口的所有方法分配协议号
    /// </summary>
    public static Dictionary<string, ushort> AssignProtocolIds(
        INamedTypeSymbol interfaceSymbol,
        SourceProductionContext context)
    {
        var protocolIds = new Dictionary<string, ushort>();
        var usedIds = new Dictionary<ushort, (string service, string method)>();
        var manualIds = new HashSet<ushort>();

        var interfaceName = interfaceSymbol.ToDisplayString();
        var methods = interfaceSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        // 第一遍：收集所有手动指定的协议号
        foreach (var method in methods)
        {
            var manualId = GetManualProtocolId(method);
            if (manualId != 0)
            {
                manualIds.Add(manualId);

                // 检查手动指定的协议号是否冲突
                if (usedIds.TryGetValue(manualId, out var existing))
                {
                    ReportProtocolIdConflict(context, method, manualId, existing);
                }
                else
                {
                    usedIds[manualId] = (interfaceName, method.Name);
                    protocolIds[method.Name] = manualId;
                }
            }
        }

        // 第二遍：为没有手动指定的方法生成协议号
        foreach (var method in methods)
        {
            if (!protocolIds.ContainsKey(method.Name))
            {
                var protocolId = GenerateProtocolId(method, usedIds, manualIds);

                // 再次检查生成的协议号是否冲突（理论上不应该，但保险起见）
                if (usedIds.TryGetValue(protocolId, out var existing))
                {
                    ReportProtocolIdConflict(context, method, protocolId, existing);
                }
                else
                {
                    usedIds[protocolId] = (interfaceName, method.Name);
                    protocolIds[method.Name] = protocolId;
                }
            }
        }

        return protocolIds;
    }
}

