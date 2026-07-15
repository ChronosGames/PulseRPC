using System;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public sealed class AotPreservationGenerationTests
{
    private const string Source = """
        #nullable enable
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using MemoryPack;
        using PulseRPC;

        namespace AotPreservationTestNs
        {
            [MemoryPackable]
            public partial class AotPayload
            {
                public int Sequence { get; set; }
                public List<string> Tags { get; set; } = new List<string>();
            }

            [MemoryPackable]
            public partial class AotReply
            {
                public bool Accepted { get; set; }
            }

            [Channel("AOT_CI")]
            public interface IAotHub : IPulseHub
            {
                Task PingAsync(CancellationToken cancellationToken = default);
                Task<AotReply> RoundTripAsync(AotPayload payload, CancellationToken cancellationToken = default);
                Task SendPairAsync(AotPayload payload, int sequence, CancellationToken cancellationToken = default);
            }

            [Channel("CLIENT")]
            public interface IAotReceiver : IPulseHub
            {
                Task OnPayloadAsync(AotPayload payload, CancellationToken cancellationToken = default);
                Task<AotReply> ConfirmAsync(AotPayload payload, CancellationToken cancellationToken = default);
            }

            [PulseClientGeneration(typeof(IAotHub))]
            [PulseClientGeneration(typeof(IAotReceiver))]
            public static class AotGenerationMarker
            {
            }
        }
        """;

    [Fact]
    public void ClientGenerator_MustEmitDeterministicUnityAotRootsForHubReceiverAndMemoryPackClosures()
    {
        var memoryPackReference = MetadataReference.CreateFromFile(
            typeof(global::MemoryPack.MemoryPackableAttribute).Assembly.Location);
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            Source,
            "AotPreservationTestAssembly",
            memoryPackReference);
        var unityParseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp9,
            preprocessorSymbols: new[] { "UNITY_2019_1_OR_NEWER" });
        var sourceTree = compilation.SyntaxTrees.Single();
        compilation = compilation
            .ReplaceSyntaxTree(sourceTree, CSharpSyntaxTree.ParseText(Source, unityParseOptions))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                "namespace UnityEngine.Scripting { public sealed class PreserveAttribute : System.Attribute { } }",
                unityParseOptions));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::PulseRPC.Generator.ServiceProxyGenerator().AsSourceGenerator() },
            parseOptions: unityParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var driverDiagnostics);
        var result = driver.GetRunResult();

        driverDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("AOT preservation generator driver must not report errors");
        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("AOT preservation generation must not report generator errors");
        outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("generated Hub, Receiver, and MemoryPack AOT roots must compile");

        var generatedSource = result.Results
            .SelectMany(generator => generator.GeneratedSources)
            .Single(source => source.HintName == "PulseRPC.Client.Generated.AotPreservation.g.cs")
            .SourceText
            .ToString();

        generatedSource.Should().Contain("[UnityEngine.Scripting.Preserve]");
        generatedSource.Should().Contain("ClientChannelGenericExtensions.GetHub<global::AotPreservationTestNs.IAotHub>");
        generatedSource.Should().Contain("ClientChannelGenericExtensions.RegisterReceiver<global::AotPreservationTestNs.IAotReceiver>");
        generatedSource.Should().Contain("typeof(global::AotPreservationTestNs.IAotHubStub)");
        generatedSource.Should().Contain("typeof(global::AotPreservationTestNs.AotReceiverDispatcher)");

        generatedSource.Should().Contain("default(global::AotPreservationTestNs.AotPayload)");
        generatedSource.Should().Contain("Deserialize<global::AotPreservationTestNs.AotPayload>");
        generatedSource.Should().Contain("default((global::AotPreservationTestNs.AotPayload, int))");
        generatedSource.Should().Contain("Deserialize<(global::AotPreservationTestNs.AotPayload, int)>");
        generatedSource.Should().Contain("default(global::AotPreservationTestNs.AotReply)");
        generatedSource.Should().Contain("Deserialize<global::AotPreservationTestNs.AotReply>");
        generatedSource.Should().Contain("default(global::PulseRPC.EmptyResponse)");
        generatedSource.Should().Contain("Deserialize<global::PulseRPC.EmptyResponse>");

        CSharpSyntaxTree.ParseText(generatedSource, new CSharpParseOptions(LanguageVersion.CSharp9))
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("Unity-facing AOT roots must stay compatible with C# 9");
    }
}
