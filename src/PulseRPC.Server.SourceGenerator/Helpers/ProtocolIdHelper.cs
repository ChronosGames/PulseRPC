using System;
using System.Collections.Generic;
using System.Linq;

namespace PulseRPC.Server.SourceGenerator.Helpers;

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
    /// 生成唯一协议号（处理冲突）
    /// </summary>
    /// <param name="signature">方法签名字符串</param>
    /// <param name="usedIds">已使用的协议号集合</param>
    /// <param name="reservedIds">保留的协议号集合（可选）</param>
    /// <returns>唯一的16位协议号</returns>
    public static ushort GenerateUniqueProtocolId(
        string signature,
        ICollection<ushort> usedIds,
        ICollection<ushort>? reservedIds = null)
    {
        var protocolId = GenerateProtocolId(signature);

        // 如果冲突，使用线性探测找到下一个可用的协议号
        var attempts = 0;
        while (usedIds.Contains(protocolId) || (reservedIds?.Contains(protocolId) ?? false))
        {
            protocolId = (ushort)((protocolId + 1) & 0xFFFF);
            attempts++;

            if (attempts > 65536)
            {
                throw new InvalidOperationException(
                    $"Failed to generate unique protocol ID for signature: {signature}");
            }
        }

        return protocolId;
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
}
