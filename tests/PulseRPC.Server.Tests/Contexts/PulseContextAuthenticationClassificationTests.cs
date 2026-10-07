using FluentAssertions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Security;
using Xunit;

namespace PulseRPC.Server.Tests.Contexts;

public sealed class PulseContextAuthenticationClassificationTests
{
    [Fact]
    public void ValidatedSessionAndTenantClaims_AreAvailableToAdmissionPolicies_AsASnapshot()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("game_session", "original-session"),
            new System.Security.Claims.Claim("tenant", "tenant-a"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "player")
        }, "validated-jwt");
        var authentication = new AuthenticationContext("connection");
        authentication.SetClientAuthentication("player-a", "Player A",
            principal: new System.Security.Claims.ClaimsPrincipal(identity));
        using var context = PulseContextData.FromAuthenticationContext(authentication);
        identity.AddClaim(new System.Security.Claims.Claim("game_session", "later-session"));
        Assert.Equal("original-session", context.Claims["game_session"]);
        Assert.Equal("tenant-a", context.Claims["tenant"]);
        Assert.Contains("player", context.Roles);
        Assert.False(context.Claims.ContainsKey(System.Security.Claims.ClaimTypes.Role));
    }

    [Fact]
    public void FromAuthenticationContext_ClientIdentityIsExternalUser()
    {
        var authentication = new AuthenticationContext("client-1");
        authentication.SetClientAuthentication("user-1", "Player One");

        using var context = PulseContextData.FromAuthenticationContext(authentication);

        context.SourceType.Should().Be(CallSourceType.ExternalUser);
        context.UserId.Should().Be("user-1");
    }

    [Fact]
    public void FromAuthenticationContext_ServiceIdentityIsInternalService_NotExternalUser()
    {
        var authentication = new AuthenticationContext("node-connection-1");
        authentication.SetServiceAuthentication(
            "node-1",
            "node-1",
            token: "cluster-token",
            scopes: new[] { "cluster-node" });

        using var context = PulseContextData.FromAuthenticationContext(authentication);

        context.SourceType.Should().Be(CallSourceType.InternalService);
        context.UserId.Should().BeNull();
        context.Permissions.Should().Contain("cluster-node");
    }

    [Fact]
    public void Create_UnauthenticatedRpcIsExternalAndHasNoInternalWildcardPermission()
    {
        using var context = PulseContextData.Create(
            new RpcMessage(),
            transport: null,
            authContext: null,
            timeout: TimeSpan.FromSeconds(1));

        context.SourceType.Should().Be(CallSourceType.ExternalUser);
        context.UserId.Should().BeNull();
        context.Permissions.Should().NotContain("*");
    }

    [Fact]
    public void CreateAnonymousClientContext_IsExternalAndHasNoElevatedPermissions()
    {
        var context = PulseContextData.CreateAnonymousClientContext(
            connectionId: "client-2",
            transport: null,
            cancellationToken: default);

        context.SourceType.Should().Be(CallSourceType.ExternalUser);
        context.CallerId.Should().Be("client-2");
        context.UserId.Should().BeNull();
        context.Permissions.Should().BeEmpty();
        context.Roles.Should().BeEmpty();
    }
}
