using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace PulseRPC.Client.SourceGenerator;

[Generator]
public class MessageRegistrationGenerator : ISourceGenerator
{
    private const string MessageAttributeFullName = "PulseRPC.PacketAttribute";
    private const string IMessageFullName = "PulseRPC.IPacket";
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new MessageSyntaxReceiver() as ISyntaxContextReceiver);
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not MessageSyntaxReceiver receiver)
        {
            return;
        }

        // 查找所有带有 PulseClientGenerationAttribute 的类型
        var clientGenerationTypes = context.Compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes())
            .OfType<ClassDeclarationSyntax>()
            .Select(c => context.Compilation.GetSemanticModel(c.SyntaxTree).GetDeclaredSymbol(c))
            .Where(symbol => symbol is INamedTypeSymbol &&
                             symbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == PulseClientGenerationAttributeName))
            .Cast<INamedTypeSymbol>()
            .ToList();

        if (!clientGenerationTypes.Any())
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC003",
                    "未找到标记了 PulseClientGenerationAttribute 的类型",
                    "请确保在客户端项目中至少有一个类型标记了 PulseClientGenerationAttribute",
                    "PulseRPC",
                    DiagnosticSeverity.Warning,
                    true),
                Location.None));
            return;
        }

        foreach (var clientType in clientGenerationTypes)
        {
            if (clientType == null) continue;

            var markerTypeAttr = clientType.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == PulseClientGenerationAttributeName);

            if (markerTypeAttr == null) continue;

            if (markerTypeAttr.ConstructorArguments[0].Value is not INamedTypeSymbol markerType)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "PRPC004",
                        "无效的 MarkerType",
                        $"类型 {clientType.Name} 的 PulseClientGenerationAttribute 中指定的 MarkerType 无效",
                        "PulseRPC",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None));
                continue;
            }

            // 获取标记类型所在程序集中的所有类型
            var messageTypes = new List<INamedTypeSymbol>();
            var assembly = markerType.ContainingAssembly;

            // 输出程序集信息
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC005",
                    "扫描程序集",
                    $"正在扫描程序集: {assembly.Name}",
                    "PulseRPC",
                    DiagnosticSeverity.Info,
                    true),
                Location.None));

            // 递归扫描所有命名空间
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(assembly.GlobalNamespace);

            while (stack.Count > 0)
            {
                var ns = stack.Pop();

                // 添加子命名空间
                foreach (var childNs in ns.GetNamespaceMembers())
                {
                    stack.Push(childNs);
                }

                // 检查当前命名空间中的类型
                foreach (var type in ns.GetTypeMembers())
                {
                    // 输出类型信息
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "PRPC006",
                            "扫描类型",
                            $"正在扫描类型: {type.ToDisplayString()}",
                            "PulseRPC",
                            DiagnosticSeverity.Info,
                            true),
                        Location.None));

                    // 检查是否实现了 IMessage 接口
                    var hasMessageInterface = type.AllInterfaces.Any(i =>
                        i.ToDisplayString() == IMessageFullName);
                    if (!hasMessageInterface)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "PRPC008",
                                "跳过类型",
                                $"类型 {type.ToDisplayString()} 未实现 IMessage 接口",
                                "PulseRPC",
                                DiagnosticSeverity.Info,
                                true),
                            Location.None));
                        continue;
                    }

                    // 检查是否有 Message 特性
                    var messageAttr = type.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == MessageAttributeFullName);
                    if (messageAttr == null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            new DiagnosticDescriptor(
                                "PRPC009",
                                "跳过类型",
                                $"类型 {type.ToDisplayString()} 未标记 MessageAttribute 特性",
                                "PulseRPC",
                                DiagnosticSeverity.Info,
                                true),
                            Location.None));
                        continue;
                    }

                    messageTypes.Add(type);

                    // 输出找到的消息类型
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "PRPC007",
                            "找到消息类型",
                            $"找到消息类型: {type.ToDisplayString()}, MessageId: {messageAttr.ConstructorArguments[0].Value}",
                            "PulseRPC",
                            DiagnosticSeverity.Info,
                            true),
                        Location.None));
                }
            }

            if (!messageTypes.Any())
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "PRPC002",
                        "未找到任何消息类型",
                        $"在程序集 {markerType.ContainingAssembly.Name} 中未找到任何带有 MessageAttribute 的消息类型",
                        "PulseRPC",
                        DiagnosticSeverity.Warning,
                        true),
                    Location.None));
                continue;
            }

            // 生成消息注册代码
            var source = GenerateMessageRegistration(clientType, messageTypes);
            context.AddSource($"{clientType.Name}.PacketRegistration.g.cs", source);
        }
    }

    private string GenerateMessageRegistration(INamedTypeSymbol clientType, List<INamedTypeSymbol> messageTypes)
    {
        var registrations1 = messageTypes
            .Select(type =>
            {
                var messageAttr = type.GetAttributes()
                    .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == MessageAttributeFullName);
                var messageId = messageAttr?.ConstructorArguments[0].Value as ushort? ?? 0;
                if (messageId == 0)
                {
                    messageId = (ushort)FNV1A32.GetHashCode(type.ToDisplayString());
                }
                return $"{{ typeof({type.ToDisplayString()}), 0x{messageId:X4} }},";
            })
            .Where(s => s != null)
            .OrderBy(s => s);

        var registrations2 = messageTypes
            .Select(type =>
            {
                var messageAttr = type.GetAttributes()
                    .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == MessageAttributeFullName);
                var messageId = messageAttr?.ConstructorArguments[0].Value as ushort? ?? 0;
                if (messageId == 0)
                {
                    messageId = (ushort)FNV1A32.GetHashCode(type.ToDisplayString());
                }
                return $"case 0x{messageId:X4}:\n                    return MemoryPackSerializer.Deserialize<{type.ToDisplayString()}>(bytes[2..], serializerOptions)!;";
            })
            .Where(s => s != null)
            .OrderBy(s => s);

        return $@"// <auto-generated/>
using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using MemoryPack;
using PulseRPC;

namespace {clientType.ContainingNamespace}
{{

    public partial class {clientType.Name}(MemoryPackSerializerOptions serializerOptions) : IPulseRPCSerializer
    {{
        private readonly Dictionary<Type, ushort> _packet2id = new()
        {{
            {string.Join("\n            ", registrations1)}
        }};

        public void Serialize<T>(IBufferWriter<byte> writer, in T message) where T : IPacket
        {{
            // 1. 获取消息ID
            var messageId = _packet2id.GetValueOrDefault(typeof(T));

            // 2. 为消息ID获取可写入的Span
            var idSpan = writer.GetSpan(2);
            BinaryPrimitives.WriteUInt16LittleEndian(idSpan, messageId);
            writer.Advance(2);

            // 3. 直接序列化消息到写入器
            MemoryPackSerializer.Serialize(writer, message, serializerOptions);
        }}

        public IPacket Deserialize(in ReadOnlySpan<byte> bytes)
        {{
            var messageId = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            switch (messageId)
            {{
                {string.Join("\n                ", registrations2)}
                default:
                    throw new NotSupportedException($""MessageId 0x{{messageId:X4}} is not supported."");
            }}
        }}
    }}
}}
";
    }
}
