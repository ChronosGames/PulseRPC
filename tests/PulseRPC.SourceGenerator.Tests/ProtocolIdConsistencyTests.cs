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
            [Reentrant]
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

    [Fact]
    public void RuntimeProtocolIdGenerate_MustMatchBothGenerators()
    {
        var compilation = CreateCompilation(TestSource);
        var serverId = ExtractServerProtocolId(RunServerGenerator(compilation), "SampleHub", "AddAsync");
        var clientId = ExtractClientProtocolId(RunClientGenerator(compilation), "AddAsync");

        var runtimeId = global::PulseRPC.Protocol.ProtocolId.Generate(
            "ProtocolIdConsistencyTestNs.ISampleHub.AddAsync(int,int)");

        runtimeId.Value.Should().Be(serverId);
        runtimeId.Value.Should().Be(clientId);
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

    private const string AuthorizationSource = """
        #nullable enable
        using System;
        using System.Threading.Tasks;
        using PulseRPC;

        namespace PulseRPC.Server
        {
            [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
            public sealed class RequireRoleAttribute : Attribute
            {
                public RequireRoleAttribute(string role) { }
                public bool AllowInternal { get; set; } = true;
                public bool AllowSystem { get; set; } = true;
            }

            [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
            public sealed class RequirePermissionAttribute : Attribute
            {
                public RequirePermissionAttribute(string permission) { }
                public bool AllowInternal { get; set; } = true;
                public bool AllowSystem { get; set; } = true;
            }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class InternalAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class ExternalOnlyAttribute : Attribute { }
        }

        namespace AuthorizationGenerationNs
        {
            [Authorize(Role = "DeclaringRole")]
            public interface ISecureMixin
            {
                [AllowAnonymous]
                [PulseRPC.Server.RequireRole("MethodRole", AllowInternal = false, AllowSystem = false)]
                [PulseRPC.Server.RequirePermission("records.read", AllowInternal = false, AllowSystem = false)]
                Task<string> ReadAsync();
            }

            [Channel("TestServer")]
            [Authorize(Role = "InterfaceRole", Policy = "tenant-owner", Scopes = new[] { "tenant.read" })]
            public interface ISecureHub : IPulseHub, ISecureMixin
            {
                [PulseRPC.Server.Internal]
                Task InternalAsync();

                [PulseRPC.Server.ExternalOnly]
                Task ExternalAsync();
            }
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

        serverGeneratedText.Should().Contain(
            "implementation is global::PulseRPC.Server.Services.PulseServiceBase actor",
            "keyed 实现实例是 PulseServiceBase 时必须进入 Actor 邮箱");

        serverGeneratedText.Should().Contain(
            "reentrant: false",
            "普通 keyed 方法必须作为写者串行执行");

        serverGeneratedText.Should().Contain(
            "reentrant: true",
            "[Reentrant] keyed 方法必须作为只读请求进入邮箱");
    }

    [Fact]
    public void GeneratedRoutingTable_MustValidateCanonicalHubAndProtocolAtomically()
    {
        var serverGeneratedText = RunServerGenerator(CreateCompilation(TestSource));

        serverGeneratedText.Should().Contain("public bool IsProtocolIdValid(string hub, ushort protocolId)");
        serverGeneratedText.Should().Contain("\"SampleHub\" => protocolId is");
        serverGeneratedText.Should().Contain(
            "RouteByProtocolIdAsync(IServiceProvider serviceProvider, string hub, ushort protocolId, ReadOnlyMemory<byte> data");
        serverGeneratedText.Should().Contain(
            "RouteByProtocolIdAsync(IServiceProvider serviceProvider, string hub, ushort protocolId, string serviceKey, ReadOnlyMemory<byte> data");
        serverGeneratedText.Should().Contain("EnsureProtocolIdValid(hub, protocolId);");
    }

    [Fact]
    public void FacetComposition_StrictMap_MustKeepDerivedHubProtocolAlias()
    {
        var serverGeneratedText = RunServerGenerator(CreateCompilation(FacetCompositionSource));
        var guildProtocolId = ExtractServerProtocolId(serverGeneratedText, "GuildHub", "CreateGuildAsync");

        guildProtocolId.Should().NotBeNull();
        serverGeneratedText.Should().MatchRegex(
            $"\\\"BackendHub\\\"\\s*=>[^\\r\\n]*0x{guildProtocolId!.Value:X4}",
            "派生 facet 调用继承方法时仍携带派生 Hub canonical name，严格映射必须保留该合法别名");
    }

    [Fact]
    public void GeneratedRoutingTable_MustEmitMergedAuthorizationBeforeActivationOrInvocation()
    {
        var serverGeneratedText = RunServerGenerator(CreateCompilation(AuthorizationSource));

        serverGeneratedText.Should().Contain("allowAnonymous: true");
        serverGeneratedText.Should().Contain("requireAuthentication: false");
        serverGeneratedText.Should().Contain("AuthorizationRequirementKind.Role, \"InterfaceRole\"");
        serverGeneratedText.Should().Contain("AuthorizationRequirementKind.Role, \"DeclaringRole\"");
        serverGeneratedText.Should().Contain("AuthorizationRequirementKind.Role, \"MethodRole\", allowInternal: false, allowSystem: false");
        serverGeneratedText.Should().Contain("AuthorizationRequirementKind.Permission, \"records.read\", allowInternal: false, allowSystem: false");
        serverGeneratedText.Should().Contain("AuthorizationRequirementKind.Scope, \"tenant.read\"");
        serverGeneratedText.Should().Contain("\"tenant-owner\"");
        serverGeneratedText.Should().Contain("internalOnly: true");
        serverGeneratedText.Should().Contain("externalOnly: true");

        var gateIndex = serverGeneratedText.IndexOf("AuthorizationGate.Enforce", StringComparison.Ordinal);
        var resolveIndex = serverGeneratedText.IndexOf("ResolveKeyedHubInstanceAsync<", StringComparison.Ordinal);
        gateIndex.Should().BeGreaterThan(-1);
        resolveIndex.Should().BeGreaterThan(gateIndex,
            "授权必须在 keyed 实例解析/激活及参数反序列化前执行");
    }

    [Fact]
    public void GeneratedStrictRoutingAndAuthorizationCode_MustCompile()
    {
        var additionalReferences = new[]
        {
            MetadataReference.CreateFromFile(typeof(global::PulseRPC.Server.IServiceRoutingTable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::PulseRPC.Server.Security.AuthorizationGate).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(
                Path.GetDirectoryName(typeof(global::PulseRPC.Server.IServiceRoutingTable).Assembly.Location)!,
                "PulseRPC.Shared.dll")),
            MetadataReference.CreateFromFile(typeof(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location),
        };
        var compilation = CreateCompilation(AuthorizationSource, additionalReferences);
        var generator = new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("生成器驱动不应报告错误");
        outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("严格 Hub 映射和授权 descriptor 的生成代码必须可编译");
    }

    [Fact]
    public void CanonicalHubNameCollision_MustBeBuildError()
    {
        const string source = """
            using System.Threading.Tasks;
            using PulseRPC;

            namespace CanonicalHubCollisionNs;

            public interface IInventoryHub : IPulseHub
            {
                Task FirstAsync();
            }

            public interface InventoryHub : IPulseHub
            {
                Task SecondAsync();
            }
            """;

        var result = RunServerGeneratorRaw(CreateCompilation(source));

        result.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == "PULSE005" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GeneratedClientExtensions_MustExposeTypedGatewayActorEntryPoint()
    {
        var clientGeneratedText = RunClientGenerator(CreateCompilation(TestSource));

        clientGeneratedText.Should().Contain(
            "public static T GetGatewayActor<T>(this IClientChannel channel, string key) where T : class, IPulseHub",
            "客户端应能用强类型 Hub + Actor key 创建透明 Gateway 代理");
        clientGeneratedText.Should().Contain(
            "return GetHub<T>(channel.ForGatewayActor<T>(key));",
            "Gateway Actor 入口必须复用普通业务 Stub，而不是生成第二套调用协议");
    }

    [Fact]
    public void GeneratedClientStub_MustSendCanonicalHub_WhenChannelSupportsAddressedWire()
    {
        var clientGeneratedText = RunClientGenerator(CreateCompilation(TestSource));
        const string commandSource = """
            using System.Threading.Tasks;
            using PulseRPC;

            namespace HubAddressedCommandNs;

            [Channel("TestServer")]
            public interface ICommandHub : IPulseHub
            {
                Task NotifyAsync(string value);
            }

            [PulseClientGeneration(typeof(ICommandHub))]
            public partial class ClientRegistrar
            {
            }
            """;
        var commandGeneratedText = RunClientGenerator(CreateCompilation(commandSource));

        clientGeneratedText.Should().Contain(
            "_connection as PulseRPC.Client.IHubAddressedClientChannel",
            "生成 Stub 必须要求通道显式支持 canonical Hub 寻址");
        clientGeneratedText.Should().Contain(
            "InvokeHubRawAsync(\"SampleHub\"",
            "请求/响应调用应使用与 Gateway Actor 寻址一致的 canonical Hub 名");
        commandGeneratedText.Should().Contain(
            "SendHubCommandAsync(\"CommandHub\"",
            "单向调用也必须携带同一个 canonical Hub 名");
        clientGeneratedText.Should().NotContain(
            "_connection.InvokeRawAsync(",
            "严格 Hub 路由不能静默回退为空 Hub 调用");
        clientGeneratedText.Should().Contain(
            "IHubAddressedClientChannel",
            "旧通道应获得明确的迁移异常，而不是编译成功后由服务端拒绝空 Hub");
    }


    [Fact]
    public void GeneratedRoutingTable_WithoutProtocolAliases_MustNotProduceEmptySwitchWarning()
    {
        var compilation = CreateCompilation(TestSource);
        var generator = new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        outputCompilation.GetDiagnostics()
            .Should().NotContain(diagnostic => diagnostic.Id == "CS1522");
    }

    [Fact]
    public void GeneratedServiceManifest_MustListServicesMethodsAndProtocolIds()
    {
        var compilation = CreateCompilation(TestSource);

        var serverGeneratedText = RunServerGenerator(compilation);

        serverGeneratedText.Should().Contain(
            "public static partial class ServiceManifest",
            "服务端生成器必须输出管理面可查询的服务元数据清单");

        serverGeneratedText.Should().Contain(
            "ServiceManifestRegistry.Register(Instance)",
            "生成的清单必须在程序集加载时注册到运行时");

        serverGeneratedText.Should().Contain("ServiceName = \"ISampleHub\"");
        serverGeneratedText.Should().Contain("HubType = typeof(global::ProtocolIdConsistencyTestNs.ISampleHub)");
        serverGeneratedText.Should().Contain("ChannelName = \"TestServer\"");
        serverGeneratedText.Should().Contain("MethodName = \"AddAsync\"");
        serverGeneratedText.Should().Contain("MethodName = \"GreetAsync\"");
        serverGeneratedText.Should().Contain("DeclaringHubTypeName = \"ProtocolIdConsistencyTestNs.IGreetingMixin\"");
        serverGeneratedText.Should().MatchRegex(@"ProtocolId = 0x[0-9A-Fa-f]{4}");
    }

    [Fact]
    public void NonMemoryPackableCustomResponse_MustReportGeneratorError()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using PulseRPC;

            namespace ResponseValidationTestNs;

            public sealed class BadResponse
            {
                public string Value { get; set; } = "";
            }

            [Channel("TestServer")]
            public interface IResponseHub : IPulseHub
            {
                Task<BadResponse> GetAsync();
            }
            """;

        var result = RunServerGeneratorRaw(CreateCompilation(source));

        result.Diagnostics.Should().Contain(d =>
            d.Id == "PULSE004" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.GetMessage().Contains("BadResponse"));
    }

    [Fact]
    public void MemoryPackableCustomResponse_MustGenerateWithoutResponseValidationError()
    {
        const string source = """
            #nullable enable
            using System.Threading.Tasks;
            using MemoryPack;
            using PulseRPC;

            namespace ResponseValidationTestNs;

            [MemoryPackable]
            public partial class GoodResponse
            {
                public string Value { get; set; } = "";
            }

            [Channel("TestServer")]
            public interface IResponseHub : IPulseHub
            {
                Task<GoodResponse> GetAsync();
            }
            """;

        var result = RunServerGeneratorRaw(CreateCompilation(source));

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("MemoryPackable custom response types must be accepted");
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
        var result = RunServerGeneratorRaw(compilation);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("服务端生成器不应报告编译错误诊断");

        return string.Join("\n\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }

    private static GeneratorDriverRunResult RunServerGeneratorRaw(CSharpCompilation compilation)
    {
        var generator = new global::PulseRPC.Server.SourceGenerator.PulseRPCSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
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

    private static CSharpCompilation CreateCompilation(
        string source,
        IEnumerable<MetadataReference>? additionalReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = GetMetadataReferences().AsEnumerable();
        if (additionalReferences is not null)
        {
            references = references.Concat(additionalReferences)
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
        }

        return CSharpCompilation.Create(
            assemblyName: "ProtocolIdConsistencyTestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
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
        references.Add(MetadataReference.CreateFromFile(typeof(global::MemoryPack.MemoryPackableAttribute).Assembly.Location));

        return references.ToArray();
    }
}
