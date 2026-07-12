using System.Text.RegularExpressions;
using FluentAssertions;
using PulseRPC.Server.SourceGenerator.Generators;
using PulseRPC.Server.SourceGenerator.Models;
using Xunit;

namespace PulseRPC.SourceGenerator.Tests;

public sealed class ClientFacingGateGenerationTests
{
    [Fact]
    public void GeneratedRoutes_MustPassCurrentServiceProviderToClientFacingGate()
    {
        var service = new ServiceModel
        {
            InterfaceName = "ITestHub",
            InterfaceFullName = "TestContracts.ITestHub",
            Namespace = "TestContracts",
            ChannelName = "TestServer",
            Methods =
            [
                CreateMethod("InvokeAsync", protocolId: 0x1234),
            ],
        };
        service.ProtocolAliases.Add(CreateMethod("LegacyInvokeAsync", protocolId: 0x4321));

        var generated = RoutingTableGenerator.GenerateRoutingTable([service], []);

        Regex.Matches(
                generated,
                @"ClientFacingGate\.Enforce\(serviceProvider, isClientFacing:")
            .Should().HaveCount(3, "普通、keyed 与 alias 路由都必须读取当前宿主策略");
        generated.Should().NotContain("ClientFacingGate.Enforce(isClientFacing:");
    }

    private static MethodModel CreateMethod(string methodName, ushort protocolId)
    {
        return new MethodModel
        {
            MethodName = methodName,
            ReturnTypeName = "Task",
            ReturnTypeFullName = "System.Threading.Tasks.Task",
            Parameters = [],
            ProtocolId = protocolId,
            IsAsync = true,
            IsClientFacing = false,
        };
    }
}
