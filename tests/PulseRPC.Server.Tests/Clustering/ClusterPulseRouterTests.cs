using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Messaging;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Routing;
using PulseRPC.Server.Security;
using PulseRPC.Server.Services;
using PulseRPC.Server.Tests;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// 回归测试：<see cref="ClusterPulseRouter"/>（§P4）—— 对 <see cref="AddressKind.Actor"/> 地址按
/// <see cref="NodeConsistentHashRing"/> 解析的候选属主分流：本地属主落到 <see cref="LocalPulseRouter"/>，
/// 远端属主经 <see cref="INodeLink"/> 转发；非 Actor 地址始终委派给本地路由器（单节点行为不变）。
/// </summary>
public class ClusterPulseRouterTests
{
    private const string LocalNodeId = "node-local";
    private const string RemoteNodeId = "node-remote";

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    private static ClusterPulseRouter CreateRouter(
        out LocalPulseRouter local,
        out ServerChannelManager channelManager,
        out IActorDirectory actorDirectory,
        out INodeLink nodeLink)
        => CreateRouter(out local, out channelManager, out actorDirectory, out nodeLink, out _);

    private static ClusterPulseRouter CreateRouter(
        out LocalPulseRouter local,
        out ServerChannelManager channelManager,
        out IActorDirectory actorDirectory,
        out INodeLink nodeLink,
        out IPulseBackplane backplane)
    {
        channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var groupManager = new GroupManager();
        var userMapping = new UserConnectionMapping();

        local = new LocalPulseRouter(
            channelManager,
            groupManager,
            userMapping,
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance);

        var hashRing = new NodeConsistentHashRing(new[] { LocalNodeId, RemoteNodeId });
        actorDirectory = Substitute.For<IActorDirectory>();
        nodeLink = Substitute.For<INodeLink>();
        backplane = Substitute.For<IPulseBackplane>();
        backplane.Subscribe(Arg.Any<BackplaneMessageHandler>()).Returns(Substitute.For<IDisposable>());

        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = LocalNodeId });

        return new ClusterPulseRouter(local, hashRing, actorDirectory, nodeLink, backplane, topology, NullLogger<ClusterPulseRouter>.Instance);
    }

    private static NodeConsistentHashRing CreateProbeRing() => new(new[] { LocalNodeId, RemoteNodeId });

    /// <summary>找到一个使一致性哈希候选属主恰好为 <paramref name="targetOwner"/> 的 Key（用于构造测试用例）。</summary>
    private static string FindKeyOwnedBy(string targetOwner)
    {
        var ring = CreateProbeRing();
        for (var i = 0; i < 10_000; i++)
        {
            var key = $"actor-{i}";
            if (ring.GetOwner(HashPlacementStrategy.BuildIdentity("RoomHub", key)) == targetOwner)
            {
                return key;
            }
        }

        throw new InvalidOperationException($"未能在探测范围内找到属主为 '{targetOwner}' 的 Key，测试环境的哈希环分布异常。");
    }

    [Fact]
    public void Constructor_WithoutLocalNodeId_MustThrow()
    {
        var local = new LocalPulseRouter(
            new ServerChannelManager(NullLogger<ServerChannelManager>.Instance),
            new GroupManager(),
            new UserConnectionMapping(),
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance);
        var hashRing = new NodeConsistentHashRing(new[] { LocalNodeId });
        var backplane = Substitute.For<IPulseBackplane>();
        backplane.Subscribe(Arg.Any<BackplaneMessageHandler>()).Returns(Substitute.For<IDisposable>());

        var act = () => new ClusterPulseRouter(
            local,
            hashRing,
            Substitute.For<IActorDirectory>(),
            Substitute.For<INodeLink>(),
            backplane,
            Options.Create(new ClusterTopologyOptions()),
            NullLogger<ClusterPulseRouter>.Instance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_ActorAddress_WithRemoteOwner_MustForwardViaNodeLink()
    {
        var router = CreateRouter(out _, out _, out var actorDirectory, out var nodeLink);
        var key = FindKeyOwnedBy(RemoteNodeId);

        var body = MemoryPackSerializer.Serialize("hello-actor");
        await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0x1234, body);

        await nodeLink.Received(1).SendActorAsync(RemoteNodeId, "RoomHub", key, 0x1234, body, "", "", Arg.Any<CancellationToken>(), Arg.Any<Guid>());
        await actorDirectory.DidNotReceive().ActivateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AskAsync_ActorAddress_WithRemoteOwner_MustForwardViaNodeLinkAndReturnItsResult()
    {
        var router = CreateRouter(out _, out _, out _, out var nodeLink);
        var key = FindKeyOwnedBy(RemoteNodeId);

        var expectedResponse = MemoryPackSerializer.Serialize("actor-result");
        nodeLink.AskActorAsync(RemoteNodeId, "RoomHub", key, 0x5678, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ReadOnlyMemory<byte>>(expectedResponse));

        var body = MemoryPackSerializer.Serialize("ask-actor");
        var result = await router.AskAsync(PulseAddress.Actor("RoomHub", key), 0x5678, body);

        result.ToArray().Should().BeEquivalentTo(expectedResponse);
    }

    [Fact]
    public async Task SendAsync_ActorAddress_WithLocalOwner_MustActivateLocallyAndDelegateToLocalRouter()
    {
        var router = CreateRouter(out _, out _, out var actorDirectory, out var nodeLink);
        var key = FindKeyOwnedBy(LocalNodeId);

        actorDirectory.ActivateAsync("RoomHub", key, LocalNodeId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(new ActorPlacement(LocalNodeId, "lease-1", DateTime.UtcNow.AddMinutes(1).Ticks)));

        var body = MemoryPackSerializer.Serialize("local-actor");

        // 无 IServiceRoutingTable 注册时，本地投递必然抛出该特定异常；能观察到该异常本身即证明请求确实被委派给了 LocalPulseRouter
        // 而不是被转发到 INodeLink（否则不会命中这条本地代码路径）。
        var act = async () => await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0x1111, body);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("IServiceRoutingTable");

        await nodeLink.DidNotReceive().SendActorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task SendAsync_ActorAddress_WhenDirectoryOverridesLocalCandidateToRemoteOwner_MustForwardToActualOwner()
    {
        // 一致性哈希候选属主是本节点，但目录显示租约实际被另一节点持有（例如迁移场景）：应转发给目录给出的实际属主。
        var router = CreateRouter(out _, out _, out var actorDirectory, out var nodeLink);
        var key = FindKeyOwnedBy(LocalNodeId);

        actorDirectory.ActivateAsync("RoomHub", key, LocalNodeId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(new ActorPlacement(RemoteNodeId, "lease-2", DateTime.UtcNow.AddMinutes(1).Ticks)));

        var body = MemoryPackSerializer.Serialize("payload");
        await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0x2222, body);

        await nodeLink.Received(1).SendActorAsync(RemoteNodeId, "RoomHub", key, 0x2222, body, "", "", Arg.Any<CancellationToken>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task SendAsync_ActorAddress_WithExplicitNodeId_MustBypassHashRingAndUseExplicitNode()
    {
        var router = CreateRouter(out _, out _, out var actorDirectory, out var nodeLink);
        // 故意选一个哈希候选属主为本节点的 Key，但显式指定远端节点：显式 NodeId 应优先生效。
        var key = FindKeyOwnedBy(LocalNodeId);

        var body = MemoryPackSerializer.Serialize("explicit-node");
        await router.SendAsync(PulseAddress.Actor("RoomHub", key, RemoteNodeId), 0x3333, body);

        await nodeLink.Received(1).SendActorAsync(RemoteNodeId, "RoomHub", key, 0x3333, body, "", "", Arg.Any<CancellationToken>(), Arg.Any<Guid>());
        await actorDirectory.DidNotReceive().ActivateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_NonActorAddress_MustAlwaysDelegateToLocalRouterRegardlessOfHashRing()
    {
        var router = CreateRouter(out _, out var channelManager, out _, out var nodeLink);
        var transport = new MockServerTransport("conn-a");
        var channel = channelManager.AddChannel(transport);
        var authContext = new AuthenticationContext("conn-a");
        authContext.SetClientAuthentication("conn-a", "conn-a");
        channel.SetAuthentication(authContext);

        var body = MemoryPackSerializer.Serialize("direct-connection");
        await router.SendAsync(PulseAddress.Connection("RoomHub", "conn-a"), 0x4444, body);

        transport.SentFrames.Should().HaveCount(1);
        await nodeLink.DidNotReceiveWithAnyArgs().SendActorAsync(default!, default!, default!, default, default, default!, default!, default);
    }

    [Fact]
    public async Task SendAsync_ActorAddress_WithGatewayRelayContextScope_MustThreadSourceNodeIdAndReplyToIntoNodeLink()
    {
        // §5：GatewayFrontHub 中转客户端请求时会建立 GatewayRelayContext 作用域；ClusterPulseRouter 转发到
        // 远端属主时必须读取该环境上下文并原样传给 INodeLink，使后端能为该「虚拟连接」建立回执寻径。
        var router = CreateRouter(out _, out _, out _, out var nodeLink);
        var key = FindKeyOwnedBy(RemoteNodeId);
        var body = MemoryPackSerializer.Serialize("via-gateway");

        using (GatewayRelayContext.SetScope("gateway-1", "client-conn-7"))
        {
            await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0x9999, body);
        }

        await nodeLink.Received(1).SendActorAsync(RemoteNodeId, "RoomHub", key, 0x9999, body, "gateway-1", "client-conn-7", Arg.Any<CancellationToken>(), Arg.Any<Guid>());

        // 作用域释放后不应再残留，之后的调用退化为无回执寻径。
        await router.SendAsync(PulseAddress.Actor("RoomHub", key), 0xAAAA, body);
        await nodeLink.Received(1).SendActorAsync(RemoteNodeId, "RoomHub", key, 0xAAAA, body, "", "", Arg.Any<CancellationToken>(), Arg.Any<Guid>());
    }

    /// <summary>
    /// §P6/§9：AllClients/Group/User/Except 是 Fan-out 语义，除本地投递外还必须经
    /// <see cref="IPulseBackplane"/> 模型 X 广播扩散到集群其它节点，否则会静默丢失跨节点成员（§15.1 风险 #2）。
    /// </summary>
    [Theory]
    [InlineData(AddressKindForTest.AllClients)]
    [InlineData(AddressKindForTest.Group)]
    [InlineData(AddressKindForTest.User)]
    [InlineData(AddressKindForTest.Except)]
    public async Task SendAsync_FanoutAddress_MustPublishToBackplane_WithLocalNodeIdAsOrigin(AddressKindForTest kind)
    {
        var router = CreateRouter(out _, out _, out _, out _, out var backplane);
        var address = kind switch
        {
            AddressKindForTest.AllClients => PulseAddress.AllClients("RoomHub"),
            AddressKindForTest.Group => PulseAddress.Group("RoomHub", "room-1"),
            AddressKindForTest.User => PulseAddress.User("RoomHub", "user-1"),
            AddressKindForTest.Except => PulseAddress.Except("RoomHub", "conn-x"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var body = MemoryPackSerializer.Serialize("fanout-payload");

        await router.SendAsync(address, 0x7777, body);

        await backplane.Received(1).PublishAsync(address, 0x7777, body, LocalNodeId, Arg.Any<CancellationToken>());
    }

    public enum AddressKindForTest { AllClients, Group, User, Except }

    [Fact]
    public async Task SendAsync_ConnectionAddress_MustNotPublishToBackplane()
    {
        var router = CreateRouter(out _, out var channelManager, out _, out _, out var backplane);
        var transport = new MockServerTransport("conn-a");
        var channel = channelManager.AddChannel(transport);
        var authContext = new AuthenticationContext("conn-a");
        authContext.SetClientAuthentication("conn-a", "conn-a");
        channel.SetAuthentication(authContext);

        await router.SendAsync(PulseAddress.Connection("RoomHub", "conn-a"), 0x4444, MemoryPackSerializer.Serialize("x"));

        await backplane.DidNotReceiveWithAnyArgs().PublishAsync(default, default, default, default!, default);
    }

    [Fact]
    public void Constructor_MustSubscribeToBackplane_ForModelXDelivery()
    {
        CreateRouter(out _, out _, out _, out _, out var backplane);

        backplane.Received(1).Subscribe(Arg.Any<BackplaneMessageHandler>());
    }

    [Fact]
    public void Dispose_MustDisposeBackplaneSubscription()
    {
        var subscription = Substitute.For<IDisposable>();
        var backplane = Substitute.For<IPulseBackplane>();
        backplane.Subscribe(Arg.Any<BackplaneMessageHandler>()).Returns(subscription);
        var local = new LocalPulseRouter(
            new ServerChannelManager(NullLogger<ServerChannelManager>.Instance),
            new GroupManager(),
            new UserConnectionMapping(),
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance);
        var router = new ClusterPulseRouter(
            local, new NodeConsistentHashRing(new[] { LocalNodeId }), Substitute.For<IActorDirectory>(),
            Substitute.For<INodeLink>(), backplane, Options.Create(new ClusterTopologyOptions { LocalNodeId = LocalNodeId }),
            NullLogger<ClusterPulseRouter>.Instance);

        router.Dispose();

        subscription.Received(1).Dispose();
    }

    /// <summary>
    /// 收到来自<strong>其它</strong>节点的模型 X 广播时，必须对本节点本地成员完成一次投递（至多一次语义）。
    /// </summary>
    [Fact]
    public async Task BackplaneSubscription_OnMessageFromOtherNode_MustDeliverToLocalMembers()
    {
        _ = CreateRouterCapturingSubscription(out var channelManager, out _, out var handler);
        var transport = new MockServerTransport("conn-local");
        var channel = channelManager.AddChannel(transport);
        var authContext = new AuthenticationContext("conn-local");
        authContext.SetClientAuthentication("conn-local", "conn-local");
        channel.SetAuthentication(authContext);

        handler.Should().NotBeNull();
        var body = MemoryPackSerializer.Serialize("from-remote");
        await handler!(PulseAddress.AllClients("RoomHub"), 0x8888, body, RemoteNodeId, CancellationToken.None);

        transport.SentFrames.Should().HaveCount(1);
    }

    [Fact]
    public async Task BackplaneSubscription_OnMessageFromSelf_MustNotDeliverAgain()
    {
        _ = CreateRouterCapturingSubscription(out var channelManager, out _, out var handler);
        var transport = new MockServerTransport("conn-local");
        var channel = channelManager.AddChannel(transport);
        var authContext = new AuthenticationContext("conn-local");
        authContext.SetClientAuthentication("conn-local", "conn-local");
        channel.SetAuthentication(authContext);

        handler.Should().NotBeNull();
        var body = MemoryPackSerializer.Serialize("from-self");
        await handler!(PulseAddress.AllClients("RoomHub"), 0x9990, body, LocalNodeId, CancellationToken.None);

        transport.SentFrames.Should().BeEmpty();
    }

    private static ClusterPulseRouter CreateRouterCapturingSubscription(
        out ServerChannelManager channelManager,
        out IPulseBackplane backplane,
        out BackplaneMessageHandler? handler)
    {
        channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var local = new LocalPulseRouter(
            channelManager,
            new GroupManager(),
            new UserConnectionMapping(),
            EmptyServiceProvider.Instance,
            NullLogger<LocalPulseRouter>.Instance);

        BackplaneMessageHandler? captured = null;
        var bp = Substitute.For<IPulseBackplane>();
        bp.Subscribe(Arg.Any<BackplaneMessageHandler>())
            .Returns(ci =>
            {
                captured = ci.Arg<BackplaneMessageHandler>();
                return Substitute.For<IDisposable>();
            });
        backplane = bp;

        var router = new ClusterPulseRouter(
            local, new NodeConsistentHashRing(new[] { LocalNodeId, RemoteNodeId }), Substitute.For<IActorDirectory>(),
            Substitute.For<INodeLink>(), bp, Options.Create(new ClusterTopologyOptions { LocalNodeId = LocalNodeId }),
            NullLogger<ClusterPulseRouter>.Instance);

        handler = captured;
        return router;
    }
}
