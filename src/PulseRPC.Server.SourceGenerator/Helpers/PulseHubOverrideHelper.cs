using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace PulseRPC.Server.SourceGenerator.Helpers;

/// <summary>
/// §5.2-C：读取 <c>[PulseHub(Provide, Consume)]</c> 显式覆盖，用于覆盖生成器对
/// "本编译侧要生成被调方骨架还是调用方代理" 的默认方向推断。
/// </summary>
/// <remarks>
/// 绝大多数接口无需标注该特性，靠 <c>[Channel]</c>（谁提供）+ 编译侧（服务端/客户端源生成器）即可
/// 推断方向；仅在少数歧义场景（如 Actor 自调用、纯 Shared 双向契约）下用本特性显式覆盖。
/// 见《统一 IPulseHub 全链路寻址与集群架构设计》§5.2-C。
/// </remarks>
internal static class PulseHubOverrideHelper
{
    /// <summary>
    /// 尝试读取接口上的 <c>[PulseHub]</c> 覆盖。
    /// </summary>
    /// <param name="typeSymbol">接口符号。</param>
    /// <param name="provide">本侧是否生成被调方骨架；未标注特性时恒为 <c>true</c>（不影响调用方判断）。</param>
    /// <param name="consume">本侧是否生成调用方代理；未标注特性时恒为 <c>true</c>（不影响调用方判断）。</param>
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

        // PulseHubAttribute.Provide/Consume 均为具名属性（AttributeUsage 无位置构造参数），
        // 特性定义中的 C# 默认值均为 true，未显式设置的一侧保持 true 语义。
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
