using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PulseRPC.Server.SourceGenerator.Helpers;

internal enum ManualProtocolIdParseResult
{
    Absent,
    Valid,
    Invalid,
}

/// <summary>
/// 协议号生成辅助类 - 使用 FNV-1a 哈希算法
/// </summary>
internal static class ProtocolIdHelper
{
    private const uint FnvPrime = 0x01000193;
    private const uint FnvOffsetBasis = 0x811C9DC5;

    /// <summary>
    /// 生成协议号（基于 FNV-1a 哈希）
    /// </summary>
    /// <param name="signature">方法签名字符串</param>
    /// <returns>16位协议号</returns>
    /// <remarks>
    /// 这是一个纯函数：相同的签名总是产生相同的协议号，且不依赖调用方传入的"已使用 ID"集合。
    /// 早期版本在这里做过线性探测（冲突时 +1 寻找空位），已被移除——探测会让协议号随编译单元
    /// 中方法的增删而静默漂移，且客户端与服务端各自独立编译时能看到的"冲突邻居"可能不同，
    /// 导致双方对同一方法算出不同的协议号。冲突检测与报告现在统一由调用方
    /// （<see cref="PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator"/> 中的
    /// <c>AssignProtocolIdsForIncremental</c> / <c>AssignReceiverProtocolIds</c>）负责：
    /// 一旦发现冲突立即报告编译错误，要求开发者用 <c>[Protocol(0xXXXX)]</c> 显式区分。
    /// </remarks>
    public static ushort GenerateProtocolId(string signature)
    {
        var hash = FnvOffsetBasis;

        foreach (var c in signature)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        return (ushort)(hash & 0xFFFF);
    }

    /// <summary>
    /// 构建方法签名字符串
    /// </summary>
    /// <param name="interfaceFullName">接口完全限定名</param>
    /// <param name="methodName">方法名</param>
    /// <param name="parameterTypes">参数类型列表（已排除 CancellationToken）</param>
    /// <returns>方法签名字符串</returns>
    public static string BuildMethodSignature(
        string interfaceFullName,
        string methodName,
        IEnumerable<string> parameterTypes)
    {
        return $"{interfaceFullName}.{methodName}({string.Join(",", parameterTypes)})";
    }

    /// <summary>
    /// 过滤掉 CancellationToken 参数类型
    /// </summary>
    /// <param name="parameterTypes">原始参数类型列表</param>
    /// <returns>过滤后的参数类型列表</returns>
    public static IEnumerable<string> FilterCancellationToken(IEnumerable<string> parameterTypes)
    {
        return parameterTypes.Where(p =>
            p != "System.Threading.CancellationToken" &&
            p != "CancellationToken");
    }

    public static bool IsCancellationToken(ITypeSymbol typeSymbol)
        => MethodIdentity.IsCancellationToken(typeSymbol);

    /// <summary>
    /// 读取数字或 CodeFix 生成的十六进制字符串形式 <c>[Protocol("0xXXXX")]</c>。
    /// </summary>
    public static bool TryGetManualProtocolId(IMethodSymbol methodSymbol, out ushort protocolId)
        => ParseManualProtocolId(methodSymbol, out protocolId) == ManualProtocolIdParseResult.Valid;

    public static ManualProtocolIdParseResult ParseManualProtocolId(
        IMethodSymbol methodSymbol,
        out ushort protocolId)
    {
        var protocolAttribute = methodSymbol.GetAttributes()
            .FirstOrDefault(attribute => attribute.AttributeClass?.Name is "ProtocolAttribute" or "Protocol");

        if (protocolAttribute == null)
        {
            protocolId = 0;
            return ManualProtocolIdParseResult.Absent;
        }

        if (protocolAttribute.ConstructorArguments.Length > 0)
        {
            var argument = protocolAttribute.ConstructorArguments[0];
            if (argument.Value is ushort ushortValue)
            {
                protocolId = ushortValue;
                return ManualProtocolIdParseResult.Valid;
            }

            if (argument.Value is int intValue && intValue >= ushort.MinValue && intValue <= ushort.MaxValue)
            {
                protocolId = (ushort)intValue;
                return ManualProtocolIdParseResult.Valid;
            }

            if (argument.Value is string hexValue)
            {
                var value = hexValue.Trim();
                if (value.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(2);
                }

                if (ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out protocolId))
                {
                    return ManualProtocolIdParseResult.Valid;
                }
            }
        }

        protocolId = 0;
        return ManualProtocolIdParseResult.Invalid;
    }

    /// <summary>
    /// 获取接口的所有公共方法候选（含直接成员 + 继承接口成员，排除 <c>IPulseHub</c> 标记接口本身），
    /// 按完整 CLR 签名去重；CancellationToken 过滤只用于后续 wire 身份与协议号计算。
    /// </summary>
    /// <remarks>
    /// 与客户端生成器（<c>PulseRPC.Generator</c> 项目中 <c>ServiceProxyGenerator.GetAllInterfaceMethods</c>）
    /// 保持完全一致的收集范围，避免两侧协议号计算基于不同的方法集合而产生不一致
    /// （见《统一 IPulseHub 全链路寻址与集群架构设计》§11.2 风险 #1）。
    /// 调用方仍需自行应用各自的额外过滤条件（如 <c>MethodKind.Ordinary</c>、异步返回类型检查等）。
    /// </remarks>
    public static IEnumerable<IMethodSymbol> GetAllPublicMethods(INamedTypeSymbol typeSymbol)
    {
        var seen = new HashSet<string>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IMethodSymbol method &&
                method.DeclaredAccessibility == Accessibility.Public &&
                seen.Add(GetMethodSignatureKey(method)))
            {
                yield return method;
            }
        }

        foreach (var baseInterface in typeSymbol.AllInterfaces)
        {
            if (baseInterface.Name is "IPulseHub")
                continue;

            foreach (var member in baseInterface.GetMembers())
            {
                if (member is IMethodSymbol method &&
                    method.DeclaredAccessibility == Accessibility.Public &&
                    seen.Add(GetMethodSignatureKey(method)))
                {
                    yield return method;
                }
            }
        }
    }

    private static string GetMethodSignatureKey(IMethodSymbol method)
    {
        return MethodIdentity.CreateClrSignatureKey(method);
    }
}
