using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using PulseRPC.Server.SourceGenerator.Helpers;

namespace PulseRPC.Server.SourceGenerator.Analyzers;

/// <summary>
/// 守卫分析器：检测把「访问控制 / 可见性」注解误标在 <c>[Channel("CLIENT")]</c> 推送接收器
/// （客户端实现、服务端推送）接口或其方法上的"死注解"。
/// </summary>
/// <remarks>
/// <para>
/// 统一 <c>IPulseHub</c> 模型下，<c>[Channel("CLIENT")]</c> 接口被服务端 <c>ServiceAnalyzer</c> 排除出
/// 「服务」分析（不进 <c>ServiceRoutingTable</c>、不经 <c>PermissionValidator</c>），其方法只由服务端
/// <strong>出站 Fan-out 代理</strong>（HubContext/HubClients）调用，而非入站被调用。因此以下这些只在
/// <strong>入站</strong>路径生效的注解贴在推送接收器上会被<strong>静默忽略</strong>，构成隐性 footgun：
/// </para>
/// <list type="bullet">
/// <item><description><c>[ClientFacing]</c> —— 协议框架级外部可见性白名单（仅入站协议号路由处强制）；</description></item>
/// <item><description><c>[Internal]</c> / <c>[ExternalOnly]</c> —— 调用来源限制（仅 <c>PermissionValidator</c> 强制）；</description></item>
/// <item><description><c>[Authorize]</c> / <c>[AllowAnonymous]</c> / <c>[RequireRole]</c> / <c>[RequirePermission]</c> —— 业务鉴权（仅入站强制）。</description></item>
/// </list>
/// <para>
/// 这不是编译错误（不影响运行），但几乎总是"以为加了防护其实没有"的误用，故以 <see cref="DiagnosticSeverity.Warning"/>
/// 在编译期暴露。方向/线程调度类注解（如 <c>[Reentrant]</c>/<c>[Priority]</c>）不在本守卫范围内——
/// 它们与访问控制正交，且语义上不构成"安全防护落空"的误解。
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClientReceiverAccessAttributeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>诊断 ID：访问控制/可见性注解标注在 <c>[Channel("CLIENT")]</c> 推送接收器上不生效。</summary>
    public const string DiagnosticId = "PULSE_CLIENT_RECEIVER_INEFFECTIVE_ATTRIBUTE";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "访问控制注解标注在推送接收器上不会生效",
        "属性 '{0}' 标注在 [Channel(\"CLIENT\")] 推送接收器 '{1}' 上不会生效并将被静默忽略"
            + "（推送接收器由服务端出站 Fan-out 代理调用，不经过入站路由与鉴权）；请移除该属性",
        "PulseRPC.Server.SourceGenerator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "在统一 IPulseHub 模型下，[Channel(\"CLIENT\")] 推送接收器不进入服务端入站路由表、"
            + "也不经过 PermissionValidator/ClientFacingGate，因此 [ClientFacing]/[Internal]/[ExternalOnly]/"
            + "[Authorize]/[AllowAnonymous]/[RequireRole]/[RequirePermission] 等访问控制与可见性注解贴在其上不会产生"
            + "任何运行时效果。访问控制应标注在服务端实现的入站 Hub（如 [Channel(\"XxxServer\")]）方法上。");

    /// <summary>被视为"死注解"的访问控制/可见性属性简单名（含/不含 Attribute 后缀）。</summary>
    private static readonly ImmutableHashSet<string> IneffectiveAttributeNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "ClientFacingAttribute", "ClientFacing",
        "InternalAttribute", "Internal",
        "ExternalOnlyAttribute", "ExternalOnly",
        "AuthorizeAttribute", "Authorize",
        "AllowAnonymousAttribute", "AllowAnonymous",
        "RequireRoleAttribute", "RequireRole",
        "RequirePermissionAttribute", "RequirePermission");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Interface)
            return;

        if (!IsClientChannelReceiver(type))
            return;

        // 接口（facet）级别的死注解。
        ReportIneffectiveAttributes(context, type, type.Name);

        // 方法级别的死注解。
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                ReportIneffectiveAttributes(context, method, type.Name);
            }
        }
    }

    /// <summary>判断接口是否标注 <c>[Channel("CLIENT")]</c>（大小写不敏感）。</summary>
    private static bool IsClientChannelReceiver(INamedTypeSymbol type)
    {
        // 仅对继承 IPulseHub 的推送接收器判定（与生成器的方向消歧一致）。
        if (!type.AllInterfaces.Any(i => i.Name == "IPulseHub"))
            return false;

        var channelAttr = type.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "ChannelAttribute" or "Channel");

        if (channelAttr is null || channelAttr.ConstructorArguments.Length == 0)
            return false;

        var channelName = channelAttr.ConstructorArguments[0].Value?.ToString();
        return string.Equals(channelName, ClientChannelConstants.ClientChannelName, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportIneffectiveAttributes(SymbolAnalysisContext context, ISymbol symbol, string receiverName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.Name;
            if (attributeName is null || !IneffectiveAttributeNames.Contains(attributeName))
                continue;

            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? symbol.Locations.FirstOrDefault()
                ?? Location.None;

            // 展示名去掉 Attribute 后缀，贴近源码书写形式（如 [ClientFacing] 而非 ClientFacingAttribute）。
            var displayName = attributeName.EndsWith("Attribute", StringComparison.Ordinal)
                ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
                : attributeName;

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, displayName, receiverName));
        }
    }
}
