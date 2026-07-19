using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using Xunit;

namespace PulseRPC.Server.Tests.Gateway;

/// <summary>
/// 回归测试：<see cref="GatewayFrontHub"/>（§P5 §6.3）—— 把外部客户端对 <c>Actor(hub,key)</c> 的调用
/// 经 <see cref="IPulseRouter"/> 中转，并在中转期间建立 <see cref="GatewayRelayContext"/> 作用域
/// （供 <c>ClusterPulseRouter</c> 转发到远端节点时携带发起网关/回执连接标识），HopLimit 耗尽时拒绝转发。
/// </summary>
public class GatewayFrontHubTests
{
    [Fact]
    public void GatewayFrontContract_MustBeClientFacing()
    {
        typeof(IGatewayFrontHub).GetCustomAttribute<ClientFacingAttribute>()
            .Should().NotBeNull("Gateway Front 是外部客户端进入 Actor 网格的显式入口");
    }

    private static GatewayFrontHub CreateHub(out IPulseRouter router, string localNodeId = "gateway-1")
    {
        router = Substitute.For<IPulseRouter>();
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = localNodeId });
        return new GatewayFrontHub(router, topology, NullLogger<GatewayFrontHub>.Instance);
    }

    [Fact]
    public async Task RelayAskAsync_ForwardsToRouter_WithActorAddress_AndReturnsItsResult()
    {
        var hub = CreateHub(out var router);
        var expected = new byte[] { 1, 2, 3 };
        router.AskAsync(PulseAddress.Actor("RoomHub", "room-1"), 0x1234, Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ReadOnlyMemory<byte>>(expected));

        byte[] result;
        using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-1")))
        {
            result = await hub.RelayAskAsync("RoomHub", "room-1", 0x1234, new byte[] { 9 }, hopLimit: 4);
        }

        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task RelayAskAsync_PropagatesAmbientRequestCancellation()
    {
        var hub = CreateHub(out var router);
        using var cts = new CancellationTokenSource();
        router.AskAsync(
                Arg.Any<PulseAddress>(),
                Arg.Any<ushort>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                cts.Token)
            .Returns(new ValueTask<ReadOnlyMemory<byte>>(Array.Empty<byte>()));

        using (PulseContext.SetContext(
                   PulseContextData.CreateUserContext("user-1", connectionId: "client-1") with
                   {
                       CancellationToken = cts.Token,
                   }))
        {
            await hub.RelayAskAsync("RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 4);
        }

        await router.Received(1).AskAsync(
            PulseAddress.Actor("RoomHub", "room-1"),
            0x1234,
            Arg.Any<ReadOnlyMemory<byte>>(),
            cts.Token);
    }

    [Fact]
    public async Task RelaySendAsync_ForwardsToRouter_WithActorAddress()
    {
        var hub = CreateHub(out var router);

        using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-1")))
        {
            await hub.RelaySendAsync("RoomHub", "room-2", 0x5678, new byte[] { 7 }, hopLimit: 4);
        }

        await router.Received(1).SendAsync(
            PulseAddress.Actor("RoomHub", "room-2"), 0x5678, Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<DeliveryMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelayAskAsync_WithZeroHopLimit_MustThrowAndNeverCallRouter()
    {
        var hub = CreateHub(out var router);

        var act = async () => await hub.RelayAskAsync("RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 0);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await router.DidNotReceiveWithAnyArgs().AskAsync(default, default, default, default);
    }

    [Fact]
    public async Task RelayAskAsync_WithoutExternalClientContext_MustFailClosed()
    {
        var hub = CreateHub(out var router);

        var act = async () => await hub.RelayAskAsync(
            "RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 4);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await router.DidNotReceiveWithAnyArgs().AskAsync(default, default, default, default);
    }

    [Fact]
    public async Task RelayAskAsync_EstablishesGatewayRelayContextScope_WithLocalNodeIdAndCurrentClientConnectionId()
    {
        var hub = CreateHub(out var router, localNodeId: "gateway-42");
        GatewayRelayInfo? captured = null;
        router.AskAsync(Arg.Any<PulseAddress>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captured = GatewayRelayContext.Current;
                return new ValueTask<ReadOnlyMemory<byte>>(Array.Empty<byte>());
            });

        using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-conn-9")))
        {
            await hub.RelayAskAsync("RoomHub", "room-1", 0x1111, Array.Empty<byte>(), hopLimit: 4);
        }

        captured.Should().NotBeNull();
        captured!.Value.GatewayNodeId.Should().Be("gateway-42");
        captured!.Value.ClientConnectionId.Should().Be("client-conn-9");

        // 调用结束后作用域必须已释放，不残留到之后的环境上下文中。
        GatewayRelayContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task RelayAskAsync_WithConnectionDirectory_MustRegisterGatewayVirtualConnection()
    {
        var router = Substitute.For<IPulseRouter>();
        router.AskAsync(Arg.Any<PulseAddress>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ReadOnlyMemory<byte>>(Array.Empty<byte>()));
        var directory = Substitute.For<IConnectionDirectory>();
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = "gateway-1" });
        var hub = new GatewayFrontHub(router, topology, NullLogger<GatewayFrontHub>.Instance, directory);

        using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-conn-9")))
        {
            await hub.RelayAskAsync("RoomHub", "room-1", 0x1111, Array.Empty<byte>(), hopLimit: 4);
        }

        await directory.Received(1).RegisterConnectionAsync(
            GatewayVirtualChannel.ComposeId("gateway-1", "client-conn-9"),
            new ConnectionPlacement("gateway-1", "client-conn-9"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RelayAskAsync_WithInvocationPolicies_MustRunInRegistrationOrderBeforeRouting()
    {
        var router = Substitute.For<IPulseRouter>();
        var events = new List<string>();
        router.AskAsync(Arg.Any<PulseAddress>(), Arg.Any<ushort>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                events.Add("router");
                return new ValueTask<ReadOnlyMemory<byte>>(Array.Empty<byte>());
            });
        var first = new RecordingInvocationPolicy("first", events);
        var second = new RecordingInvocationPolicy("second", events);
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = "gateway-1" });
        var hub = new GatewayFrontHub(
            router,
            topology,
            NullLogger<GatewayFrontHub>.Instance,
            connectionDirectory: null,
            routingTable: null,
            new IGatewayActorInvocationPolicy[] { first, second });

        using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-1")))
        {
            await hub.RelayAskAsync("RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 4);
        }

        events.Should().Equal("first", "second", "router");
        first.Context.Should().NotBeNull();
        first.Context!.Hub.Should().Be("RoomHub");
        first.Context.Key.Should().Be("room-1");
        first.Context.ProtocolId.Should().Be(0x1234);
        first.Context.InvocationKind.Should().Be(GatewayActorInvocationKind.Ask);
        first.Context.CallerContext.UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task RelaySendAsync_WhenInvocationPolicyRejects_MustNotRegisterOrRoute()
    {
        var router = Substitute.For<IPulseRouter>();
        var directory = Substitute.For<IConnectionDirectory>();
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = "gateway-1" });
        var hub = new GatewayFrontHub(
            router,
            topology,
            NullLogger<GatewayFrontHub>.Instance,
            directory,
            routingTable: null,
            new IGatewayActorInvocationPolicy[] { new RejectingInvocationPolicy() });

        var act = async () =>
        {
            using (PulseContext.SetContext(PulseContextData.CreateUserContext("user-1", connectionId: "client-1")))
            {
                await hub.RelaySendAsync("RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 4);
            }
        };

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await directory.DidNotReceiveWithAnyArgs().RegisterConnectionAsync(default!, default, default);
        await router.DidNotReceiveWithAnyArgs().SendAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task RelaySendAsync_WithInvocationPolicy_MustPassAmbientCancellationAndSendKind()
    {
        var router = Substitute.For<IPulseRouter>();
        using var cts = new CancellationTokenSource();
        var policy = new RecordingInvocationPolicy("policy", new List<string>());
        var topology = Options.Create(new ClusterTopologyOptions { LocalNodeId = "gateway-1" });
        var hub = new GatewayFrontHub(
            router,
            topology,
            NullLogger<GatewayFrontHub>.Instance,
            connectionDirectory: null,
            routingTable: null,
            new IGatewayActorInvocationPolicy[] { policy });

        using (PulseContext.SetContext(
                   PulseContextData.CreateUserContext("user-1", connectionId: "client-1") with
                   {
                       CancellationToken = cts.Token,
                   }))
        {
            await hub.RelaySendAsync("RoomHub", "room-1", 0x1234, Array.Empty<byte>(), hopLimit: 4);
        }

        policy.CancellationToken.Should().Be(cts.Token);
        policy.Context!.InvocationKind.Should().Be(GatewayActorInvocationKind.Send);
    }

    private sealed class RecordingInvocationPolicy : IGatewayActorInvocationPolicy
    {
        private readonly string _name;
        private readonly List<string> _events;

        public RecordingInvocationPolicy(string name, List<string> events)
        {
            _name = name;
            _events = events;
        }

        public GatewayActorInvocationContext? Context { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public ValueTask EvaluateAsync(
            GatewayActorInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            CancellationToken = cancellationToken;
            _events.Add(_name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RejectingInvocationPolicy : IGatewayActorInvocationPolicy
    {
        public ValueTask EvaluateAsync(
            GatewayActorInvocationContext context,
            CancellationToken cancellationToken = default)
        {
            throw new UnauthorizedAccessException("rejected");
        }
    }

}
