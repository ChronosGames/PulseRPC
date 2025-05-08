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
    private const string MessageAttributeFullName = "PulseRPC.Protocol.Attributes.MessageAttribute";
    private const string IMessageFullName = "PulseRPC.Protocol.IMessage";
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
            .Where(symbol => symbol != null && symbol is INamedTypeSymbol &&
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

            var markerType = markerTypeAttr.ConstructorArguments[0].Value as INamedTypeSymbol;
            if (markerType == null)
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
            context.AddSource($"{clientType.Name}.MessageRegistration.g.cs", source);
        }
    }

    private string GenerateMessageRegistration(INamedTypeSymbol clientType, List<INamedTypeSymbol> messageTypes)
    {
        var registrations = messageTypes
            .Select(type =>
            {
                var messageAttr = type.GetAttributes()
                    .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == MessageAttributeFullName);
                if (messageAttr == null) return null;
                var messageId = messageAttr.ConstructorArguments[0].Value;
                if (messageId == null) return null;
                return $"            PulseRPCFormatterProvider.RegisterMessageType<{type.ToDisplayString()}>({(int)messageId});";
            })
            .Where(s => s != null)
            .OrderBy(s => s);

        return $@"// <auto-generated/>
using System;
using PulseRPC.Protocol.Serialization;

namespace {clientType.ContainingNamespace}
{{
    public partial class {clientType.Name}
    {{
        static partial void RegisterMessages()
        {{
{string.Join("\n", registrations)}
        }}
    }}
}}
";
    }
}
