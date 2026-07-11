using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Messaging;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public sealed class NodeWireProtocolTests
{
    [Fact]
    public void ActorEnvelope_RoundTripsDuplicateClaimsAndClaimMetadata()
    {
        var envelope = CreateEnvelope("lease-1", CreateCaller());

        var payload = NodeWireProtocol.SerializeActorInvocation(envelope);
        var restored = NodeWireProtocol.ParseActorInvocation(payload);

        restored.WireVersion.Should().Be(NodeWireProtocol.CurrentWireVersion);
        restored.LeaseId.Should().Be("lease-1");
        restored.Caller.Should().NotBeNull();
        restored.Caller!.Identities.Should().ContainSingle();
        restored.Caller.Identities[0].AuthenticationType.Should().Be("jwt");
        restored.Caller.Identities[0].NameClaimType.Should().Be("custom-name");
        restored.Caller.Identities[0].RoleClaimType.Should().Be("custom-role");
        restored.Caller.Identities[0].Claims.Count(c => c.Type == "tenant").Should().Be(2);
        var firstTenant = restored.Caller.Identities[0].Claims.First(c => c.Value == "alpha");
        firstTenant.ValueType.Should().Be(ClaimValueTypes.String);
        firstTenant.Issuer.Should().Be("issuer-a");
        firstTenant.OriginalIssuer.Should().Be("issuer-original");
        firstTenant.Properties.Should().Contain("source", "jwt-header");
    }

    [Fact]
    public async Task TransportBackedNodeLink_V2_PreservesCallerPrincipalAndLease()
    {
        var transport = new CapturingVersionedTransport(NodeWireProtocol.SupportedCapabilities);
        var link = new TransportBackedNodeLink(transport);
        var identity = new ClaimsIdentity(
            new[]
            {
                NewClaim("tenant", "alpha", "issuer-a", "issuer-original", "source", "jwt-header"),
                NewClaim("tenant", "beta", "issuer-a", "issuer-original", "source", "jwt-body"),
                new Claim("custom-role", "admin"),
            },
            "jwt",
            "custom-name",
            "custom-role");
        var authentication = new AuthenticationContext("client-1");
        authentication.SetClientAuthentication(
            "user-7",
            "Player Seven",
            token: "bearer-must-not-cross-node",
            principal: new ClaimsPrincipal(identity));
        using var contextScope = PulseContext.SetContext(
            PulseContextData.FromAuthenticationContext(authentication) with
            {
                CallerId = "user-7",
                UserId = "user-7",
                Permissions = new HashSet<string> { "room.write" },
                Roles = new HashSet<string> { "admin" },
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            });

        await link.SendActorAsync(
            "node-b",
            "RoomHub",
            "room-1",
            0x4321,
            new byte[] { 1, 2, 3 },
            sourceNodeId: "gateway-a",
            replyTo: "conn-9",
            messageId: Guid.NewGuid(),
            leaseId: "lease-42");

        EnvelopeRelay.TryReadHeader(transport.LastFrame, out var header, out var methodBody).Should().BeTrue();
        header.Hub.Should().Be(NodeWireProtocol.ClusterInternalHubName);
        header.MethodId.Should().Be(NodeWireProtocol.SendActorV2ProtocolId);
        header.Type.Should().Be(MessageType.Request);
        header.Flags.Should().HaveFlag(MessageFlags.RequireResponse);
        transport.AskCount.Should().Be(1, "V2 Send must wait for the execution ACK");
        transport.SendCount.Should().Be(0);
        var innerPayload = NodeWireProtocol.ParseByteArrayResult(methodBody.Span);
        var envelope = NodeWireProtocol.ParseActorInvocation(innerPayload);

        envelope.LeaseId.Should().Be("lease-42");
        envelope.Caller!.UserId.Should().Be("user-7");
        envelope.Caller.Permissions.Should().Contain("room.write");
        envelope.Caller.Roles.Should().Contain("admin");
        envelope.Caller.Identities[0].Claims.Count(c => c.Type == "tenant").Should().Be(2);
        typeof(NodeCallerContextSnapshot).GetProperty("Token").Should().BeNull(
            "bearer credentials must never be part of the caller wire snapshot");
    }

    [Fact]
    public async Task TransportBackedNodeLink_ExternalCallerWithoutClaimsCapability_FailsClosed()
    {
        var capabilities = NodeWireProtocol.SupportedCapabilities & ~NodeTransportCapabilities.ClaimsPrincipal;
        var transport = new CapturingVersionedTransport(capabilities);
        var link = new TransportBackedNodeLink(transport);
        using var contextScope = PulseContext.SetContext(
            PulseContextData.CreateUserContext("user-1", connectionId: "client-1"));

        var act = async () => await link.SendActorAsync(
            "node-b", "RoomHub", "room-1", 0x1001, Array.Empty<byte>(), leaseId: "lease-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ClaimsPrincipal*拒绝*");
        transport.LastFrame.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task NegotiateAsync_RecordsVersionAndCapabilitiesOnAuthenticatedConnection()
    {
        var harness = CreateHarness();
        var authentication = CreateNodeAuthentication();
        var channel = Substitute.For<IServerChannel>();
        channel.AuthenticationContext.Returns(authentication);
        harness.ChannelManager.GetChannel("peer-conn-1").Returns(channel);

        using var nodeScope = EnterNodeScope(authentication, negotiated: false);
        var responsePayload = await harness.Hub.NegotiateAsync(
            NodeWireProtocol.SerializeNegotiationRequest(
                "node-peer",
                NodeWireProtocol.SupportedCapabilities));
        var response = NodeWireProtocol.ParseNegotiationResponse(responsePayload);

        response.Accepted.Should().BeTrue();
        response.NodeId.Should().Be("node-local");
        response.SelectedWireVersion.Should().Be(NodeWireProtocol.CurrentWireVersion);
        response.Capabilities.Should().Be(NodeWireProtocol.SupportedCapabilities);
        response.Credential.Should().Equal(1, 2, 3, 4);
        authentication.Properties[NodeWireProtocol.NegotiatedWireVersionPropertyName]
            .Should().Be(NodeWireProtocol.CurrentWireVersion);
        authentication.Properties[NodeWireProtocol.NegotiatedCapabilitiesPropertyName]
            .Should().Be(NodeWireProtocol.SupportedCapabilities);
    }

    [Fact]
    public async Task SendActorV2_HubProtocolMismatch_IsRejectedBeforeLeaseAndExecution()
    {
        var harness = CreateHarness(protocolIsValid: false);
        using var nodeScope = EnterNodeScope(CreateNodeAuthentication(), negotiated: true);

        var act = async () => await harness.Hub.SendActorV2Async(
            NodeWireProtocol.SerializeActorInvocation(CreateEnvelope("lease-1", caller: null)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not belong*");
        await harness.ActorDirectory.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default);
        harness.LeaseHeartbeat.DidNotReceiveWithAnyArgs()
            .Track(default!, default!, default);
        await harness.RoutingTable.DidNotReceiveWithAnyArgs()
            .RouteByProtocolIdAsync(default!, default!, default, default!, default, default);
    }

    [Fact]
    public async Task SendActorV2_LeaseMismatch_IsRejectedBeforeExecution()
    {
        var harness = CreateHarness(placementLeaseId: "new-lease");
        using var nodeScope = EnterNodeScope(CreateNodeAuthentication(), negotiated: true);

        var act = async () => await harness.Hub.SendActorV2Async(
            NodeWireProtocol.SerializeActorInvocation(CreateEnvelope("stale-lease", caller: null)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*lease fencing rejected*");
        await harness.RoutingTable.DidNotReceiveWithAnyArgs()
            .RouteByProtocolIdAsync(default!, default!, default, default!, default, default);
        harness.LeaseHeartbeat.DidNotReceiveWithAnyArgs()
            .Track(default!, default!, default);
    }

    [Fact]
    public async Task SendActorV2_CallerOverConfiguredIdentityLimit_IsRejectedBeforeLease()
    {
        var harness = CreateHarness();
        var caller = CreateCaller();
        caller.Identities = Enumerable.Range(0, 9)
            .Select(_ => new NodeClaimsIdentitySnapshot
            {
                AuthenticationType = "jwt",
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role,
            })
            .ToArray();
        using var nodeScope = EnterNodeScope(CreateNodeAuthentication(), negotiated: true);

        var act = async () => await harness.Hub.SendActorV2Async(
            NodeWireProtocol.SerializeActorInvocation(CreateEnvelope("lease-1", caller)));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*identity count exceeds*");
        await harness.ActorDirectory.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default);
        harness.LeaseHeartbeat.DidNotReceiveWithAnyArgs()
            .Track(default!, default!, default);
    }

    [Fact]
    public async Task SendActorV2_RestoresExternalPrincipalWithoutNodeOrBearerCredentials()
    {
        var harness = CreateHarness();
        PulseContextData? captured = null;
        harness.RoutingTable.RouteByProtocolIdAsync(
                Arg.Any<IServiceProvider>(),
                "RoomHub",
                0x2222,
                "room-1",
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captured = PulseContext.Current as PulseContextData;
                return new ValueTask<object?>((object?)null);
            });
        using var nodeScope = EnterNodeScope(CreateNodeAuthentication(), negotiated: true);

        await harness.Hub.SendActorV2Async(
            NodeWireProtocol.SerializeActorInvocation(CreateEnvelope("lease-1", CreateCaller())));

        captured.Should().NotBeNull();
        captured!.SourceType.Should().Be(CallSourceType.ExternalUser);
        captured.UserId.Should().Be("user-7");
        captured.CallerId.Should().Be("user-7");
        captured.Token.Should().BeNull();
        captured.Permissions.Should().Contain("room.write");
        captured.Roles.Should().Contain("admin");
        captured.User.Should().NotBeNull();
        captured.User!.Claims.Count(c => c.Type == "tenant").Should().Be(2);
        captured.User.IsInRole("admin").Should().BeTrue();
        captured.User.Claims.First(c => c.Value == "alpha").Properties
            .Should().Contain("source", "jwt-header");
        captured.AuthenticationContext!.Token.Should().BeNull();
        harness.LeaseHeartbeat.Received(1).Track(
            "RoomHub",
            "room-1",
            Arg.Is<ActorPlacement>(p => p.NodeId == "node-local" && p.LeaseId == "lease-1"));
    }

    [Fact]
    public void Authenticate_IsTheOnlyClientFacingClusterInternalMethod()
    {
        var methods = typeof(IClusterInternalHub).GetMethods();

        methods.Single(m => m.Name == nameof(IClusterInternalHub.AuthenticateAsync))
            .GetCustomAttribute<ClientFacingAttribute>()
            .Should().NotBeNull();
        methods.Where(m => m.Name != nameof(IClusterInternalHub.AuthenticateAsync))
            .Should().OnlyContain(m => m.GetCustomAttribute<ClientFacingAttribute>() == null);
    }

    private static TestHarness CreateHarness(
        bool protocolIsValid = true,
        string placementLeaseId = "lease-1")
    {
        var authenticator = Substitute.For<INodeAuthenticator>();
        authenticator.CreateCredentialAsync("node-local", Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3, 4 }));
        var channelManager = Substitute.For<IServerChannelManager>();
        var nodeLink = Substitute.For<INodeLink>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var routingTable = Substitute.For<IServiceRoutingTable>();
        routingTable.IsProtocolIdValid("RoomHub", 0x2222).Returns(protocolIsValid);
        var actorDirectory = Substitute.For<IActorDirectory>();
        var leaseHeartbeat = Substitute.For<IActorLeaseHeartbeat>();
        actorDirectory.ResolveAsync("RoomHub", "room-1", Arg.Any<CancellationToken>())
            .Returns(new ActorPlacement(
                "node-local",
                placementLeaseId,
                DateTime.UtcNow.AddMinutes(1).Ticks));
        var hub = new ClusterInternalHub(
            authenticator,
            channelManager,
            nodeLink,
            serviceProvider,
            NullLogger<ClusterInternalHub>.Instance,
            routingTable,
            actorDirectory: actorDirectory,
            topologyOptions: Options.Create(new ClusterTopologyOptions { LocalNodeId = "node-local" }),
            wireOptions: Options.Create(new ClusterNodeWireOptions()),
            leaseHeartbeat: leaseHeartbeat);
        return new TestHarness(hub, channelManager, routingTable, actorDirectory, leaseHeartbeat);
    }

    private static AuthenticationContext CreateNodeAuthentication()
    {
        var authentication = new AuthenticationContext("peer-conn-1");
        authentication.SetServiceAuthentication(
            "node-peer",
            "node-peer",
            token: NodeConnectionGate.NodeConnectionScope,
            scopes: new[] { NodeConnectionGate.NodeConnectionScope });
        return authentication;
    }

    private static IDisposable EnterNodeScope(
        AuthenticationContext authentication,
        bool negotiated)
    {
        if (negotiated)
        {
            authentication.Properties[NodeWireProtocol.NegotiatedNodeIdPropertyName] = "node-peer";
            authentication.Properties[NodeWireProtocol.NegotiatedWireVersionPropertyName] = NodeWireProtocol.CurrentWireVersion;
            authentication.Properties[NodeWireProtocol.NegotiatedCapabilitiesPropertyName] = NodeWireProtocol.SupportedCapabilities;
        }

        return PulseContext.SetContext(PulseContextData.FromAuthenticationContext(authentication) with
        {
            ConnectionId = "peer-conn-1",
        });
    }

    private static NodeActorInvocationEnvelope CreateEnvelope(
        string leaseId,
        NodeCallerContextSnapshot? caller)
        => new()
        {
            WireVersion = NodeWireProtocol.CurrentWireVersion,
            Hub = "RoomHub",
            Key = "room-1",
            ProtocolId = 0x2222,
            Body = new byte[] { 1, 2, 3 },
            MessageId = Guid.NewGuid(),
            LeaseId = leaseId,
            Caller = caller,
        };

    private static NodeCallerContextSnapshot CreateCaller()
        => new()
        {
            UserId = "user-7",
            CallerId = "user-7",
            Permissions = new[] { "room.write" },
            Roles = new[] { "admin" },
            ExpiresAtUtcTicks = DateTime.UtcNow.AddMinutes(5).Ticks,
            Identities = new[]
            {
                new NodeClaimsIdentitySnapshot
                {
                    AuthenticationType = "jwt",
                    NameClaimType = "custom-name",
                    RoleClaimType = "custom-role",
                    Claims = new[]
                    {
                        new NodeClaimSnapshot
                        {
                            Type = "custom-name",
                            Value = "Player Seven",
                            ValueType = ClaimValueTypes.String,
                            Issuer = "issuer-a",
                            OriginalIssuer = "issuer-original",
                        },
                        new NodeClaimSnapshot
                        {
                            Type = "custom-role",
                            Value = "admin",
                            ValueType = ClaimValueTypes.String,
                            Issuer = "issuer-a",
                            OriginalIssuer = "issuer-original",
                        },
                        new NodeClaimSnapshot
                        {
                            Type = "tenant",
                            Value = "alpha",
                            ValueType = ClaimValueTypes.String,
                            Issuer = "issuer-a",
                            OriginalIssuer = "issuer-original",
                            Properties = new() { ["source"] = "jwt-header" },
                        },
                        new NodeClaimSnapshot
                        {
                            Type = "tenant",
                            Value = "beta",
                            ValueType = ClaimValueTypes.String,
                            Issuer = "issuer-a",
                            OriginalIssuer = "issuer-original",
                            Properties = new() { ["source"] = "jwt-body" },
                        },
                    },
                },
            },
        };

    private static Claim NewClaim(
        string type,
        string value,
        string issuer,
        string originalIssuer,
        string propertyName,
        string propertyValue)
    {
        var claim = new Claim(type, value, ClaimValueTypes.String, issuer, originalIssuer);
        claim.Properties[propertyName] = propertyValue;
        return claim;
    }

    private sealed record TestHarness(
        ClusterInternalHub Hub,
        IServerChannelManager ChannelManager,
        IServiceRoutingTable RoutingTable,
        IActorDirectory ActorDirectory,
        IActorLeaseHeartbeat LeaseHeartbeat);

    private sealed class CapturingVersionedTransport : IVersionedNodeTransport
    {
        private readonly NodeTransportCapabilities _capabilities;

        public CapturingVersionedTransport(NodeTransportCapabilities capabilities)
        {
            _capabilities = capabilities;
        }

        public ReadOnlyMemory<byte> LastFrame { get; private set; }
        public int SendCount { get; private set; }
        public int AskCount { get; private set; }

        public ValueTask<NodeTransportSession> GetSessionAsync(
            string targetNodeId,
            CancellationToken cancellationToken = default)
            => new(new NodeTransportSession(
                targetNodeId,
                NodeWireProtocol.CurrentWireVersion,
                _capabilities));

        public ValueTask SendFrameAsync(
            string targetNodeId,
            ReadOnlyMemory<byte> framedPacket,
            CancellationToken cancellationToken = default)
        {
            LastFrame = framedPacket;
            SendCount++;
            return default;
        }

        public ValueTask<ReadOnlyMemory<byte>> AskFrameAsync(
            string targetNodeId,
            ReadOnlyMemory<byte> framedPacket,
            CancellationToken cancellationToken = default)
        {
            LastFrame = framedPacket;
            AskCount++;
            return new ValueTask<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
        }
    }
}
