using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PulseRPC.Server.SourceGenerator.Analyzers;

/// <summary>
/// 检测已硬移除的 <c>IPulseReceiver</c> 继承（统一 <c>IPulseHub</c> 架构 M2 决策），
/// 引导用户迁移为 <c>[Channel("CLIENT")] : IPulseHub</c>。
/// </summary>
/// <remarks>
/// <para>
/// 《统一 IPulseHub 全链路寻址与集群架构设计》§13.1 定稿 M2：硬移除 <c>IPulseReceiver</c>，
/// 不保留 <c>[Obsolete]</c> 兼容分支。由于该类型已从 <c>PulseRPC.Abstractions</c> 中物理删除，
/// 引用它的代码本身就会因 <c>CS0246</c>（找不到类型）编译失败；本分析器仅做纯语法层面的
/// 名称匹配（不依赖语义符号解析，因为该符号已不存在），以便在编译器报告 CS0246 的同时，
/// 给出更明确的迁移指引，并驱动 <see cref="PulseRPC.Server.SourceGenerator.CodeFixes.ReceiverMigrationCodeFixProvider"/>
/// 自动改写。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReceiverMigrationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>诊断 ID：检测到已移除的 <c>IPulseReceiver</c> 继承。</summary>
    public const string DiagnosticId = "PULSE_MIGRATE_RECEIVER";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "接口继承了已移除的 IPulseReceiver",
        "接口 '{0}' 继承了已在统一 IPulseHub 架构中硬移除的 IPulseReceiver，请改为 [Channel(\"CLIENT\")] 并继承 IPulseHub",
        "PulseRPC.Server.SourceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IPulseReceiver 已被硬移除（M2，见《统一 IPulseHub 全链路寻址与集群架构设计》§13.1）。" +
                      "所有推送契约（原 Receiver）应改写为标注 [Channel(\"CLIENT\")] 的 IPulseHub 派生接口。" +
                      "可使用配套的代码修复自动完成改写。");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInterfaceDeclaration, SyntaxKind.InterfaceDeclaration);
    }

    private static void AnalyzeInterfaceDeclaration(SyntaxNodeAnalysisContext context)
    {
        var interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        if (interfaceDeclaration.BaseList is null)
            return;

        foreach (var baseType in interfaceDeclaration.BaseList.Types)
        {
            if (!IsIPulseReceiverReference(baseType.Type))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                baseType.GetLocation(),
                interfaceDeclaration.Identifier.Text));
        }
    }

    /// <summary>
    /// 判断一个基类型语法节点是否为（已删除的）<c>IPulseReceiver</c> 的引用。
    /// </summary>
    /// <remarks>
    /// 纯语法名称匹配：由于 <c>IPulseReceiver</c> 已从程序集中删除，语义模型无法将其解析为
    /// 有效符号（会得到 <see cref="IErrorTypeSymbol"/> 或编译错误），因此不采用语义比对。
    /// </remarks>
    internal static bool IsIPulseReceiverReference(TypeSyntax typeSyntax)
    {
        var simpleName = typeSyntax switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualifiedName => qualifiedName.Right.Identifier.Text,
            _ => null,
        };

        return simpleName == "IPulseReceiver";
    }
}
