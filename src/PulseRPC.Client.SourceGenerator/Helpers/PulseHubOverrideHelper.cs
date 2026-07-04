using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PulseRPC.Generator.Helpers;

/// <summary>
/// §5.2-C：读取 <c>[PulseHub(Provide, Consume)]</c> 显式覆盖，用于覆盖生成器对
/// "本编译侧要生成调用方 Stub 还是被调方 Dispatcher" 的默认方向推断。
/// </summary>
/// <remarks>
/// 客户端侧的等价物，见服务端 <c>PulseRPC.Server.SourceGenerator.Helpers.PulseHubOverrideHelper</c>。
/// 见《统一 IPulseHub 全链路寻址与集群架构设计》§5.2-C。
/// </remarks>
internal static class PulseHubOverrideHelper
{
    /// <summary>
    /// 尝试读取接口上的 <c>[PulseHub]</c> 覆盖。
    /// </summary>
    /// <param name="typeSymbol">接口符号。</param>
    /// <param name="provide">本侧是否生成被调方骨架（客户端 Dispatcher）；未标注特性时恒为 <c>true</c>。</param>
    /// <param name="consume">本侧是否生成调用方代理（客户端 Stub）；未标注特性时恒为 <c>true</c>。</param>
    /// <returns>接口上是否标注了 <c>[PulseHub]</c> 特性（即是否存在显式覆盖）。</returns>
    public static bool TryGetOverride(INamedTypeSymbol typeSymbol, out bool provide, out bool consume)
    {
        var attribute = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "PulseHubAttribute" or "PulseHub");

        if (attribute is null)
        {
            provide = true;
            consume = true;
            return false;
        }

        provide = GetNamedBool(attribute, "Provide", defaultValue: true);
        consume = GetNamedBool(attribute, "Consume", defaultValue: true);
        return true;
    }

    private static bool GetNamedBool(AttributeData attribute, string name, bool defaultValue)
    {
        var namedArg = attribute.NamedArguments.FirstOrDefault(na => na.Key == name);
        if (!namedArg.Equals(default(KeyValuePair<string, TypedConstant>)) && namedArg.Value.Value is bool value)
        {
            return value;
        }

        return defaultValue;
    }
}
