using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    /// 计算方法对应的协议号（纯函数：仅基于方法签名的 FNV-1a 哈希，不做线性探测）
    /// </summary>
    /// <remarks>
    /// 协议号必须是签名的纯函数，不依赖"同一次编译中还有哪些其他方法"这类外部状态：
    /// 一旦引入线性探测（冲突时 +1 寻找空位），协议号就会随着编译单元中方法的增删而漂移，
    /// 且客户端/服务端各自独立编译时能看到的"邻居"集合可能不同，导致双方为同一方法算出
    /// 不同的协议号。冲突检测与报告统一由调用方（<see cref="AssignProtocolIds(System.Collections.Generic.IEnumerable{INamedTypeSymbol},SourceProductionContext)"/>）负责：
    /// 一旦发现冲突即报错（PRPC001），要求开发者用 <c>[Protocol(0xXXXX)]</c> 显式区分，而不是静默改号。
    /// </remarks>
    /// <param name="method">方法符号</param>
    /// <returns>生成的协议号</returns>
    public static ushort GenerateProtocolId(IMethodSymbol method)
    {
        // 构造方法签名字符串：InterfaceName.MethodName(ParameterTypes)
        var signature = BuildMethodSignature(method);

        // 使用 FNV-1a 哈希生成协议号（纯函数，无探测/无递增）
        var hash = ComputeFnv1aHash(signature);
        return (ushort)(hash & 0xFFFF);
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
    /// <remarks>
    /// 在 <see cref="Diagnostic.Properties"/> 中附带一个当前未被占用的建议协议号
    /// （键 <c>SuggestedProtocolId</c>，值为 4 位十六进制字符串），供
    /// <c>ProtocolIdConflictCodeFixProvider</c>（客户端）自动插入 <c>[Protocol(0xXXXX)]</c> 使用。
    /// </remarks>
    public static void ReportProtocolIdConflict(
        SourceProductionContext context,
        IMethodSymbol method,
        ushort protocolId,
        (string service, string method) existing,
        IReadOnlyCollection<ushort>? usedProtocolIds = null)
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

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (usedProtocolIds != null)
        {
            var suggested = FindSuggestedProtocolId(protocolId, usedProtocolIds);
            if (suggested.HasValue)
            {
                properties = properties.Add("SuggestedProtocolId", suggested.Value.ToString("X4"));
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(descriptor, method.Locations.FirstOrDefault(), properties));
    }

    /// <summary>
    /// 从指定协议号开始向后查找一个尚未被占用的协议号，作为 CodeFixProvider 的插入建议。
    /// </summary>
    private static ushort? FindSuggestedProtocolId(ushort protocolId, IReadOnlyCollection<ushort> usedProtocolIds)
    {
        for (var offset = 1; offset <= ushort.MaxValue; offset++)
        {
            var candidate = unchecked((ushort)(protocolId + offset));
            if (!usedProtocolIds.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
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
    /// 为单个接口的所有方法分配协议号（包括继承的接口方法）
    /// </summary>
    /// <remarks>
    /// 冲突检测范围仅限于该接口自身；如需要与编译单元内其他接口一起做冲突检测
    /// （与服务端 <c>AssignProtocolIdsForIncremental</c> 的可见域保持一致），请使用
    /// <see cref="AssignProtocolIds(IEnumerable{INamedTypeSymbol}, SourceProductionContext)"/> 重载。
    /// </remarks>
    /// <returns>键为 "{InterfaceFullName}.{MethodName}" 的协议号字典</returns>
    public static Dictionary<string, ushort> AssignProtocolIds(
        INamedTypeSymbol interfaceSymbol,
        SourceProductionContext context)
        => AssignProtocolIds(new[] { interfaceSymbol }, context);

    /// <summary>
    /// 为多个接口的所有方法统一分配协议号（包括各自继承的接口方法）
    /// </summary>
    /// <remarks>
    /// <para>
    /// 冲突检测聚合到整个传入集合（通常是编译单元内所有 <c>IPulseHub</c> 或所有
    /// <c>IPulseReceiver</c> 派生接口），而非逐接口独立判定。这与服务端
    /// <c>PulseRPCSourceGenerator.AssignProtocolIdsForIncremental</c> 的聚合范围保持一致，
    /// 避免"客户端只看到当前接口、服务端能看到全部接口"导致双方对同一方法算出不同协议号。
    /// </para>
    /// <para>
    /// 协议号本身是方法签名的纯哈希函数，不做线性探测；一旦发现冲突（无论是手动号之间、
    /// 自动号之间，还是手动号与自动号之间）立即报告 <c>PRPC001</c> 编译错误，要求开发者
    /// 通过 <c>[Protocol(0xXXXX)]</c> 显式区分，而不是静默改号。
    /// </para>
    /// </remarks>
    /// <returns>键为 "{InterfaceFullName}.{MethodName}" 的协议号字典</returns>
    public static Dictionary<string, ushort> AssignProtocolIds(
        IEnumerable<INamedTypeSymbol> interfaceSymbols,
        SourceProductionContext context)
    {
        var protocolIds = new Dictionary<string, ushort>();
        var usedIds = new Dictionary<ushort, (string service, string method)>();

        // 收集全部接口的方法（按声明接口+方法名去重，避免共享基接口的方法被重复处理为"冲突"）
        var methods = new List<IMethodSymbol>();
        var seenMethodKeys = new HashSet<string>();
        foreach (var interfaceSymbol in interfaceSymbols)
        {
            foreach (var method in GetAllInterfaceMethods(interfaceSymbol))
            {
                var methodKey = $"{method.ContainingType.ToDisplayString()}.{method.Name}";
                if (seenMethodKeys.Add(methodKey))
                {
                    methods.Add(method);
                }
            }
        }

        // 第一遍：收集所有手动指定的协议号
        foreach (var method in methods)
        {
            var declaringInterface = method.ContainingType.ToDisplayString();
            var methodKey = $"{declaringInterface}.{method.Name}";

            var manualId = GetManualProtocolId(method);
            if (manualId != 0)
            {
                // 检查手动指定的协议号是否冲突
                if (usedIds.TryGetValue(manualId, out var existing))
                {
                    ReportProtocolIdConflict(context, method, manualId, existing, usedIds.Keys);
                }
                else
                {
                    usedIds[manualId] = (declaringInterface, method.Name);
                    protocolIds[methodKey] = manualId;
                }
            }
        }

        // 第二遍：为没有手动指定的方法生成协议号（纯哈希，不做线性探测）
        foreach (var method in methods)
        {
            var declaringInterface = method.ContainingType.ToDisplayString();
            var methodKey = $"{declaringInterface}.{method.Name}";

            if (!protocolIds.ContainsKey(methodKey))
            {
                var protocolId = GenerateProtocolId(method);

                if (usedIds.TryGetValue(protocolId, out var existing))
                {
                    ReportProtocolIdConflict(context, method, protocolId, existing, usedIds.Keys);
                }
                else
                {
                    usedIds[protocolId] = (declaringInterface, method.Name);
                    protocolIds[methodKey] = protocolId;
                }
            }
        }

        return protocolIds;
    }

    /// <summary>
    /// 获取接口的所有方法（包括继承的接口方法）
    /// </summary>
    private static IEnumerable<IMethodSymbol> GetAllInterfaceMethods(INamedTypeSymbol interfaceSymbol)
    {
        var processedMethods = new HashSet<string>();

        // 首先处理当前接口定义的方法
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member is IMethodSymbol method && method.DeclaredAccessibility == Accessibility.Public)
            {
                var methodKey = GetMethodKey(method);
                if (processedMethods.Add(methodKey))
                {
                    yield return method;
                }
            }
        }

        // 然后处理继承的接口方法（排除 IPulseHub 等基础接口）
        foreach (var baseInterface in interfaceSymbol.AllInterfaces)
        {
            // 跳过 PulseRPC 框架的基础接口
            if (baseInterface.Name is "IPulseHub" or "IPulseReceiver")
                continue;

            foreach (var member in baseInterface.GetMembers())
            {
                if (member is IMethodSymbol method && method.DeclaredAccessibility == Accessibility.Public)
                {
                    var methodKey = GetMethodKey(method);
                    if (processedMethods.Add(methodKey))
                    {
                        yield return method;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 获取方法的唯一键（用于去重）
    /// </summary>
    private static string GetMethodKey(IMethodSymbol method)
    {
        var paramTypes = string.Join(",", method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken" &&
                       p.Type.ToDisplayString() != "CancellationToken")
            .Select(p => p.Type.ToDisplayString()));
        return $"{method.Name}({paramTypes})";
    }
}

