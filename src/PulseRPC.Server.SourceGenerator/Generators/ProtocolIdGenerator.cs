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
                    ? $"new[] {{ {string.Join(", ", method.Parameters.Select(p => $"typeof({p.TypeFullName})"))} }}"
                    : "Type.EmptyTypes";

                sb.AppendLine($"        // {service.InterfaceName}.{method.MethodName}");
                sb.AppendLine($"        {{ (typeof({service.InterfaceFullName}), new ProtocolId(0x{method.ProtocolId:X4})), ");
                sb.AppendLine($"          typeof({service.InterfaceFullName}).GetMethod(\"{method.MethodName}\", {paramTypesArray})! }},");
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
}
