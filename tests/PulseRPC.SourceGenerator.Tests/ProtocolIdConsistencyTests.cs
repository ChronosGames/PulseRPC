using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

/// <summary>
/// 回归测试：客户端 Stub 与服务端骨架必须为同一方法算出相同协议号。
/// </summary>
/// <remarks>
/// 对应《统一 IPulseHub 全链路寻址与集群架构设计》§11.2 风险 #1：两个生成器早期各自独立实现的
/// "方法收集范围"（是否含继承接口方法）与"协议号哈希输入"（用派生接口全名还是方法实际声明所在
/// 接口全名）一旦出现差异，客户端算出的协议号与服务端路由表里的协议号就会不一致，导致继承自
/// 基接口的方法在运行时找不到路由（或路由到错误的方法）。
/// <para>
/// 本测试直接驱动真实的两个 <c>IIncrementalGenerator</c>（而非重新实现一份哈希逻辑），
/// 对同一份编译单元分别生成客户端与服务端代码，再从生成结果中提取协议号常量比对，
/// 确保两侧对「直接声明的方法」与「继承自纯 mixin 基接口的方法」始终算出完全相同的协议号。
/// </para>
/// </remarks>
public class ProtocolIdConsistencyTests
{
    private const string TestSource = """
        #nullable enable
        using System.Threading.Tasks;
        using PulseRPC;

        namespace ProtocolIdConsistencyTestNs;

        // 纯 mixin 基接口：不独立标注 [Channel]、不继承 IPulseHub，因此不会被当作独立的顶层 Hub 扫描。
        // 用于验证「继承而来的方法」在客户端 Stub 与服务端骨架两侧算出相同协议号。
        public interface IGreetingMixin
        {
            Task<string> GreetAsync(string name, int times);
        }

        [Channel("TestServer")]
        public interface ISampleHub : IPulseHub, IGreetingMixin
        {
            Task<int> AddAsync(int a, int b);
        }

        [PulseClientGeneration(typeof(ISampleHub))]
        public partial class ClientRegistrar
        {
        }
        """;

    [Fact]
    public void InheritedAndDirectMethods_MustProduceSameProtocolId_OnClientAndServer()
    {
        var compilation = CreateCompilation(TestSource);

        var serverGeneratedText = RunServerGenerator(compilation);
        var clientGeneratedText = RunClientGenerator(compilation);

        // 基线：直接声明在 ISampleHub 上的方法，两侧本就应该一致（回归保护，防止测试基础设施本身出错）。
        var directServerId = ExtractServerProtocolId(serverGeneratedText, "SampleHub", "AddAsync");
        var directClientId = ExtractClientProtocolId(clientGeneratedText, "AddAsync");

        directServerId.Should().NotBeNull("服务端应为 ISampleHub 直接声明的 AddAsync 生成协议号常量");
        directClientId.Should().NotBeNull("客户端应为 ISampleHub 直接声明的 AddAsync 生成协议号常量");
        directServerId.Should().Be(directClientId, "直接声明方法的协议号两侧必须一致");

        // 核心断言：继承自纯 mixin 基接口 IGreetingMixin 的 GreetAsync。
        // 修复 §11.2 风险 #1 之前，服务端只扫描接口的直接成员，完全不会为其生成路由；
        // 修复之后，服务端必须收录该方法，且哈希输入（声明接口全名）与客户端保持一致。
        var inheritedServerId = ExtractServerProtocolId(serverGeneratedText, "SampleHub", "GreetAsync");
        var inheritedClientId = ExtractClientProtocolId(clientGeneratedText, "GreetAsync");

        inheritedServerId.Should().NotBeNull("服务端必须为继承自 IGreetingMixin 的 GreetAsync 生成路由（§11.2 风险 #1）");
        inheritedClientId.Should().NotBeNull("客户端应为继承自 IGreetingMixin 的 GreetAsync 生成协议号常量");
        inheritedServerId.Should().Be(inheritedClientId,
            "继承方法的协议号哈希输入必须使用方法实际声明所在接口（IGreetingMixin）的全名，与客户端保持一致");
    }

    private const string FacetCompositionSource = """
        #nullable enable
        using System.Threading.Tasks;
        using PulseRPC;

        namespace FacetCompositionTestNs;

        // 独立顶层 Hub：自己也有 [Channel]，会被单独扫描并生成自己的路由。
        [Channel("BackendServer")]
        public interface IGuildHub : IPulseHub
        {
            Task CreateGuildAsync(string name);
        }

        // 组合 facet：通过继承聚合另一个"本身也独立可路由"的 Hub（真实场景见
        // DistributedGameApp 的 IBackendHub : IPulseHub, IGuildHub）。
        [Channel("BackendServer")]
        public interface IBackendHub : IPulseHub, IGuildHub
        {
            Task<int> PingAsync();
        }

        [PulseClientGeneration(typeof(IGuildHub))]
        [PulseClientGeneration(typeof(IBackendHub))]
        public partial class ClientRegistrar
        {
        }
        """;

