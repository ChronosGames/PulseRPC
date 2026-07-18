using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public sealed class UnityGeneratorCompatibilityTests
{
    [Fact]
    public void ClientNuGet_MustPackRoslyn43UnityGeneratorWithoutIdeDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var clientProjectPath = Path.Combine(
            repositoryRoot,
            "src",
            "PulseRPC.Client",
            "PulseRPC.Client.csproj");
        var clientProject = XDocument.Load(clientProjectPath);

        var unityProjectReference = clientProject
            .Descendants("ProjectReference")
            .SingleOrDefault(element =>
                NormalizePath((string?)element.Attribute("Include"))
                    .EndsWith("PulseRPC.Client.SourceGenerator.Unity/PulseRPC.Client.SourceGenerator.Unity.csproj", StringComparison.Ordinal));
        unityProjectReference.Should().NotBeNull(
            "the Unity-compatible generator must be built before packing PulseRPC.Client");

        var packedAnalyzer = clientProject
            .Descendants("None")
            .SingleOrDefault(element =>
                NormalizePath((string?)element.Attribute("PackagePath")) == "analyzers/dotnet/roslyn4.3/cs");
        packedAnalyzer.Should().NotBeNull("NuGetForUnity selects analyzers by the Unity Roslyn version");
        NormalizePath((string?)packedAnalyzer!.Attribute("Include"))
            .Should().Contain("PulseRPC.Client.SourceGenerator.Unity/bin/$(Configuration)/netstandard2.0/");

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Unable to determine the test build configuration.");
        var generatorPath = Path.Combine(
            repositoryRoot,
            "src",
            "PulseRPC.Client.SourceGenerator.Unity",
            "bin",
            configuration,
            "netstandard2.0",
            "PulseRPC.Client.SourceGenerator.dll");

        File.Exists(generatorPath).Should().BeTrue(
            "the Unity-compatible generator project must be part of the client build graph");

        var references = ReadAssemblyReferences(generatorPath);
        references.Should().Equal(
            new KeyValuePair<string, Version>("Microsoft.CodeAnalysis", new Version(4, 3, 0, 0)),
            new KeyValuePair<string, Version>("Microsoft.CodeAnalysis.CSharp", new Version(4, 3, 0, 0)),
            new KeyValuePair<string, Version>("System.Collections.Immutable", new Version(6, 0, 0, 0)),
            new KeyValuePair<string, Version>("netstandard", new Version(2, 0, 0, 0)));
    }

    private static IReadOnlyList<KeyValuePair<string, Version>> ReadAssemblyReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadataReader = peReader.GetMetadataReader();

        return metadataReader.AssemblyReferences
            .Select(handle => metadataReader.GetAssemblyReference(handle))
            .Select(reference => new KeyValuePair<string, Version>(
                metadataReader.GetString(reference.Name),
                reference.Version))
            .OrderBy(reference => reference.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PulseRPC.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate the PulseRPC repository root.");
    }

    private static string NormalizePath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/');
}
