using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using PulseRPC.Server.SourceGenerator.Models;

#pragma warning disable RS1035 // Do not use APIs banned by analyzers - needed for backward compatibility
namespace PulseRPC.Server.SourceGenerator.Generators;

/// <summary>
/// 协议号生成器 - 为 RPC 方法生成唯一的协议号
/// </summary>
public static class ProtocolIdGenerator
{
    /// <summary>
    /// 为服务的所有方法生成协议号（已废弃 - 仅用于向后兼容）
    /// 新代码应该使用 PulseRPCSourceGenerator.AssignProtocolIdsForIncremental
    /// </summary>
    /// <param name="services">服务模型列表</param>
    /// <param name="context">生成器执行上下文（用于报告冲突）</param>
    [Obsolete("This method is deprecated. Use the incremental generator version instead.")]
#pragma warning disable RS1035 // Do not use APIs banned by analyzers
    public static void AssignProtocolIds(List<ServiceModel> services, GeneratorExecutionContext context)
#pragma warning restore RS1035
    {
        var usedIds = new Dictionary<ushort, (string service, string method)>();
        var manualIds = new HashSet<ushort>();

        // 第一遍：收集所有手动指定的协议号（从 MethodModel.ProtocolId 读取）
        foreach (var service in services)
        {
            foreach (var method in service.Methods)
            {
                if (method.ProtocolId != 0) // 已经在 CreateServiceModelFromSymbol 中设置
                {
                    manualIds.Add(method.ProtocolId);

                    // 检查手动指定的协议号是否冲突
                    if (usedIds.TryGetValue(method.ProtocolId, out var existing))
                    {
#pragma warning disable CS0618 // Type or member is obsolete
                        ReportProtocolIdConflict(context, service, method, method.ProtocolId, existing);
#pragma warning restore CS0618
                    }
                    else
                    {
                        usedIds[method.ProtocolId] = (service.InterfaceName, method.MethodName);
                    }
                }
            }
        }

        // 第二遍：为没有手动指定的方法生成协议号
        foreach (var service in services)
        {
            foreach (var method in service.Methods)
            {
                if (method.ProtocolId == 0) // 未手动指定
                {
                    var protocolId = GenerateProtocolId(service, method, usedIds, manualIds);
                    method.ProtocolId = protocolId;
                    usedIds[protocolId] = (service.InterfaceName, method.MethodName);
                }
            }
        }
    }

    /// <summary>
    /// 生成协议号（基于 FNV-1a 哈希）
    /// </summary>
    private static ushort GenerateProtocolId(
        ServiceModel service,
        MethodModel method,
        Dictionary<ushort, (string service, string method)> usedIds,
        HashSet<ushort> manualIds)
    {
        // 构造方法签名字符串：InterfaceName.MethodName(ParameterTypes)
        var signature = BuildMethodSignature(service, method);

        // 使用 FNV-1a 哈希生成初始协议号
        var hash = ComputeFnv1aHash(signature);
        var protocolId = (ushort)(hash & 0xFFFF);

        // 如果冲突，使用线性探测找到下一个可用的协议号
        var originalId = protocolId;
        var attempts = 0;
        while (usedIds.ContainsKey(protocolId) || manualIds.Contains(protocolId))
        {
            protocolId = (ushort)((protocolId + 1) & 0xFFFF);
            attempts++;

            // 防止无限循环（理论上不应该发生，因为 ushort 有 65536 个可能值）
            if (attempts > 65536)
            {
                throw new InvalidOperationException(
                    $"Failed to generate unique protocol ID for {service.InterfaceName}.{method.MethodName}");
            }
        }

        return protocolId;
    }

