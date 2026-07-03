using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace PulseRPC.Client.SourceGenerator.CodeFixes;

/// <summary>
/// 针对客户端协议号冲突诊断（<c>PRPC001</c>）的自动修复。
/// </summary>
/// <remarks>
/// <para>
/// 协议号是方法签名的纯哈希函数（FNV-1a），不做线性探测；一旦发生冲突，唯一的解决方式是
/// 通过 <c>[Protocol(0xXXXX)]</c> 特性手动指定一个不冲突的协议号。
/// </para>
/// <para>
/// 本 CodeFixProvider 读取诊断 <see cref="Diagnostic.Properties"/> 中由生成器计算好的
/// <c>SuggestedProtocolId</c>（一个当前未被占用的协议号，从冲突号 +1 开始向后查找），
/// 在冲突方法声明前插入 <c>[Protocol(0x{SuggestedProtocolId})]</c>。
/// </para>
/// <para>
/// 建议号仅基于生成诊断那一刻已知的协议号集合计算，应用修复后应重新编译确认冲突已解决。
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ProtocolIdConflictCodeFixProvider))]
[Shared]
public sealed class ProtocolIdConflictCodeFixProvider : CodeFixProvider
{
    private const string SuggestedProtocolIdKey = "SuggestedProtocolId";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("PRPC001");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(SuggestedProtocolIdKey, out var suggestedHex) ||
                string.IsNullOrEmpty(suggestedHex))
            {
                continue;
            }

            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var methodDeclaration = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (methodDeclaration == null)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"添加 [Protocol(0x{suggestedHex})] 以解决协议号冲突",
                    createChangedDocument: ct => AddProtocolAttributeAsync(context.Document, root, methodDeclaration, suggestedHex!, ct),
                    equivalenceKey: $"{nameof(ProtocolIdConflictCodeFixProvider)}_{suggestedHex}"),
                diagnostic);
        }
    }

    private static async Task<Document> AddProtocolAttributeAsync(
        Document document,
        SyntaxNode root,
        MethodDeclarationSyntax methodDeclaration,
        string suggestedHex,
        CancellationToken cancellationToken)
    {
        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("Protocol"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal("0x" + suggestedHex))))));

        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        var newMethodDeclaration = methodDeclaration
            .AddAttributeLists(attributeList)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(methodDeclaration, newMethodDeclaration);
        var newDocument = document.WithSyntaxRoot(newRoot);

        return await Formatter.FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
