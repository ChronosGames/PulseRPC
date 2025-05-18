using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace PulseRPC.Client.SourceGenerator;

[Generator]
public class MessageRegistrationGenerator : IIncrementalGenerator
{
    private const string MessageAttributeFullName = "PulseRPC.PacketAttribute";
    private const string IMessageFullName = "PulseRPC.IPacket";
    private const string PulseClientGenerationAttributeName = "PulseClientGenerationAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 注册语法提供器：查找所有带有特性的类
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Where(static m => m is not null);

        // 创建编译供应商
        IncrementalValueProvider<Compilation> compilationProvider = context.CompilationProvider;

        // 结合语法和编译信息
        IncrementalValuesProvider<(ClassDeclarationSyntax Syntax, Compilation Compilation)> syntaxWithCompilation =
            classDeclarations.Combine(compilationProvider);

        // 为每个类型创建符号信息
        IncrementalValuesProvider<INamedTypeSymbol> clientTypes = syntaxWithCompilation
            .Select((pair, ct) => GetClientType(pair.Syntax, pair.Compilation, ct))
            .Where(static symbol => symbol is not null)!;

        // 注册源代码输出
        context.RegisterSourceOutput(clientTypes,
            static (spc, clientType) => GenerateMessageRegistration(spc, clientType));
    }

    private static INamedTypeSymbol? GetClientType(
        ClassDeclarationSyntax syntax,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        var symbol = model.GetDeclaredSymbol(syntax, cancellationToken) as INamedTypeSymbol;

        if (symbol is null)
            return null;

        // 检查是否有PulseClientGenerationAttribute
        if (!symbol.GetAttributes().Any(attr => attr.AttributeClass?.Name == PulseClientGenerationAttributeName))
            return null;

        return symbol;
    }

    private static void GenerateMessageRegistration(
        SourceProductionContext context,
        INamedTypeSymbol clientType)
    {
        try
        {
            var markerTypeAttr = clientType.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name == PulseClientGenerationAttributeName);

            if (markerTypeAttr == null) return;

            if (markerTypeAttr.ConstructorArguments.Length == 0 || 
                markerTypeAttr.ConstructorArguments[0].Value is not INamedTypeSymbol markerType)
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
                return;
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
                    // 检查是否实现了 IMessage 接口
                    var hasMessageInterface = type.AllInterfaces.Any(i =>
                        i.ToDisplayString() == IMessageFullName);
                    if (!hasMessageInterface)
                    {
                        continue;
                    }

                    // 检查是否有 Message 特性
                    var messageAttr = type.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == MessageAttributeFullName);
                    if (messageAttr == null)
                    {
                        continue;
                    }

                    messageTypes.Add(type);
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
                return;
            }

            // 生成消息注册代码
            var source = GenerateMessageRegistrationCode(clientType, messageTypes);
            context.AddSource($"{clientType.Name}.PacketRegistration.g.cs", source);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "PRPC001",
                    "生成消息注册代码异常",
                    $"生成消息注册代码时发生异常: {ex.Message}",
                    "PulseRPC",
                    DiagnosticSeverity.Error,
                    true),
                Location.None));
        }
    }

    private static string GenerateMessageRegistrationCode(INamedTypeSymbol clientType, List<INamedTypeSymbol> messageTypes)
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
            ushort messageId = 0;
            _packet2id.TryGetValue(typeof(T), out messageId);

            // 2. 为消息ID获取可写入的Span
            var idSpan = writer.GetSpan(2);
            BinaryPrimitives.WriteUInt16LittleEndian(idSpan, messageId);
            writer.Advance(2);

            // 3. 直接序列化消息到写入器
            MemoryPackSerializer.Serialize(writer, message, serializerOptions);
        }}

        public IPacket Deserialize(ReadOnlySpan<byte> bytes)
        {{
            var messageId = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            return messageId switch
            {{
                {string.Join("\n                ", registrations2)}
                _ => throw new NotImplementedException($""未知的消息类型: 0x{{messageId:X4}}"")
            }};
        }}
    }}
}}
";
    }
}
