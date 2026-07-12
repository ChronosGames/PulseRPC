using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

/// <summary>
/// 回归测试：生成的 Fan-out 代理（ReceiverProxy/HubClients）必须经 <c>IPulseRouter.SendAsync</c>/<c>AskAsync</c>
/// 投递，而不是直接遍历 <c>IServerChannel</c> 列表（§P3 fanout-via-router）。
/// </summary>
/// <remarks>
/// <c>[Channel("CLIENT")]</c> 接收器接口是通过 <c>PulseRPCSourceGenerator.ScanAssemblyForReceivers</c>
/// 扫描<strong>已引用的程序集</strong>（而非当前编译单元的语法树）发现的——这与真实项目结构一致
/// （接收器接口通常声明在被 Server 项目引用的 Shared 项目里）。因此本测试需要先把接收器接口单独编译
/// 成一个引用程序集，再在第二个（包含至少一个普通服务端 Hub 的）编译单元中引用它并驱动生成器。
/// </remarks>
public class ReceiverFanoutViaRouterTests
{
    private const string SharedReceiverSource = """
        #nullable enable
        using System.Threading.Tasks;
        using PulseRPC;

        namespace FanoutViaRouterTestNs;

        [Channel("CLIENT")]
        public interface IChatClientHub : IPulseHub
        {
            Task OnMessageAsync(string message);

            Task<string> PingAsync(string request);
        }
        """;

    // 生成器在 serviceModels（服务端 Hub）为空时会提前返回、不再扫描 Receiver（见
    // PulseRPCSourceGenerator.ExecuteGeneration 的 PULSE001 早退分支），因此本测试需要
    // 同时提供至少一个普通服务端 Hub 接口，才能触发 [Channel("CLIENT")] 接收器的扫描与生成。
    private const string ServerSource = """
        #nullable enable
        using System.Threading.Tasks;
        using PulseRPC;

        namespace FanoutViaRouterTestNs;

        [Channel("TestServer")]
        public interface IDummyHub : IPulseHub
        {
            Task PingServerAsync();
        }
        """;

    [Fact]
    public void GeneratedReceiverProxy_MustRouteFanoutThroughIPulseRouter()
    {
        var sharedReference = ProtocolIdConsistencyTestsHelpers.CompileToMetadataReference(SharedReceiverSource, "FanoutViaRouterSharedAssembly");
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(ServerSource, "FanoutViaRouterServerAssembly", sharedReference);
        var generatedText = ProtocolIdConsistencyTestsHelpers.RunServerGenerator(compilation);

        // 代理类必须持有 IPulseRouter + PulseAddress 列表，而不是 IServerChannel 列表。
        generatedText.Should().Contain("private readonly IPulseRouter _router;");
        generatedText.Should().Contain("private readonly IReadOnlyList<PulseAddress> _addresses;");
        generatedText.Should().NotContain("IReadOnlyList<IServerChannel> _targets");

        // 单向推送方法必须经路由器投递。
        generatedText.Should().Contain("_router.SendAsync(_addresses[0]");

        // 反向 Ask 方法必须经路由器投递，而不是直接调用 IServerChannel.InvokeClientAsync。
        generatedText.Should().Contain("_router.AskAsync(_addresses[0]");
        generatedText.Should().NotContain("_targets[0].InvokeClientAsync");

        // HubClients 的单目标选择器必须直接映射为对应的 PulseAddress 工厂方法。
        generatedText.Should().Contain("PulseAddress.AllClients(\"ChatClientHub\")");
        generatedText.Should().Contain("PulseAddress.Connection(\"ChatClientHub\", connectionId)");
        generatedText.Should().Contain("PulseAddress.Group(\"ChatClientHub\", groupName)");
        generatedText.Should().Contain("PulseAddress.User(\"ChatClientHub\", userId)");
        generatedText.Should().Contain("PulseAddress.Except(\"ChatClientHub\", connectionId)");

        // HubContext/HubClients 构造函数必须接收 IPulseRouter 依赖（交由 DI 自动注入）。
        generatedText.Should().Contain("IPulseRouter router,");
    }
}

/// <summary>
/// 复用 <see cref="ProtocolIdConsistencyTests"/> 的编译/生成器驱动辅助逻辑。
/// </summary>
internal static class ProtocolIdConsistencyTestsHelpers
{
    public static CSharpCompilation CreateCompilation(string source, string assemblyName = "ReceiverFanoutViaRouterTestAssembly", params MetadataReference[] extraReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));

        return CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences().Concat(extraReferences),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>
    /// 把一段源码单独编译为程序集并返回其 <see cref="MetadataReference"/>，
    /// 用于模拟"接收器接口声明在被引用的 Shared 项目里"的真实项目结构。
    /// </summary>
    public static MetadataReference CompileToMetadataReference(string source, string assemblyName)
    {
        var compilation = CreateCompilation(source, assemblyName);

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Success.Should().BeTrue(
            $"辅助编译单元 '{assemblyName}' 应能成功编译：{string.Join("; ", emitResult.Diagnostics.Select(d => d.ToString()))}");

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    public static string RunServerGenerator(CSharpCompilation compilation)
    {
        var result = RunServerGeneratorRaw(compilation);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("服务端生成器不应报告编译错误诊断");

        return string.Join("\n\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    public static GeneratorDriverRunResult RunServerGeneratorRaw(CSharpCompilation compilation)
    {
        var generator = new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }

    public static string RunClientGenerator(CSharpCompilation compilation)
    {
        var result = RunClientGeneratorRaw(compilation);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("客户端生成器不应报告编译错误诊断");

        return string.Join("\n\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    public static GeneratorDriverRunResult RunClientGeneratorRaw(CSharpCompilation compilation)
    {
        var generator = new global::PulseRPC.Generator.ServiceProxyGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(System.IO.Path.PathSeparator);

        var references = trustedAssembliesPaths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(global::PulseRPC.IPulseHub).Assembly.Location));

        return references.ToArray();
    }
}
