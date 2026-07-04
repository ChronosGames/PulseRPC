using System;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Routing;
using PulseRPC.Server.Security;
using PulseRPC.Server.Services;
using PulseRPC.Server.Tests.TestUtilities;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// §P6/§9.3 广播语义矩阵回归测试 —— 用 <see cref="InMemorySharedPulseBackplane"/> 在单进程内模拟一个
/// 双节点集群（两个各自独立的 <see cref="ClusterPulseRouter"/> 共享同一个 <see cref="InMemoryBackplaneBus"/>），
/// 验证 <c>AllClients/Group/User/Except</c> 在跨节点场景下"成员不漏、不重复"，closing 设计文档 §15.1
/// 风险 #2（跨节点广播静默丢消息）。
/// </summary>
public class MultiNodeFanoutSemanticsTests
{
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private sealed class SimulatedNode
    {
        public required string NodeId { get; init; }
        public required ServerChannelManager ChannelManager { get; init; }
        public required GroupManager GroupManager { get; init; }
        public required UserConnectionMapping UserMapping { get; init; }
        public required ClusterPulseRouter Router { get; init; }

        public MockServerTransport AddAuthenticatedConnection(string connectionId)
        {
            var transport = new MockServerTransport(connectionId);
            var channel = ChannelManager.AddChannel(transport);
            var authContext = new AuthenticationContext(connectionId);
            authContext.SetClientAuthentication(connectionId, connectionId);
            channel.SetAuthentication(authContext);
            return transport;
        }
    }

    private static SimulatedNode CreateNode(string nodeId, InMemoryBackplaneBus bus)
    {
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var groupManager = new GroupManager();
        var userMapping = new UserConnectionMapping();
        var local = new LocalPulseRouter(
            channelManager, groupManager, userMapping, EmptyServiceProvider.Instance, NullLogger<LocalPulseRouter>.Instance);
        var hashRing = new NodeConsistentHashRing(new[] { NodeA, NodeB });
        var backplane = new InMemorySharedPulseBackplane(bus);
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = nodeId });

        var router = new ClusterPulseRouter(
            local, hashRing, Substitute.For<IActorDirectory>(), Substitute.For<INodeLink>(), backplane, topology,
            NullLogger<ClusterPulseRouter>.Instance);

        return new SimulatedNode
        {
            NodeId = nodeId,
            ChannelManager = channelManager,
            GroupManager = groupManager,
            UserMapping = userMapping,
            Router = router,
        };
    }

    [Fact]
    public async Task AllClients_MustReachAuthenticatedConnectionsOnEveryNode_ExactlyOnceEach()
    {
        var bus = new InMemoryBackplaneBus();
        var nodeA = CreateNode(NodeA, bus);
        var nodeB = CreateNode(NodeB, bus);
        var connA = nodeA.AddAuthenticatedConnection("conn-a1");
        var connB = nodeB.AddAuthenticatedConnection("conn-b1");

        await nodeA.Router.SendAsync(PulseAddress.AllClients("RoomHub"), 0x1001, MemoryPackSerializer.Serialize("hello-all"));

        connA.SentFrames.Should().HaveCount(1, "发起节点上的本地成员应由本地投递覆盖");
        connB.SentFrames.Should().HaveCount(1, "另一节点上的成员应经 Backplane 模型 X 扩散后由该节点本地投递覆盖，且只投递一次");
    }

    [Fact]
    public async Task Except_MustReachAllAuthenticatedConnectionsAcrossCluster_ExceptTheExcludedOne()
    {
        var bus = new InMemoryBackplaneBus();
        var nodeA = CreateNode(NodeA, bus);
        var nodeB = CreateNode(NodeB, bus);
        var excluded = nodeA.AddAuthenticatedConnection("conn-a1");
        var otherOnNodeA = nodeA.AddAuthenticatedConnection("conn-a2");
        var onNodeB = nodeB.AddAuthenticatedConnection("conn-b1");

        await nodeA.Router.SendAsync(PulseAddress.Except("RoomHub", "conn-a1"), 0x1002, MemoryPackSerializer.Serialize("hello-except"));

        excluded.SentFrames.Should().BeEmpty("被排除的连接（即便在发起节点本地）也不应收到消息");
        otherOnNodeA.SentFrames.Should().HaveCount(1);
        onNodeB.SentFrames.Should().HaveCount(1, "排除的连接在别的节点上时，其余节点的全部成员仍应正常收到（不能连带漏发）");
    }

    [Fact]
    public async Task Group_MustReachMembersOnRemoteNode_EvenThoughOriginNodeHasNoLocalMembers()
    {
        var bus = new InMemoryBackplaneBus();
        var nodeA = CreateNode(NodeA, bus);
        var nodeB = CreateNode(NodeB, bus);
        var unrelatedOnNodeA = nodeA.AddAuthenticatedConnection("conn-a1"); // 未加入分组，不应收到
        var memberOnNodeB = nodeB.AddAuthenticatedConnection("conn-b1");
        await nodeB.GroupManager.AddToGroupAsync("conn-b1", "room-1");

        await nodeA.Router.SendAsync(PulseAddress.Group("RoomHub", "room-1"), 0x1003, MemoryPackSerializer.Serialize("hello-group"));

        unrelatedOnNodeA.SentFrames.Should().BeEmpty();
        memberOnNodeB.SentFrames.Should().HaveCount(1, "组成员位于远端节点：本地投递解析不到，必须经 Backplane 扩散后由持有该成员的节点投递");
    }

    [Fact]
    public async Task User_MustReachConnectionsOnRemoteNode_EvenThoughOriginNodeHasNoLocalMapping()
    {
        var bus = new InMemoryBackplaneBus();
        var nodeA = CreateNode(NodeA, bus);
        var nodeB = CreateNode(NodeB, bus);
        var unrelatedOnNodeA = nodeA.AddAuthenticatedConnection("conn-a1");
        var userConnOnNodeB = nodeB.AddAuthenticatedConnection("conn-b1");
        nodeB.UserMapping.Add("alice", "conn-b1");

        await nodeA.Router.SendAsync(PulseAddress.User("RoomHub", "alice"), 0x1004, MemoryPackSerializer.Serialize("hello-user"));

        unrelatedOnNodeA.SentFrames.Should().BeEmpty();
        userConnOnNodeB.SentFrames.Should().HaveCount(1);
    }

    [Fact]
    public async Task Group_WithMembersOnBothNodes_MustDeliverToEachExactlyOnce_WithoutDuplication()
    {
        var bus = new InMemoryBackplaneBus();
        var nodeA = CreateNode(NodeA, bus);
        var nodeB = CreateNode(NodeB, bus);
        var memberOnNodeA = nodeA.AddAuthenticatedConnection("conn-a1");
        await nodeA.GroupManager.AddToGroupAsync("conn-a1", "room-1");
        var memberOnNodeB = nodeB.AddAuthenticatedConnection("conn-b1");
        await nodeB.GroupManager.AddToGroupAsync("conn-b1", "room-1");

        await nodeA.Router.SendAsync(PulseAddress.Group("RoomHub", "room-1"), 0x1005, MemoryPackSerializer.Serialize("hello-both"));

        memberOnNodeA.SentFrames.Should().HaveCount(1, "本地成员由本地投递覆盖一次，不应因 Backplane 回环而重复");
        memberOnNodeB.SentFrames.Should().HaveCount(1, "远端成员经 Backplane 扩散覆盖一次");
    }
}
