using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PulseRPC.Server.SourceGenerator.Analyzers;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

/// <summary>
/// 回归测试：<see cref="ClientReceiverAccessAttributeAnalyzer"/> —— 守卫把访问控制/可见性注解误标在
/// <c>[Channel("CLIENT")]</c> 推送接收器上的"死注解"，编译期以 Warning 暴露。
/// </summary>
public class ClientReceiverAccessAttributeAnalyzerTests
{
    private const string DiagnosticId = "PULSE_CLIENT_RECEIVER_INEFFECTIVE_ATTRIBUTE";

    /// <summary>本地声明一个与 PulseRPC.Server 中同名的 [Internal]，验证分析器按简单名匹配（无需引用 Server 程序集）。</summary>
    private const string LocalInternalAttributeSource = """
        namespace PulseRPC
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class InternalAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public async Task ClientReceiver_WithInterfaceAndMethodLevelAccessAttributes_MustWarnForEach()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using PulseRPC;

            namespace GuardTestNs;

            [Channel("CLIENT")]
            [ClientFacing]
            public interface IChatReceiver : IPulseHub
            {
                [Authorize(Role = RoleTypes.External)]
                Task OnMessage(string text);

                [AllowAnonymous]
                [Internal]
                Task OnSystemAnnouncement(string text);
            }
            """;

        var diagnostics = await RunAnalyzerAsync(source);

        // 接口级 [ClientFacing] + 方法级 [Authorize] + [AllowAnonymous] + [Internal] = 4 处死注解。
        diagnostics.Should().HaveCount(4);
        diagnostics.Should().OnlyContain(d => d.Id == DiagnosticId);
        diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Warning);

        var messages = diagnostics.Select(d => d.GetMessage()).ToArray();
        messages.Should().Contain(m => m.Contains("ClientFacing") && m.Contains("IChatReceiver"));
        messages.Should().Contain(m => m.Contains("Authorize"));
        messages.Should().Contain(m => m.Contains("AllowAnonymous"));
        messages.Should().Contain(m => m.Contains("Internal"));
    }

    [Fact]
    public async Task NormalServerHub_WithAccessAttributes_MustNotWarn()
    {
        // 服务端实现的入站 Hub：这些注解在此处是**有效**的，绝不应被守卫误报。
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using PulseRPC;

            namespace GuardTestNs;

            [Channel("GameServer")]
            [ClientFacing]
            public interface IGameHub : IPulseHub
            {
                [Authorize(Role = RoleTypes.External)]
                Task<int> LoginAsync(string token);

                [AllowAnonymous]
                Task PingAsync();
            }
            """;

        var diagnostics = await RunAnalyzerAsync(source);

        diagnostics.Should().BeEmpty("访问控制注解标注在服务端入站 Hub 上是有效用法，不应触发守卫");
    }

    [Fact]
    public async Task ClientReceiver_WithoutAccessAttributes_MustNotWarn()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using PulseRPC;

            namespace GuardTestNs;

            [Channel("CLIENT")]
            public interface ICleanReceiver : IPulseHub
            {
                Task OnMessage(string text);
            }
            """;

        var diagnostics = await RunAnalyzerAsync(source);

        diagnostics.Should().BeEmpty("干净的推送接收器（只声明方向）不应触发任何守卫诊断");
    }

    [Fact]
    public async Task NonReceiverInterface_EvenWithClientFacing_MustNotWarn()
    {
        // 不继承 IPulseHub、也无 [Channel("CLIENT")] 的普通接口：守卫不应介入。
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using PulseRPC;

            namespace GuardTestNs;

            [ClientFacing]
            public interface IJustSomeInterface
            {
                Task DoAsync();
            }
            """;

        var diagnostics = await RunAnalyzerAsync(source);

        diagnostics.Should().BeEmpty();
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string source)
    {
        var compilation = CreateCompilation(source, LocalInternalAttributeSource);
        var analyzer = new ClientReceiverAccessAttributeAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var analyzerDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        return analyzerDiagnostics.Where(d => d.Id == DiagnosticId).ToImmutableArray();
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var syntaxTrees = sources
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName: "GuardAnalyzerTestAssembly",
            syntaxTrees: syntaxTrees,
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var references = trustedAssembliesPaths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // PulseRPC.Abstractions：提供 IPulseHub / ChannelAttribute / ClientFacingAttribute / AuthorizeAttribute / AllowAnonymousAttribute / RoleTypes。
        references.Add(MetadataReference.CreateFromFile(typeof(global::PulseRPC.IPulseHub).Assembly.Location));

        return references.ToArray();
    }
}