    /// <summary>
    /// 构造方法签名字符串
    /// 排除 CancellationToken 参数，与客户端保持一致
    /// </summary>
    private static string BuildMethodSignature(ServiceModel service, MethodModel method)
    {
        var sb = new StringBuilder();

        // 格式：InterfaceName.MethodName(ParamType1,ParamType2,...)
        sb.Append(service.InterfaceFullName);
        sb.Append('.');
        sb.Append(method.MethodName);
        sb.Append('(');

        // 排除 CancellationToken 参数（与客户端保持一致）
        var parameters = method.Parameters
            .Where(p => p.TypeFullName != "System.Threading.CancellationToken" &&
                       p.TypeFullName != "CancellationToken")
            .ToList();

        if (parameters.Count > 0)
        {
            sb.Append(string.Join(",", parameters.Select(p => p.TypeFullName)));
        }

        sb.Append(')');

        return sb.ToString();
    }

    /// <summary>
    /// 计算 FNV-1a 哈希（32位）
    /// FNV-1a 是一个简单但分布良好的非加密哈希算法
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
    /// 报告协议号冲突的诊断信息（已废弃 - 保留用于向后兼容）
    /// </summary>
    [Obsolete("Use SourceProductionContext instead")]
    private static void ReportProtocolIdConflict(
        GeneratorExecutionContext context,
        ServiceModel service,
        MethodModel method,
        ushort protocolId,
        (string service, string method) existing)
    {
        var descriptor = new DiagnosticDescriptor(
            "PULSE003",
            "Protocol ID conflict detected",
            $"Protocol ID 0x{protocolId:X4} is already used by {existing.service}.{existing.method}. " +
            $"Method {service.InterfaceName}.{method.MethodName} cannot use the same protocol ID. " +
            $"Please manually specify a different protocol ID using [Protocol(0xXXXX)] attribute.",
            "PulseRPC.Server.SourceGenerator",
            DiagnosticSeverity.Error,
            true);

        context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
    }

    /// <summary>
    /// 生成协议号映射表的源代码
    /// </summary>
    public static string GenerateProtocolIdMappingTable(List<ServiceModel> services)
    {
        var sb = new StringBuilder();

        // 文件头部
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This code was generated by PulseRPC.Server.SourceGenerator");
        sb.AppendLine("// Protocol ID to Method mapping table for high-performance routing");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using PulseRPC.Protocol;");
        sb.AppendLine();

        sb.AppendLine("namespace PulseRPC.Generated;");
        sb.AppendLine();

        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Static protocol ID to method mapping table");
        sb.AppendLine("/// Generated at compile-time for zero-overhead method routing");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static partial class ProtocolIdMapping");
        sb.AppendLine("{");

        // 生成每个服务的协议号常量
        GenerateProtocolIdConstants(sb, services);

        // 生成映射字典初始化
        GenerateMappingDictionary(sb, services);

        // 生成查找方法
        GenerateLookupMethods(sb);

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// 生成协议号常量
    /// </summary>
    private static void GenerateProtocolIdConstants(StringBuilder sb, List<ServiceModel> services)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Protocol ID constants for all RPC methods");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static class ProtocolIds");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            sb.AppendLine($"        // {service.InterfaceFullName}");

            foreach (var method in service.Methods)
            {
                var constName = $"{service.InterfaceName.TrimStart('I')}_{method.MethodName}";
                sb.AppendLine($"        public const ushort {constName} = 0x{method.ProtocolId:X4}; // {method.ProtocolId}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成映射字典
    /// </summary>
    private static void GenerateMappingDictionary(StringBuilder sb, List<ServiceModel> services)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Pre-computed protocol ID to method info mapping");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private static readonly Dictionary<(Type ServiceType, ProtocolId ProtocolId), MethodInfo> _methodMapping = new()");
        sb.AppendLine("    {");

        foreach (var service in services)
        {
            foreach (var method in service.Methods)
            {
                var paramTypesArray = method.Parameters.Count > 0
                    ? $"new[] {{ {string.Join(", ", method.Parameters.Select(p => $"typeof({RemoveNullableMarker(p.TypeFullName)})"))} }}"
                    : "Type.EmptyTypes";

                var serviceTypeName = RemoveNullableMarker(service.InterfaceFullName);
                sb.AppendLine($"        // {service.InterfaceName}.{method.MethodName}");
                sb.AppendLine($"        {{ (typeof({serviceTypeName}), new ProtocolId(0x{method.ProtocolId:X4})), ");
                sb.AppendLine($"          typeof({serviceTypeName}).GetMethod(\"{method.MethodName}\", {paramTypesArray})! }},");
            }
        }

        sb.AppendLine("    };");
        sb.AppendLine();
    }

