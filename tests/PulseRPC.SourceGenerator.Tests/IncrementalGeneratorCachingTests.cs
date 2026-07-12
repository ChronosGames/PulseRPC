using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public sealed class IncrementalGeneratorCachingTests
{
    private const string Source = """
        using System.Threading.Tasks;
        using PulseRPC;

        namespace IncrementalContract;

        [Channel("main")]
        public interface IIncrementalHub : IPulseHub
        {
            Task<int> EchoAsync(int value);
        }

        [PulseClientGeneration(typeof(IIncrementalHub))]
        public partial class ClientRegistrar
        {
        }
        """;

    [Fact]
    public void ClientGenerator_UnchangedCompilation_MustReuseIncrementalOutputs()
    {
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(Source, "ClientIncrementalCaching");

        AssertSecondRunIsCached(
            compilation,
            new global::PulseRPC.Generator.ServiceProxyGenerator().AsSourceGenerator());
    }

    [Fact]
    public void ServerGenerator_UnchangedCompilation_MustReuseIncrementalOutputs()
    {
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(Source, "ServerIncrementalCaching");

        AssertSecondRunIsCached(
            compilation,
            new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator().AsSourceGenerator());
    }

    private static void AssertSecondRunIsCached(CSharpCompilation compilation, ISourceGenerator generator)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation);

        var result = driver.GetRunResult().Results.Single();
        var outputReasons = result.TrackedOutputSteps.Values
            .SelectMany(steps => steps)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();
        var trackedDetails = string.Join(", ", result.TrackedSteps
            .SelectMany(pair => pair.Value.SelectMany(step => step.Outputs.Select(output =>
                $"{pair.Key}={output.Reason}"))));

        outputReasons.Should().NotBeEmpty();
        outputReasons.Should().OnlyContain(reason =>
            reason == IncrementalStepRunReason.Cached ||
            reason == IncrementalStepRunReason.Unchanged,
            "unchanged compilations should not regenerate outputs; tracked steps: {0}",
            trackedDetails);
    }
}
