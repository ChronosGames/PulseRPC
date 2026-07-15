using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public class GeneratorContractDiagnosticTests
{
    [Theory]
    [InlineData("\"not-hex\"")]
    [InlineData("\"   \"")]
    [InlineData("\"0x10000\"")]
    [InlineData("null")]
    public void InvalidStringProtocol_MustReportErrorsWithoutFallingBackToAutomaticId(string attributeArgument)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using PulseRPC;
            using PulseRPC.Protocol;

            namespace InvalidProtocolContract;

            [Channel("TestServer")]
            public interface IInvalidProtocolHub : IPulseHub
            {
                [Protocol({{attributeArgument}})]
                Task<string> PingAsync(string value);
            }

            [PulseClientGeneration(typeof(IInvalidProtocolHub))]
            public partial class ClientRegistrar
            {
            }
            """;

        var compilation = CreateRuntimeAwareCompilation(source, $"InvalidProtocol_{attributeArgument.GetHashCode():X8}");
        var method = compilation.GetTypeByMetadataName("InvalidProtocolContract.IInvalidProtocolHub")!
            .GetMembers("PingAsync")
            .OfType<IMethodSymbol>()
            .Single();
        var automaticId = global::PulseRPC.Generator.Generators.ProtocolIdGenerator.GenerateProtocolId(method);

        var clientResult = ProtocolIdConsistencyTestsHelpers.RunClientGeneratorRaw(compilation);
        var serverResult = ProtocolIdConsistencyTestsHelpers.RunServerGeneratorRaw(compilation);

        clientResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PRPC003" && diagnostic.Severity == DiagnosticSeverity.Error);
        serverResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PULSE007" && diagnostic.Severity == DiagnosticSeverity.Error);

        JoinGeneratedText(clientResult).Should().NotContain($"0x{automaticId:X4}");
        JoinGeneratedText(serverResult).Should().NotContain($"0x{automaticId:X4}");
    }

    [Fact]
    public void CancellationTokenOnlyOverloadDifference_MustReportWireSignatureCollisionOnBothEnds()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using PulseRPC;

            namespace CancellationCollisionContract;

            [Channel("TestServer")]
            public interface ICancellationCollisionHub : IPulseHub
            {
                Task<string> EchoAsync(string value);
                Task<string> EchoAsync(string value, CancellationToken cancellationToken);
            }

            [PulseClientGeneration(typeof(ICancellationCollisionHub))]
            public partial class ClientRegistrar
            {
            }
            """;

        var compilation = CreateRuntimeAwareCompilation(source, "CancellationWireSignatureCollisionCompilation");
        var clientResult = ProtocolIdConsistencyTestsHelpers.RunClientGeneratorRaw(compilation);
        var serverResult = ProtocolIdConsistencyTestsHelpers.RunServerGeneratorRaw(compilation);

        clientResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PRPC005" && diagnostic.Severity == DiagnosticSeverity.Error);
        serverResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PULSE009" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MultipleCancellationTokens_MustBeRejectedOnBothEnds()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using PulseRPC;

            namespace MultipleCancellationTokenContract;

            [Channel("TestServer")]
            public interface IMultipleCancellationTokenHub : IPulseHub
            {
                Task<string> EchoAsync(string value, CancellationToken first, CancellationToken second);
            }

            [PulseClientGeneration(typeof(IMultipleCancellationTokenHub))]
            public partial class ClientRegistrar
            {
            }
            """;

        var compilation = CreateRuntimeAwareCompilation(source, "MultipleCancellationTokenCompilation");
        var clientResult = ProtocolIdConsistencyTestsHelpers.RunClientGeneratorRaw(compilation);
        var serverResult = ProtocolIdConsistencyTestsHelpers.RunServerGeneratorRaw(compilation);

        clientResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PRPC006" && diagnostic.Severity == DiagnosticSeverity.Error);
        serverResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PULSE010" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ReceiverOverloads_MustGenerateDistinctProtocolMembersAndCompileOnBothEnds()
    {
        const string receiverSource = """
            using System.Threading.Tasks;
            using PulseRPC;
            using PulseRPC.Protocol;

            namespace ReceiverOverloadContract;

            [Channel("CLIENT")]
            public interface IOverloadedReceiver : IPulseHub
            {
                [Protocol("0x5101")]
                Task OnChangedAsync(int value);

                [Protocol("0x5102")]
                Task OnChangedAsync(string value);
            }

            [PulseClientGeneration(typeof(IOverloadedReceiver))]
            public partial class ClientRegistrar
            {
            }
            """;
        const string serverSource = """
            using System.Threading.Tasks;
            using PulseRPC;

            namespace ReceiverOverloadServer;

            [Channel("TestServer")]
            public interface IDummyHub : IPulseHub
            {
                Task PingAsync();
            }
            """;

        var clientCompilation = CreateRuntimeAwareCompilation(receiverSource, "ReceiverOverloadClientCompilation");
        var clientResult = RunClientGeneratorAndUpdateCompilation(clientCompilation, out var clientOutput);

        clientResult.Diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == "PRPC004" || diagnostic.Severity == DiagnosticSeverity.Error);
        var clientGenerated = JoinGeneratedText(clientResult);
        clientGenerated.Should().Contain("OverloadedReceiverDispatcher");
        clientGenerated.Should().Contain("ProtocolId_OnChangedAsync_5101");
        clientGenerated.Should().Contain("ProtocolId_OnChangedAsync_5102");
        clientGenerated.Should().Contain("dispatcher.RegisterTo(connectionContext)");
        clientGenerated.Should().NotContain("TODO: 实现通用连接获取逻辑");
        clientOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("Receiver 重载生成代码必须真实编译");
        AssertClientSourcesAreCSharp9(clientResult);

        var receiverReference = ProtocolIdConsistencyTestsHelpers.CompileToMetadataReference(
            receiverSource,
            "ReceiverOverloadSharedAssembly");
        var serverCompilation = CreateRuntimeAwareCompilation(
            serverSource,
            "ReceiverOverloadServerCompilation",
            receiverReference);
        var serverResult = RunServerGeneratorAndUpdateCompilation(serverCompilation, out var serverOutput);

        serverResult.Diagnostics.Should().NotContain(diagnostic =>
            diagnostic.Id == "PULSE008" || diagnostic.Severity == DiagnosticSeverity.Error);
        var serverGenerated = JoinGeneratedText(serverResult);
        serverGenerated.Should().Contain("OverloadedReceiverProxy");
        serverGenerated.Should().Contain("Protocol_OnChangedAsync_5101");
        serverGenerated.Should().Contain("Protocol_OnChangedAsync_5102");
        serverGenerated.Should().Contain("OverloadedReceiver_OnChangedAsync_5101");
        serverGenerated.Should().Contain("OverloadedReceiver_OnChangedAsync_5102");
        serverOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("服务端 Receiver 重载代理必须真实编译");
    }

    [Fact]
    public void ServiceFactoryMustForwardLoadBalancingOptionsToContextualRouting()
    {
        const string source = """
            using System.Threading.Tasks;
            using PulseRPC;

            namespace ContextualRoutingContract;

            [Channel("TestServer")]
            public interface IContextualHub : IPulseHub
            {
                Task<string> EchoAsync(string value);
            }

            [PulseClientGeneration(typeof(IContextualHub))]
            public partial class ClientRegistrar
            {
            }
            """;
        var compilation = CreateRuntimeAwareCompilation(source, "ContextualRoutingClientCompilation");
        var result = RunClientGeneratorAndUpdateCompilation(compilation, out var outputCompilation);

        JoinGeneratedText(result).Should().Contain(
            "RouteWithOptionsAsync(\"IContextualHub\", options, cancellationToken)");
        outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("生成的服务工厂必须把 StickyKey 和路由提示传入连接管理器");
        AssertClientSourcesAreCSharp9(result);
    }

    [Fact]
    public void GeneratedCompatibilityOptionsMustFailFastInsteadOfBeingIgnored()
    {
        const string source = """
            using System.Threading.Tasks;
            using PulseRPC;

            namespace GeneratedOptionsContract;

            [Channel("TestServer")]
            public interface IGeneratedOptionsHub : IPulseHub
            {
                Task<string> EchoAsync(string value);
            }

            [Channel("CLIENT")]
            public interface IGeneratedOptionsReceiver : IPulseHub
            {
                Task OnChangedAsync(string value);
            }

            [PulseClientGeneration(typeof(IGeneratedOptionsHub))]
            public partial class ClientRegistrar
            {
            }
            """;
        var compilation = CreateRuntimeAwareCompilation(source, "GeneratedCompatibilityOptionsCompilation");
        var result = RunClientGeneratorAndUpdateCompilation(compilation, out var outputCompilation);
        var generated = JoinGeneratedText(result);

        generated.Should().Contain(
            "ServiceProxyOptions is not consumed when connectionId is explicit. Pass null.");
        outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("generated compatibility checks must compile");
        AssertClientSourcesAreCSharp9(result);
    }

    private static CSharpCompilation CreateRuntimeAwareCompilation(
        string source,
        string assemblyName,
        params MetadataReference[] additionalReferences)
    {
        var serverAssembly = typeof(global::PulseRPC.Server.IServiceRoutingTable).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(serverAssembly)!;
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(global::PulseRPC.Client.IPulseClient).Assembly.Location),
            MetadataReference.CreateFromFile(serverAssembly),
            MetadataReference.CreateFromFile(Path.Combine(assemblyDirectory, "PulseRPC.Shared.dll")),
            MetadataReference.CreateFromFile(typeof(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location),
        }.Concat(additionalReferences).ToArray();

        return ProtocolIdConsistencyTestsHelpers.CreateCompilation(source, assemblyName, references);
    }

    private static GeneratorDriverRunResult RunClientGeneratorAndUpdateCompilation(
        CSharpCompilation compilation,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::PulseRPC.Generator.ServiceProxyGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _);
        return driver.GetRunResult();
    }

    private static GeneratorDriverRunResult RunServerGeneratorAndUpdateCompilation(
        CSharpCompilation compilation,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out _);
        return driver.GetRunResult();
    }

    private static string JoinGeneratedText(GeneratorDriverRunResult result)
        => string.Join(
            "\n\n",
            result.Results.SelectMany(generator => generator.GeneratedSources)
                .Select(source => source.SourceText.ToString()));

    private static void AssertClientSourcesAreCSharp9(GeneratorDriverRunResult result)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (var source in result.Results.SelectMany(generator => generator.GeneratedSources))
        {
            CSharpSyntaxTree.ParseText(source.SourceText, parseOptions).GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Should().BeEmpty($"{source.HintName} 必须兼容 C# 9");
        }
    }
}