    /// <summary>
    /// 生成查找方法
    /// </summary>
    private static void GenerateLookupMethods(StringBuilder sb)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Get MethodInfo by protocol ID (O(1) lookup)");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static MethodInfo? GetMethod(Type serviceType, ProtocolId protocolId)");
        sb.AppendLine("    {");
        sb.AppendLine("        return _methodMapping.TryGetValue((serviceType, protocolId), out var methodInfo) ? methodInfo : null;");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Check if a protocol ID is valid for a service type");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static bool IsValidProtocolId(Type serviceType, ProtocolId protocolId)");
        sb.AppendLine("    {");
        sb.AppendLine("        return _methodMapping.ContainsKey((serviceType, protocolId));");
        sb.AppendLine("    }");
    }

    /// <summary>
    /// 移除类型名中的可空引用类型标记（?），保留可空值类型标记
    /// 因为 typeof(string?) 是无效的，但 typeof(int?) 是有效的
    /// 例如：string? -> string, int? -> int?, Dictionary&lt;string?, int?&gt; -> Dictionary&lt;string, int?&gt;
    /// </summary>
    private static string RemoveNullableMarker(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return typeName;

        var trimmed = typeName.Trim();
        return ProcessTypeName(trimmed, 0, trimmed.Length);
    }

    /// <summary>
    /// 递归处理类型名，移除可空引用类型标记
    /// </summary>
    private static string ProcessTypeName(string text, int start, int end)
    {
        var result = new StringBuilder(end - start);
        var i = start;

        while (i < end)
        {
            var ch = text[i];

            if (ch == '?')
            {
                // 找到可空标记，需要判断前面的类型
                var typeStart = FindPrecedingTypeNameStart(text, i, start);
                var typeName = text.Substring(typeStart, i - typeStart).Trim();

                if (IsValueType(typeName))
                {
                    // 值类型可空标记，保留
                    result.Append(ch);
                }
                // 否则是引用类型可空标记，跳过（移除）
            }
            else if (ch == '<')
            {
                // 泛型类型开始
                result.Append(ch);
                i++;

                // 找到泛型参数列表的结束位置
                var genericEnd = FindMatchingBracket(text, i, end, '<', '>');
                if (genericEnd < 0)
                {
                    // 未找到匹配的 >，直接复制剩余内容
                    while (i < end)
                    {
                        result.Append(text[i++]);
                    }
                    break;
                }

                // 递归处理泛型参数
                var genericParams = ProcessGenericParameters(text, i, genericEnd);
                result.Append(genericParams);
                result.Append('>'); // 添加闭合的 >
                i = genericEnd + 1;
                continue;
            }
            else if (ch == '(')
            {
                // 元组类型开始
                result.Append(ch);
                i++;

                // 找到元组的结束位置
                var tupleEnd = FindMatchingBracket(text, i, end, '(', ')');
                if (tupleEnd < 0)
                {
                    // 未找到匹配的 )，直接复制剩余内容
                    while (i < end)
                    {
                        result.Append(text[i++]);
                    }
                    break;
                }

                // 递归处理元组元素
                var tupleElements = ProcessTupleElements(text, i, tupleEnd);
                result.Append(tupleElements);
                result.Append(')'); // 添加闭合的 )
                i = tupleEnd + 1;
                continue;
            }
            else
            {
                result.Append(ch);
            }

            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// 处理泛型参数列表
    /// </summary>
    private static string ProcessGenericParameters(string text, int start, int end)
    {
        var result = new StringBuilder();
        var paramStart = start;
        var depth = 0;

        for (int i = start; i < end; i++)
        {
            var ch = text[i];

            if (ch == '<')
            {
                depth++;
                // 继续处理，让 ProcessTypeName 递归处理嵌套泛型
            }
            else if (ch == '>')
            {
                depth--;
                // 继续处理，让 ProcessTypeName 递归处理嵌套泛型
            }
            else if (ch == ',' && depth == 0)
            {
                // 顶层逗号，分隔参数
                var param = ProcessTypeName(text, paramStart, i);
                result.Append(param);
                result.Append(',');
                paramStart = i + 1;
            }
        }

        // 处理最后一个参数
        if (paramStart < end)
        {
            var param = ProcessTypeName(text, paramStart, end);
            result.Append(param);
        }

        return result.ToString();
    }

    /// <summary>
    /// 处理元组元素
    /// </summary>
    private static string ProcessTupleElements(string text, int start, int end)
    {
        var result = new StringBuilder();
        var elementStart = start;
        var depth = 0;

        for (int i = start; i < end; i++)
        {
            var ch = text[i];

            if (ch == '(')
            {
                depth++;
                // 继续处理，让 ProcessTypeName 递归处理嵌套元组
            }
            else if (ch == ')')
            {
                depth--;
                // 继续处理，让 ProcessTypeName 递归处理嵌套元组
            }
            else if (ch == ',' && depth == 0)
            {
                // 顶层逗号，分隔元素
                var element = ProcessTypeName(text, elementStart, i);
                result.Append(element);
                result.Append(',');
                elementStart = i + 1;
            }
        }

        // 处理最后一个元素
        if (elementStart < end)
        {
            var element = ProcessTypeName(text, elementStart, end);
            result.Append(element);
        }

        return result.ToString();
    }

    /// <summary>
    /// 查找匹配的括号位置
    /// </summary>
    private static int FindMatchingBracket(string text, int start, int end, char open, char close)
    {
        var depth = 1;
        for (int i = start; i < end; i++)
        {
            if (text[i] == open)
                depth++;
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 向前查找类型名的起始位置（跳过空格、逗号等分隔符）
    /// </summary>
    private static int FindPrecedingTypeNameStart(string text, int position, int limit)
    {
        var i = position - 1;
        // 跳过空格和分隔符
        while (i >= limit && (char.IsWhiteSpace(text[i]) || text[i] == ',' || text[i] == '<' || text[i] == '('))
        {
            i--;
        }

        // 继续向前查找类型名的开始（字母、数字、下划线、点）
        while (i >= limit && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.'))
        {
            i--;
        }

        return i + 1;
    }

    /// <summary>
    /// 判断类型名是否为值类型
    /// </summary>
    private static bool IsValueType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        var trimmed = typeName.Trim();

        // 移除命名空间前缀，只保留类型名
        var lastDot = trimmed.LastIndexOf('.');
        var simpleName = lastDot >= 0 ? trimmed.Substring(lastDot + 1) : trimmed;

        // C# 内置值类型
        var builtInValueTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "bool", "byte", "sbyte", "char", "decimal", "double", "float",
            "int", "uint", "long", "ulong", "short", "ushort",
            "Guid", "DateTime", "DateTimeOffset", "TimeSpan", "DateOnly", "TimeOnly"
        };

        if (builtInValueTypes.Contains(simpleName))
            return true;

        // 检查是否是 Nullable<T> 类型
        if (simpleName.StartsWith("Nullable<", StringComparison.Ordinal) || 
            simpleName.StartsWith("System.Nullable<", StringComparison.Ordinal))
            return true;

        // 检查是否是元组类型（元组是值类型）
        if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            return true;

        // 对于其他类型，假设是引用类型
        // 注意：自定义结构体可能无法准确识别，但这是安全的默认行为
        return false;
    }
}
