using System;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public sealed class RouterProxyGenerationTests
{
    private const string ConsumerOnlySource = """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;
        using MemoryPack;
        using PulseRPC;

        namespace RouterProxyTestNs;

        [MemoryPackable]
        public partial class GameCommand
        {
            public int Sequence { get; set; }
        }

        [MemoryPackable]
        public partial class GameReply
        {
            public bool Accepted { get; set; }
        }

        [Channel("GAME")]
        [PulseHub(Provide = false, Consume = true)]
        public interface IGameHub : IPulseHub
        {
            Task<GameReply> ExecuteAsync(GameCommand command, CancellationToken cancellationToken = default);
            ValueTask NotifyAsync(GameCommand command, int sequence, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ConsumeOnlyHub_MustGenerateTypedRouterProxyWithoutServerRegistrationSideEffects()
    {
        var compilation = CreateCompilation(ConsumerOnlySource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        var result = driver.GetRunResult();

        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("consume-only Hub generation must succeed");
        outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated router proxy must compile");

        var generatedText = string.Join(
            "\n\n",
            result.Results.SelectMany(generator => generator.GeneratedSources)
                .Select(source => source.SourceText.ToString()));

        generatedText.Should().Contain("public sealed class GameHubRouterProxy : RouterProxyTestNs.IGameHub");
        generatedText.Should().Contain("private readonly IPulseRouter _router;");
        generatedText.Should().Contain("private readonly PulseAddress _address;");
        generatedText.Should().Contain("PulseAddress.Actor(\"GameHub\", key, nodeId)");
        generatedText.Should().Contain("_router.AskAsync(_address, Protocol_ExecuteAsync");
        generatedText.Should().Contain("_router.SendAsync(_address, Protocol_NotifyAsync");
        generatedText.Should().Contain("MemoryPackSerializer.Serialize((command, sequence))");
        generatedText.Should().Contain("MemoryPackSerializer.Deserialize<global::RouterProxyTestNs.GameReply>");

        generatedText.Should().NotContain("[System.Runtime.CompilerServices.ModuleInitializer]");
        generatedText.Should().NotContain("ServiceRoutingTableRegistry.Register");
        generatedText.Should().NotContain("ResponseSerializerRegistry.Register");
        generatedText.Should().NotContain("ServiceManifestRegistry.Register");
    }

    [Fact]
    public void AssemblyLocalMarker_MustSelectConsumerRoleWithoutChangingSharedContract()
    {
        const string sharedContract = """
            #nullable enable
            using System.Threading;
            using System.Threading.Tasks;
            using PulseRPC;

            namespace AssemblyRoleTestNs;

            [Channel("GAME")]
            public interface IGameHub : IPulseHub
            {
                Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);
            }
            """;
        const string providerSource = """
            namespace AssemblyRoleProvider;
            public static class ProviderMarker { }
            """;
        const string consumerSource = """
            using PulseRPC.Abstractions;
            using AssemblyRoleTestNs;
            [assembly: PulseRouterGeneration(typeof(IGameHub))]

            namespace AssemblyRoleConsumer;
            public static class ConsumerMarker { }
            """;

        var sharedReference = ProtocolIdConsistencyTestsHelpers.CompileToMetadataReference(
            sharedContract,
            "AssemblyRoleSharedContracts");

        var providerCompilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            providerSource,
            "AssemblyRoleProvider",
            sharedReference);
        var providerGenerated = ProtocolIdConsistencyTestsHelpers.RunServerGenerator(providerCompilation);

        var consumerCompilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            consumerSource,
            "AssemblyRoleConsumer",
            sharedReference);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(consumerCompilation, out var consumerOutput, out _);
        var consumerResult = driver.GetRunResult();
        var consumerGenerated = string.Join(
            "\n\n",
            consumerResult.Results.SelectMany(result => result.GeneratedSources)
                .Select(source => source.SourceText.ToString()));

        consumerResult.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        consumerOutput.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("a consumer project must compile without provider-generated types");

        providerGenerated.Should().Contain("ServiceRoutingTableRegistry.Register");
        providerGenerated.Should().Contain("[System.Runtime.CompilerServices.ModuleInitializer]");
        providerGenerated.Should().NotContain("class GameHubRouterProxy");

        consumerGenerated.Should().Contain("public sealed class GameHubRouterProxy : AssemblyRoleTestNs.IGameHub");
        consumerGenerated.Should().NotContain("[System.Runtime.CompilerServices.ModuleInitializer]");
        consumerGenerated.Should().NotContain("ServiceRoutingTableRegistry.Register");
        consumerGenerated.Should().NotContain("ResponseSerializerRegistry.Register");
        consumerGenerated.Should().NotContain("ServiceManifestRegistry.Register");

        var consumerProtocolId = Regex.Match(
            consumerGenerated,
            @"Protocol_ExecuteAsync = 0x([0-9A-F]{4})").Groups[1].Value;
        consumerProtocolId.Should().NotBeEmpty();
        providerGenerated.Should().Contain($"0x{consumerProtocolId}",
            "provider and consumer generation must use the same wire protocol ID");
    }

    [Fact]
    public void AssemblyLocalMarker_WithNonHubTarget_MustReportDiagnostic()
    {
        const string source = """
            using PulseRPC.Abstractions;
            [assembly: PulseRouterGeneration(typeof(string))]

            namespace InvalidAssemblyRoleConsumer;
            public static class ConsumerMarker { }
            """;
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            source,
            "InvalidAssemblyRoleConsumer");

        var result = ProtocolIdConsistencyTestsHelpers.RunServerGeneratorRaw(compilation);
        var generatedText = string.Join(
            "\n\n",
            result.Results.SelectMany(generator => generator.GeneratedSources)
                .Select(generated => generated.SourceText.ToString()));

        result.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == "PULSE011" && diagnostic.Severity == DiagnosticSeverity.Error);
        generatedText.Should().NotContain("[System.Runtime.CompilerServices.ModuleInitializer]");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var memoryPackReference = MetadataReference.CreateFromFile(
            typeof(global::MemoryPack.MemoryPackableAttribute).Assembly.Location);
        return ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            source,
            "RouterProxyTestAssembly",
            memoryPackReference);
    }
}
