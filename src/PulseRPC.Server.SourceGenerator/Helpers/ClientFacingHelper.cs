using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PulseRPC.Server.SourceGenerator.Helpers;

/// <summary>
/// P-6：客户端可见性门闸 — 从符号（facet 接口或方法）上提取 <c>[ClientFacing]</c> 标注信息。
/// </summary>
internal static class ClientFacingHelper
{
    /// <summary>
    /// 读取符号上的 <c>[ClientFacing]</c> 特性。
    /// </summary>
    /// <returns>
    /// 未标注该特性时返回 <c>null</c>（表示「未声明」，由调用方决定是否回退到上一级默认值）；
    /// 标注时返回 <c>Enabled</c> 的值（无参数的 <c>[ClientFacing]</c> 等价于 <c>true</c>）。
    /// </returns>
    public static bool? GetClientFacing(ISymbol symbol)
    {
        var attribute = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "ClientFacingAttribute" or "ClientFacing");

        if (attribute == null)
        {
            return null;
        }

        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is bool enabled)
        {
            return enabled;
        }

        var namedArg = attribute.NamedArguments.FirstOrDefault(na => na.Key == "Enabled");
        if (!namedArg.Equals(default(KeyValuePair<string, TypedConstant>)) && namedArg.Value.Value is bool namedEnabled)
        {
            return namedEnabled;
        }

        // [ClientFacing]（无参数）等价于 [ClientFacing(true)]
        return true;
    }
}
