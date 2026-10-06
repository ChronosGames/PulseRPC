using PulseRPC.Clustering;
using PulseRPC.Gateway;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public sealed class BuiltInRoutingTests
{
    [Fact]
    public void RuntimeAssembly_MustProvideGatewayAndNodeRoutes()
    {
        // Access the runtime's own generated table, independent of test registry resets
        // and of hand-written routing tables in the older loopback topology fixture.
        var table = PulseRPC.Generated.ServiceRoutingTable.Instance;
        Assert.True(table.IsProtocolIdValid("GatewayFrontHub", GatewayProtocolIds.FrontRelayAsk));
        Assert.True(table.IsProtocolIdValid("GatewayRelayHub", GatewayProtocolIds.RelayPushFrame));
        Assert.True(table.IsProtocolIdValid(NodeWireProtocol.ClusterInternalHubName, NodeWireProtocol.AuthenticateProtocolId));
        Assert.True(table.IsProtocolIdValid(NodeWireProtocol.ClusterInternalHubName, NodeWireProtocol.AskActorV2ProtocolId));
        Assert.False(table.IsProtocolIdValid("GatewayFrontHub", NodeWireProtocol.AskActorV2ProtocolId));
    }
}