    [Fact]
    public void FacetCompositionOfTwoTopLevelHubs_MustNotProduceDuplicateServerRoute()
    {
        // 回归保护：扩大方法收集范围以修复 §11.2 风险 #1 后，像 IBackendHub : IGuildHub 这样
        // 通过继承组合另一个「本身也独立作为顶层 Hub」的既有模式，不应导致服务端全局路由表
        // 出现重复的 protocolId case（详见 DeduplicateFacadeInheritedMethods 的说明）。
        var compilation = CreateCompilation(FacetCompositionSource);

        var serverGeneratedText = RunServerGenerator(compilation);

        // 服务端不应报告 PULSE003（协议号冲突）
        serverGeneratedText.Should().NotContain("CreateGuildAsync 不能使用相同的协议号");

        // IGuildHub 自己的路由表必须包含 CreateGuildAsync；IBackendHub 的路由表不应重复包含它。
        var guildHubHasCreateGuild = Regex.IsMatch(serverGeneratedText, @"\bGuildHub_CreateGuildAsync\s*=\s*0x[0-9A-Fa-f]{4}\b");
        var backendHubHasCreateGuild = Regex.IsMatch(serverGeneratedText, @"\bBackendHub_CreateGuildAsync\s*=\s*0x[0-9A-Fa-f]{4}\b");

        guildHubHasCreateGuild.Should().BeTrue("IGuildHub 自己声明的 CreateGuildAsync 必须保留在其路由表中");
        backendHubHasCreateGuild.Should().BeFalse("继承而来的 CreateGuildAsync 已由 IGuildHub 自己的路由表提供，IBackendHub 不应重复生成");
    }

    [Fact]
    public void GeneratedRoutingTable_MustSupportKeyedProtocolIdRouting()
    {
        // 回归保护：§P3 keyed-actor-routing —— IServiceRoutingTable 的 5 参数（含 serviceKey）重载
        // 必须由服务端生成器落地：空 key 转发到既有 4 参数（DI 单例）语义，非空 key 经
        // PulseServiceManager 解析/激活 keyed actor 后按同一协议号路由。
        var compilation = CreateCompilation(TestSource);

        var serverGeneratedText = RunServerGenerator(compilation);

        serverGeneratedText.Should().Contain(
            "RouteByProtocolIdAsync(IServiceProvider serviceProvider, ushort protocolId, string serviceKey, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)",
            "生成的路由表必须实现 IServiceRoutingTable 的 (Hub,Key) 5 参数重载");

        serverGeneratedText.Should().Contain(
            "ResolveKeyedHubInstanceAsync<",
            "生成的路由表必须包含经 PulseServiceManager 解析 keyed actor 实例的辅助方法调用");

        serverGeneratedText.Should().Contain(
            "RouteByProtocolIdKeyedAsync",
            "生成的路由表必须包含按协议号分发到 keyed 路由器的内部方法");
    }

    private static ushort? ExtractServerProtocolId(string generatedText, string interfaceNameWithoutI, string methodName)
    {
        // 服务端常量名格式：{InterfaceNameWithoutI}_{MethodName} = 0xXXXX;（见 ProtocolIdGenerator.GenerateProtocolIdConstants）
        var match = Regex.Match(generatedText, $@"\b{Regex.Escape(interfaceNameWithoutI)}_{Regex.Escape(methodName)}\s*=\s*0x([0-9A-Fa-f]{{4}})\b");
        return match.Success ? Convert.ToUInt16(match.Groups[1].Value, 16) : null;
    }

    private static ushort? ExtractClientProtocolId(string generatedText, string methodName)
    {
        // 客户端常量名格式：ProtocolId_{MethodName}[_ParamTypeSuffix] = 0xXXXX;（见 ProtocolIdGenerator.GetProtocolIdConstantName）
        var match = Regex.Match(generatedText, $@"\bProtocolId_{Regex.Escape(methodName)}\w*\s*=\s*0x([0-9A-Fa-f]{{4}})\b");
        return match.Success ? Convert.ToUInt16(match.Groups[1].Value, 16) : null;
    }

    private static string RunServerGenerator(CSharpCompilation compilation)
    {
        var generator = new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("服务端生成器不应报告编译错误诊断");

        return string.Join("\n\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    private static string RunClientGenerator(CSharpCompilation compilation)
    {
        var generator = new global::PulseRPC.Generator.ServiceProxyGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        var result = driver.GetRunResult();

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("客户端生成器不应报告编译错误诊断");

        return string.Join("\n\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));

        return CSharpCompilation.Create(
            assemblyName: "ProtocolIdConsistencyTestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var trustedAssembliesPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var references = trustedAssembliesPaths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();

        // PulseRPC.Abstractions：提供 IPulseHub / ChannelAttribute / PulseClientGenerationAttribute。
        references.Add(MetadataReference.CreateFromFile(typeof(global::PulseRPC.IPulseHub).Assembly.Location));

        return references.ToArray();
    }
}
