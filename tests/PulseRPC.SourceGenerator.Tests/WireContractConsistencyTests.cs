using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

/// <summary>
/// 回归测试：CancellationToken 是本地调用控制参数，不属于 wire payload；手动协议号在两端必须一致。
/// </summary>
public class WireContractConsistencyTests
{
    private const string CancellableHubSource = """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;
        using PulseRPC;

        namespace WireContractTestNs;

        [Channel("TestServer")]
        public interface ICancellableHub : IPulseHub
        {
            Task<string> EchoAsync(string value, CancellationToken cancellationToken = default);
        }

        [PulseClientGeneration(typeof(ICancellableHub))]
        public partial class ClientRegistrar
        {
        }
        """;

    private const string CancellableReceiverSource = """
        #nullable enable
        using System.Threading;
        using System.Threading.Tasks;
        using PulseRPC;

        namespace WireContractTestNs;

        [Channel("CLIENT")]
        [GenerateEventHandler]
        public interface ICallbackReceiver : IPulseHub
        {
            Task OnMessageAsync(string message, CancellationToken cancellationToken = default);

            Task<string> QueryAsync(string query, CancellationToken cancellationToken = default);
        }

        [PulseClientGeneration(typeof(ICallbackReceiver))]
        public partial class ClientRegistrar
        {
        }
        """;

    private const string DummyServerSource = """
        using System.Threading.Tasks;
        using PulseRPC;

        namespace WireContractTestNs;

        [Channel("TestServer")]
        public interface IDummyHub : IPulseHub
        {
            Task PingAsync();
        }
        """;

    [Fact]
    public void HubCancellationToken_MustNotBeSerialized_AndServerMustInjectDispatchToken()
    {
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(CancellableHubSource);

        var clientGeneratedText = ProtocolIdConsistencyTestsHelpers.RunClientGenerator(compilation);
        var serverGeneratedText = ProtocolIdConsistencyTestsHelpers.RunServerGenerator(compilation);

        clientGeneratedText.Should().Contain("MemoryPack.MemoryPackSerializer.Serialize(__buffer__, value);");
        clientGeneratedText.Should().Contain("cancellationToken: cancellationToken");
        clientGeneratedText.Should().NotContain("(value, cancellationToken)");

        serverGeneratedText.Should().Contain("MemoryPackSerializer.Deserialize<string>(data.Span)");
        serverGeneratedText.Should().Contain("_implementation.EchoAsync(value, cancellationToken)");
        serverGeneratedText.Should().NotContain("MemoryPackSerializer.Deserialize<(string, System.Threading.CancellationToken)>");
        serverGeneratedText.Should().NotContain("MemoryPackSerializer.Deserialize<System.Threading.CancellationToken>");
    }

