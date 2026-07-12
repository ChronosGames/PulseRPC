using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public class MethodOverloadIdentityTests
{
    private const string NonOverloadedHubSource = """
        #nullable enable
        using System.Threading.Tasks;
        using PulseRPC;
        using PulseRPC.Protocol;

        namespace OverloadIdentityContracts;

        [Channel("TestServer")]
        public interface ICompatibilityHub : IPulseHub
        {
            [Protocol("0x4101")]
            Task<string> PingAsync(string value);
        }

        [PulseClientGeneration(typeof(ICompatibilityHub))]
        public partial class ClientRegistrar
        {
        }
        """;

    private const string OverloadedHubSource = """
        #nullable enable
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using MemoryPack;
        using PulseRPC;
        using PulseRPC.Protocol;

        namespace OverloadIdentityContracts.Left
        {
            [MemoryPackable]
            public partial class Payload
            {
                public int Value { get; set; }
            }
        }

        namespace OverloadIdentityContracts.Right
        {
            [MemoryPackable]
            public partial class Payload
            {
                public string? Value { get; set; }
            }
        }

        namespace OverloadIdentityContracts
        {
            public interface IBaseOverloadFacet
            {
                [Protocol("0x3105")]
                Task<string> FooAsync(Left.Payload value);

                [Protocol("0x3106")]
                Task<string> FooAsync(int[] values);
            }

            [Channel("TestServer")]
            public interface IOverloadHub : IPulseHub, IBaseOverloadFacet
            {
                [Protocol("0x3101")]
                Task<string> FooAsync(int value);

                [Protocol("0x3102")]
                Task<string> FooAsync(string value);

                [Protocol("0x3103")]
                Task<string> FooAsync(Right.Payload value);

                [Protocol("0x3104")]
                Task<string> FooAsync(List<Left.Payload> values, CancellationToken cancellationToken = default);
            }

            [PulseClientGeneration(typeof(IOverloadHub))]
            public partial class ClientRegistrar
            {
            }
        }
        """;

    [Fact]
    public void HubOverloads_MustHaveStableDistinctGeneratedIdentity_AndCompileOnBothEnds()
    {
        var clientAssembly = typeof(global::PulseRPC.Client.IPulseClient).Assembly.Location;
        var serverAssembly = typeof(global::PulseRPC.Server.IServiceRoutingTable).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(serverAssembly)!;
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            OverloadedHubSource,
            "MethodOverloadIdentityCompilation",
            MetadataReference.CreateFromFile(clientAssembly),
            MetadataReference.CreateFromFile(serverAssembly),
            MetadataReference.CreateFromFile(Path.Combine(assemblyDirectory, "PulseRPC.Shared.dll")),
            MetadataReference.CreateFromFile(typeof(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location));

        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("重载契约源码本身必须有效");

        var clientResult = RunClientGeneratorAndCompile(compilation, out var clientOutput);
        var serverResult = RunServerGeneratorAndCompile(compilation, out var serverOutput);

        AssertNoErrors(clientResult, clientOutput, "客户端");
        AssertNoErrors(serverResult, serverOutput, "服务端");

        var clientGeneratedText = JoinGeneratedText(clientResult);
        var serverGeneratedText = JoinGeneratedText(serverResult);
        foreach (var protocolId in new[] { "3101", "3102", "3103", "3104", "3105", "3106" })
        {
            clientGeneratedText.Should().Contain($"0x{protocolId}", $"客户端查找键不得覆盖 FooAsync 重载 0x{protocolId}");
            serverGeneratedText.Should().Contain($"0x{protocolId}");
        }

        AssertDistinctGeneratedNames(
            clientGeneratedText,
            @"private const ushort (ProtocolId_FooAsync\w*)\s*=",
            "客户端协议常量");
        AssertDistinctGeneratedNames(
            serverGeneratedText,
            @"public ValueTask<object\?> (Invoke_FooAsync\w*_Async)\(",
            "服务端 invoker");
        AssertDistinctGeneratedNames(
            serverGeneratedText,
            @"private (?:async )?ValueTask<object\?> (RouteById_OverloadHub_FooAsync\w*)\(",
            "服务端 router helper",
            expectedCount: 12);
        AssertDistinctGeneratedNames(
            serverGeneratedText,
            @"public sealed class (\w*FooAsync\w*_ResponseSerializer)\s*:",
            "服务端 response serializer");

        var csharp9 = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (var generatedSource in clientResult.Results.SelectMany(result => result.GeneratedSources))
        {
            CSharpSyntaxTree.ParseText(generatedSource.SourceText, csharp9).GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Should().BeEmpty($"{generatedSource.HintName} 必须兼容 C# 9");
        }
    }

    [Fact]
    public void NonOverloadedHub_MustPreserveLegacyPublicGeneratedNames()
    {
        var clientAssembly = typeof(global::PulseRPC.Client.IPulseClient).Assembly.Location;
        var serverAssembly = typeof(global::PulseRPC.Server.IServiceRoutingTable).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(serverAssembly)!;
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            NonOverloadedHubSource,
            "NonOverloadedPublicNameCompatibilityCompilation",
            MetadataReference.CreateFromFile(clientAssembly),
            MetadataReference.CreateFromFile(serverAssembly),
            MetadataReference.CreateFromFile(Path.Combine(assemblyDirectory, "PulseRPC.Shared.dll")),
            MetadataReference.CreateFromFile(typeof(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location));

        var clientResult = RunClientGeneratorAndCompile(compilation, out var clientOutput);
        var serverResult = RunServerGeneratorAndCompile(compilation, out var serverOutput);

        AssertNoErrors(clientResult, clientOutput, "客户端");
        AssertNoErrors(serverResult, serverOutput, "服务端");

        var serverGeneratedText = JoinGeneratedText(serverResult);
        serverGeneratedText.Should().Contain("public const ushort CompatibilityHub_PingAsync = 0x4101");
        serverGeneratedText.Should().Contain("public ValueTask<object?> Invoke_PingAsync_Async(");
        serverGeneratedText.Should().Contain("public sealed class ICompatibilityHub_PingAsync_ResponseSerializer");
        serverGeneratedText.Should().NotContain("public const ushort CompatibilityHub_PingAsync_4101");
        serverGeneratedText.Should().NotContain("Invoke_PingAsync_4101_Async");
        serverGeneratedText.Should().NotContain("ICompatibilityHub_PingAsync_4101_ResponseSerializer");
    }

    private static GeneratorDriverRunResult RunClientGeneratorAndCompile(
        CSharpCompilation compilation,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new global::PulseRPC.Generator.ServiceProxyGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out _);
        return driver.GetRunResult();
    }

    private static GeneratorDriverRunResult RunServerGeneratorAndCompile(
        CSharpCompilation compilation,
        out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out _);
        return driver.GetRunResult();
    }

    private static void AssertNoErrors(
        GeneratorDriverRunResult result,
        Compilation outputCompilation,
        string side)
    {
        result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty($"{side}生成器不应报告错误");
        outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty($"{side}生成输出必须真实编译通过");
    }

    private static string JoinGeneratedText(GeneratorDriverRunResult result)
        => string.Join(
            "\n\n",
            result.Results.SelectMany(generator => generator.GeneratedSources)
                .Select(source => source.SourceText.ToString()));

    private static void AssertDistinctGeneratedNames(
        string generatedText,
        string pattern,
        string description,
        int expectedCount = 6)
    {
        var names = Regex.Matches(generatedText, pattern)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        names.Should().HaveCount(expectedCount, $"应为每个重载生成独立的{description}");
        names.Distinct(StringComparer.Ordinal).Should().HaveCount(expectedCount, $"{description}不得碰撞");
    }
}
