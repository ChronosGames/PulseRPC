using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Gateway;

public sealed class GameServerProfileTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("bad", true)]
    [InlineData("9223372036854775807", true)]
    [InlineData("4102444800", false)]
    public void AuthenticatedJwtExpiry_IsPreservedInRequestContext(string expiration, bool expired)
    {
        var auth = new PulseRPC.Server.Security.AuthenticationContext("client");
        auth.SetClientAuthentication("alice", "Alice", principal: new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("exp", expiration) }, "jwt")));
        var context = PulseContextData.FromAuthenticationContext(auth);
        Assert.Equal(expired, context.IsExpired);
        Assert.NotNull(context.ExpiresAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Profiles_EnableGateAndResourceBudgets_WithoutChangingLegacyDefaults(bool internalNode)
    {
        var legacy = new PulseServerOptions();
        Assert.False(legacy.EnableClientFacingGate);
        Assert.Equal(1, legacy.MaxConcurrentMessagesPerShard);
        Assert.Equal(0, legacy.MaxPendingMessageBytesPerShard);
        var options = new PulseServerOptions().AddTcp(7000);
        if (internalNode) options.UseGameNodeProfile();
        else options.UseGameGatewayProfile();
        options.Validate();
        Assert.True(options.EnableClientFacingGate);
        Assert.True(options.MaxQueuedMessagesPerConnection > 0);
        Assert.True(options.MaxPendingMessageBytesPerConnection > 0);
        Assert.True(options.MaxRequestTimeoutMs > 0);
        Assert.Equal(internalNode ? 16 : 1, options.MaxConcurrentMessagesPerConnection);
    }

    [Fact]
    public async Task TcpListener_CanBindOnlyLoopback_ForTlsSidecar()
    {
        using var listener = new TcpServerListener(0, new TcpTransportOptions(), NullLogger.Instance, IPAddress.Loopback);
        await listener.StartAsync();
        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndPoint).Address);
        await listener.StopAsync();
        using var configured = new TcpTransportProvider().CreateServerListener(new TransportChannelConfiguration
        {
            Type = TransportType.TCP, Port = 12345, ListenAddress = IPAddress.Loopback
        }, NullLoggerFactory.Instance);
        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)configured.LocalEndPoint).Address);
    }

    [Fact]
    public async Task KcpListener_HonorsConfiguredIpv4Interface()
    {
        using var listener = new KcpServerListener(0, new KcpTransportOptions(), NullLogger.Instance, IPAddress.Loopback);
        await listener.StartAsync();
        Assert.Equal(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndPoint).Address);
        await listener.StopAsync();
    }

    [Theory]
    [InlineData("Player", "alice", 100, false, true)]
    [InlineData("Player", "bob", 100, false, false)]
    [InlineData("Player", "alice", 101, false, false)]
    [InlineData("Admin", "alice", 100, false, false)]
    [InlineData("Player", "alice", 100, true, false)]
    public async Task ResourcePolicy_ChecksOwnerMethodHubAndExpiry(string hub, string key, int protocol, bool expired, bool allowed)
    {
        var methods = new List<ushort> { 100 };
        var policy = new UserOwnedActorInvocationPolicy(new Dictionary<string, IReadOnlyCollection<ushort>> { ["Player"] = methods });
        methods.Add(101); // Mutating application configuration must not silently widen permissions.
        var caller = PulseContextData.CreateUserContext("alice", connectionId: "client") with
        {
            ExpiresAt = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(1)
        };
        var context = new GatewayActorInvocationContext(hub, key, (ushort)protocol, GatewayActorInvocationKind.Ask, caller);
        if (allowed) await policy.EvaluateAsync(context);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => policy.EvaluateAsync(context).AsTask());
    }

    [Fact]
    public async Task ResourcePolicy_RejectsAnonymousAndInternalCaller()
    {
        var policy = new UserOwnedActorInvocationPolicy(new Dictionary<string, IReadOnlyCollection<ushort>> { ["Player"] = new ushort[] { 100 } });
        foreach (var caller in new[]
        {
            new PulseContextData(),
            PulseContextData.CreateUserContext("alice") with { SourceType = CallSourceType.InternalService }
        })
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => policy.EvaluateAsync(
                new GatewayActorInvocationContext("Player", "alice", 100, GatewayActorInvocationKind.Ask, caller)).AsTask());
    }
}
