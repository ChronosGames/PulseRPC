using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using PulseRPC.Analyzers;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public sealed class PackageBoundaryAnalyzerTests
{
    [Fact]
    public async Task AbstractionsReferencingImplementationAssemblyMustFail()
    {
        var implementationReference = CreateAssemblyReference("PulseRPC.Server");

        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Abstractions.Contracts; public interface IContract { }",
            additionalReferences: new[] { implementationReference });

        diagnostics.Should().ContainSingle(item =>
            item.Id == PackageBoundaryAnalyzer.ForbiddenReferenceDiagnosticId
            && item.GetMessage().Contains("PulseRPC.Server", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AbstractionsContractInCanonicalNamespaceMustPass()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Abstractions.Contracts; public interface IContract { }");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task AbstractionsNewPublicTypeInLegacyNamespaceMustFail()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Client; public interface INewChannel { }");

        diagnostics.Should().ContainSingle(item =>
            item.Id == PackageBoundaryAnalyzer.NamespaceOwnershipDiagnosticId);
    }

    [Fact]
    public async Task ShippedLegacyTypeMustRemainCompatible()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Client; public interface ILegacyChannel { }",
            shippedApi: "PulseRPC.Client.ILegacyChannel");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task UnshippedLegacyTypeMustNotBypassNamespaceRule()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Client; public interface INewChannel { }",
            unshippedApi: "PulseRPC.Client.INewChannel");

        diagnostics.Should().ContainSingle(item =>
            item.Id == PackageBoundaryAnalyzer.NamespaceOwnershipDiagnosticId);
    }

    [Fact]
    public async Task AbstractionsNewPublicImplementationTypeMustFail()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Abstractions.Runtime; public sealed class RuntimeManager { }");

        diagnostics.Should().ContainSingle(item =>
            item.Id == PackageBoundaryAnalyzer.PublicImplementationDiagnosticId);
    }

    [Fact]
    public async Task AbstractionsPublicContractImplementationMustFail()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Abstractions.Runtime; public interface IRuntime { } public sealed class Runtime : IRuntime { }");

        diagnostics.Should().ContainSingle(item =>
            item.Id == PackageBoundaryAnalyzer.PublicImplementationDiagnosticId);
    }

    [Fact]
    public async Task AbstractionsPublicDtoMustPass()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Abstractions",
            "namespace PulseRPC.Abstractions.Clustering; public sealed class LeaseSnapshot { public string Id { get; init; } = string.Empty; }");

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task SharedNewPublicTypeInAbstractionsNamespaceMustFail()
    {
        var diagnostics = await RunAnalyzerAsync(
            "PulseRPC.Shared",
            "namespace PulseRPC.Abstractions.Transport; public sealed class NewTransportOptions { }");

        diagnostics.Should().ContainSingle(item =>
            item.Id == PackageBoundaryAnalyzer.NamespaceOwnershipDiagnosticId);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(
        string assemblyName,
        string source,
        IReadOnlyCollection<MetadataReference>? additionalReferences = null,
        string? shippedApi = null,
        string? unshippedApi = null)
    {
        var references = GetMetadataReferences().ToList();
        if (additionalReferences is not null)
        {
            references.AddRange(additionalReferences);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var additionalFiles = ImmutableArray.CreateBuilder<AdditionalText>();
        if (shippedApi is not null)
        {
            additionalFiles.Add(new InMemoryAdditionalText("PublicAPI.Shipped.txt", shippedApi));
        }

        if (unshippedApi is not null)
        {
            additionalFiles.Add(new InMemoryAdditionalText("PublicAPI.Unshipped.txt", unshippedApi));
        }

        var analyzerOptions = new AnalyzerOptions(additionalFiles.ToImmutable());
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new PackageBoundaryAnalyzer()),
            analyzerOptions);

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static MetadataReference CreateAssemblyReference(string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText("public sealed class ImplementationMarker { }") },
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("PulseRPC.", StringComparison.Ordinal))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
