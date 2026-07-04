using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using PulseRPC.Client.SourceGenerator.Analyzers;
using PulseRPC.Generator.Helpers;

namespace PulseRPC.Client.SourceGenerator.CodeFixes;

/// <summary>
/// 针对 <see cref="ReceiverMigrationAnalyzer.DiagnosticId"/>（<c>PRPC_MIGRATE_RECEIVER</c>）诊断的自动修复。
/// </summary>
/// <remarks>
/// 把 <c>IXxxReceiver : IPulseReceiver</c> 机械改写为 <c>[Channel("CLIENT")] IXxxReceiver : IPulseHub</c>，
/// 对应《统一 IPulseHub 全链路寻址与集群架构设计》§13.2 迁移步骤第 1 步。
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReceiverMigrationCodeFixProvider))]
[Shared]
public sealed class ReceiverMigrationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(ReceiverMigrationAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var interfaceDeclaration = node.FirstAncestorOrSelf<InterfaceDeclarationSyntax>();
            if (interfaceDeclaration == null)
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "改为 [Channel(\"CLIENT\")] : IPulseHub",
                    createChangedDocument: ct => MigrateReceiverInterfaceAsync(context.Document, root, interfaceDeclaration, ct),
                    equivalenceKey: nameof(ReceiverMigrationCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> MigrateReceiverInterfaceAsync(
        Document document,
        SyntaxNode root,
        InterfaceDeclarationSyntax interfaceDeclaration,
        CancellationToken cancellationToken)
    {
        var migrated = ReplaceIPulseReceiverBaseType(interfaceDeclaration);
        migrated = EnsureClientChannelAttribute(migrated);

        var newRoot = root.ReplaceNode(interfaceDeclaration, migrated);
        var newDocument = document.WithSyntaxRoot(newRoot);

        return await Formatter.FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static InterfaceDeclarationSyntax ReplaceIPulseReceiverBaseType(InterfaceDeclarationSyntax interfaceDeclaration)
    {
        if (interfaceDeclaration.BaseList is null)
            return interfaceDeclaration;

        var newTypes = interfaceDeclaration.BaseList.Types.Select(baseType =>
            ReceiverMigrationAnalyzer.IsIPulseReceiverReference(baseType.Type)
                ? baseType.WithType(SyntaxFactory.IdentifierName("IPulseHub").WithTriviaFrom(baseType.Type))
                : baseType);

        return interfaceDeclaration.WithBaseList(
            interfaceDeclaration.BaseList.WithTypes(SyntaxFactory.SeparatedList(newTypes)));
    }

    private static InterfaceDeclarationSyntax EnsureClientChannelAttribute(InterfaceDeclarationSyntax interfaceDeclaration)
    {
        var hasChannelAttribute = interfaceDeclaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attr => attr.Name.ToString() is "Channel" or "ChannelAttribute");

        if (hasChannelAttribute)
            return interfaceDeclaration;

        var channelAttribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("Channel"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(ClientChannelConstants.ClientChannelName))))));

        var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(channelAttribute))
            .WithLeadingTrivia(interfaceDeclaration.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        return interfaceDeclaration
            .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
            .AddAttributeLists(attributeList)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
}