    [Fact]
    public void ReceiverCancellationToken_MustNotBeSerialized_AndClientMustInjectHandlerToken()
    {
        var receiverReference = ProtocolIdConsistencyTestsHelpers.CompileToMetadataReference(
            CancellableReceiverSource,
            "WireContractReceiverAssembly");
        var serverCompilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            DummyServerSource,
            "WireContractServerAssembly",
            receiverReference);
        var clientCompilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            CancellableReceiverSource,
            "WireContractClientAssembly");

        var serverGeneratedText = ProtocolIdConsistencyTestsHelpers.RunServerGenerator(serverCompilation);
        var clientGeneratedText = ProtocolIdConsistencyTestsHelpers.RunClientGenerator(clientCompilation);

        serverGeneratedText.Should().Contain("MemoryPackSerializer.Serialize(message)");
        serverGeneratedText.Should().Contain("cancellationToken: cancellationToken");
        serverGeneratedText.Should().Contain("payload, cancellationToken)");
        serverGeneratedText.Should().NotContain("MemoryPackSerializer.Serialize((message, cancellationToken))");
        serverGeneratedText.Should().NotContain("MemoryPackSerializer.Serialize((query, cancellationToken))");

        clientGeneratedText.Should().Contain("MemoryPackSerializer.Deserialize<string>(__data__.Span)");
        clientGeneratedText.Should().Contain("_implementation.OnMessageAsync(__arg__, System.Threading.CancellationToken.None)");
        clientGeneratedText.Should().Contain("_implementation.QueryAsync(__arg__, __ct__)");
        clientGeneratedText.Should().Contain("await DispatchOnMessageAsyncAsync(message, System.Threading.CancellationToken.None);");
        clientGeneratedText.Should().NotContain("Deserialize<(string, System.Threading.CancellationToken)>");
    }

    [Fact]
    public void StringProtocolAttribute_InCodeFixFormat_MustProduceSameManualId_OnClientAndServer()
    {
        const string source = """
            using System.Threading.Tasks;
            using PulseRPC;
            using PulseRPC.Protocol;

            namespace StringProtocolTestNs;

            [Channel("TestServer")]
            public interface IManualProtocolHub : IPulseHub
            {
                [Protocol("0x2345")]
                Task PingAsync();
            }

            [PulseClientGeneration(typeof(IManualProtocolHub))]
            public partial class ClientRegistrar
            {
            }
            """;

        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(source);
        var clientGeneratedText = ProtocolIdConsistencyTestsHelpers.RunClientGenerator(compilation);
        var serverGeneratedText = ProtocolIdConsistencyTestsHelpers.RunServerGenerator(compilation);

        ExtractProtocolId(clientGeneratedText, @"ProtocolId_PingAsync\w*")
            .Should().Be(0x2345, "客户端必须识别 CodeFix 生成的 [Protocol(\"0xXXXX\")] 格式");
        ExtractProtocolId(serverGeneratedText, @"ManualProtocolHub_PingAsync\w*")
            .Should().Be(0x2345, "服务端当前编译单元也必须读取字符串协议号");
    }

    [Fact]
    public void ExplicitProtocolZero_MustBeRejectedByBothGenerators()
    {
        const string source = """
            using System.Threading.Tasks;
            using PulseRPC;
            using PulseRPC.Protocol;

            namespace ReservedProtocolTestNs;

            [Channel("TestServer")]
            public interface IReservedProtocolHub : IPulseHub
            {
                [Protocol("0x0000")]
                Task PingAsync();
            }

            [PulseClientGeneration(typeof(IReservedProtocolHub))]
            public partial class ClientRegistrar
            {
            }
            """;

        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(source);
        var clientResult = ProtocolIdConsistencyTestsHelpers.RunClientGeneratorRaw(compilation);
        var serverResult = ProtocolIdConsistencyTestsHelpers.RunServerGeneratorRaw(compilation);

        clientResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PRPC002" && diagnostic.Severity == DiagnosticSeverity.Error);
        serverResult.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PULSE006" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProtocolConflictCodeFix_MustEmitStringFormatAcceptedByBothGenerators(bool useClientProvider)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("ProtocolCodeFixProject", LanguageNames.CSharp);
        var document = workspace.AddDocument(
            project.Id,
            "Contract.cs",
            SourceText.From("interface IContract { System.Threading.Tasks.Task PingAsync(); }"));
        var root = (await document.GetSyntaxRootAsync())!;
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var diagnosticId = useClientProvider ? "PRPC001" : "PULSE003";
        var descriptor = new DiagnosticDescriptor(
            diagnosticId,
            "Protocol conflict",
            "Protocol conflict",
            "PulseRPC",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("SuggestedProtocolId", "2345");
        var diagnostic = Diagnostic.Create(descriptor, method.Identifier.GetLocation(), properties);
        var actions = new List<CodeAction>();
        CodeFixProvider provider = useClientProvider
            ? new global::PulseRPC.Client.SourceGenerator.CodeFixes.ProtocolIdConflictCodeFixProvider()
            : new global::PulseRPC.Server.SourceGenerator.CodeFixes.ProtocolIdConflictCodeFixProvider();

        await provider.RegisterCodeFixesAsync(new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None));

        var action = actions.Should().ContainSingle().Which;
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changedText = await changedSolution.GetDocument(document.Id)!.GetTextAsync();

        changedText.ToString().Should().Contain("[Protocol(\"0x2345\")]");
    }

    [Fact]
    public void ClientCancellationTokenOutput_MustRemainCSharp9Compatible()
    {
        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(CancellableReceiverSource);
        var result = ProtocolIdConsistencyTestsHelpers.RunClientGeneratorRaw(compilation);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);

        foreach (var generatedSource in result.Results.SelectMany(generator => generator.GeneratedSources))
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(generatedSource.SourceText, parseOptions);
            syntaxTree.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Should().BeEmpty($"{generatedSource.HintName} 必须兼容 Unity 所需的 C# 9");
        }
    }

    [Fact]
    public void NullableResponses_MustUseDeclaredTypeForMemoryPackNull_OnBothEnds()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using MemoryPack;
            using PulseRPC;

            namespace NullableResponseContractNs;

            [MemoryPackable]
            public partial class NullableDto
            {
                public string? Value { get; set; }
            }

            [Channel("TestServer")]
            public interface INullableResponseHub : IPulseHub
            {
                Task<string?> GetStringAsync();
                Task<NullableDto?> GetDtoAsync();
                Task<int?> GetIntAsync();
            }

            [PulseClientGeneration(typeof(INullableResponseHub))]
            public partial class ClientRegistrar
            {
            }
            """;

        var compilation = ProtocolIdConsistencyTestsHelpers.CreateCompilation(source);
        var serverGeneratedText = ProtocolIdConsistencyTestsHelpers.RunServerGenerator(compilation);
        var clientResult = ProtocolIdConsistencyTestsHelpers.RunClientGeneratorRaw(compilation);
        var clientGeneratedText = string.Join(
            "\n\n",
            clientResult.Results.SelectMany(generator => generator.GeneratedSources)
                .Select(generated => generated.SourceText.ToString()));

        serverGeneratedText.Should().Contain("string? typedNull = default;");
        serverGeneratedText.Should().Contain(": INullResponseSerializer",
            "nullable 响应序列化器必须显式 opt-in typed-null 调用契约");
        serverGeneratedText.Should().Contain("global::NullableResponseContractNs.NullableDto? typedNull = default;");
        serverGeneratedText.Should().Contain("int? typedNull = default;");
        serverGeneratedText.Should().Contain("int? typedResponse = typed;",
            "非 null Nullable<T> 也必须保留 nullable wire 格式");

        clientGeneratedText.Should().Contain("MemoryPack.MemoryPackSerializer.Deserialize<string?>");
        clientGeneratedText.Should().Contain("MemoryPack.MemoryPackSerializer.Deserialize<NullableResponseContractNs.NullableDto?>");
        clientGeneratedText.Should().Contain("MemoryPack.MemoryPackSerializer.Deserialize<int?>");

        var csharp9 = new CSharpParseOptions(LanguageVersion.CSharp9);
        foreach (var generatedSource in clientResult.Results.SelectMany(generator => generator.GeneratedSources))
        {
            CSharpSyntaxTree.ParseText(generatedSource.SourceText, csharp9).GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Should().BeEmpty($"{generatedSource.HintName} 必须兼容 C# 9");
        }

        var serverAssembly = typeof(global::PulseRPC.Server.IServiceRoutingTable).Assembly.Location;
        var compileReferences = new[]
        {
            MetadataReference.CreateFromFile(serverAssembly),
            MetadataReference.CreateFromFile(Path.Combine(Path.GetDirectoryName(serverAssembly)!, "PulseRPC.Shared.dll")),
            MetadataReference.CreateFromFile(typeof(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location),
        };
        var compileInput = ProtocolIdConsistencyTestsHelpers.CreateCompilation(
            source,
            "NullableResponseGeneratedCompilation",
            compileReferences);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(
            compileInput,
            out var outputCompilation,
            out var generatorDiagnostics);

        generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("nullable 响应生成器不应报告错误");
        outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("nullable 强类型响应序列化器生成代码必须真实编译通过");
    }

    private static ushort? ExtractProtocolId(string generatedText, string constantNamePattern)
    {
        var match = Regex.Match(generatedText, $@"\b{constantNamePattern}\s*=\s*0x([0-9A-Fa-f]{{4}})\b");
        return match.Success ? Convert.ToUInt16(match.Groups[1].Value, 16) : null;
    }
}
