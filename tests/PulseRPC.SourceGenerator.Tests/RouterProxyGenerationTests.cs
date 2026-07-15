using System;
using System.Linq;
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
