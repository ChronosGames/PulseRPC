using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Security;
using Xunit;

namespace PulseRPC.Server.Tests;

public sealed class AuthorizationGateTests
{
    private const ushort ProtocolId = 0x1234;
    private const string MethodName = "ITestHub.RunAsync";

    [Fact]
    public void None_WithoutContext_Allows()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => AuthorizationGate.Enforce(
            provider, AuthorizationDescriptor.None, ProtocolId, MethodName);

        act.Should().NotThrow();
    }

    [Fact]
    public void Authorize_AnonymousExternalUser_FailsClosed()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(requireAuthentication: true);
        using var scope = PulseContext.SetContext(new PulseContextData
        {
            SourceType = CallSourceType.ExternalUser,
            CallerId = "anonymous",
        });

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*authentication is required*");
    }

    [Fact]
    public void AllowAnonymous_OnlyRemovesAuthenticationRequirement_NotRoleRequirement()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(
            allowAnonymous: true,
            requireAuthentication: false,
            requirements: new[]
            {
                new AuthorizationRequirement(AuthorizationRequirementKind.Role, "Administrators"),
            });
        using var scope = PulseContext.SetContext(new PulseContextData
        {
            SourceType = CallSourceType.ExternalUser,
            CallerId = "anonymous",
        });

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*missing required role 'Administrators'*");
    }

    [Fact]
    public void RolePermissionAndScope_AllSatisfied_Allows()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(
            requireAuthentication: true,
            requirements: new[]
            {
                new AuthorizationRequirement(AuthorizationRequirementKind.Role, "Administrators"),
                new AuthorizationRequirement(AuthorizationRequirementKind.Permission, "users.write"),
                new AuthorizationRequirement(AuthorizationRequirementKind.Scope, "rpc.execute"),
            });
        using var scope = PulseContext.SetContext(PulseContextData.CreateUserContext(
            "user-1",
            permissions: new HashSet<string> { "users.write", "rpc.execute" },
            roles: new HashSet<string> { "Administrators" }));

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        act.Should().NotThrow();
    }

    [Fact]
    public void RequirePermission_WithInternalBypass_AllowsInternalService()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(
            requirements: new[]
            {
                new AuthorizationRequirement(
                    AuthorizationRequirementKind.Permission,
                    "users.write",
                    allowInternal: true),
            });
        using var scope = PulseContext.SetContext(
            PulseContextData.CreateServiceContext("Backend", "node-1"));

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(true, false, CallSourceType.ExternalUser)]
    [InlineData(false, true, CallSourceType.InternalService)]
    public void SourceRestriction_WrongSource_Denies(
        bool internalOnly,
        bool externalOnly,
        CallSourceType sourceType)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(
            internalOnly: internalOnly,
            externalOnly: externalOnly);
        using var scope = PulseContext.SetContext(new PulseContextData
        {
            SourceType = sourceType,
            CallerId = "caller-1",
        });

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void Policy_WithoutEvaluator_FailsClosed()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(policies: new[] { "tenant-owner" });
        using var scope = PulseContext.SetContext(PulseContextData.CreateUserContext("user-1"));

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("*no IPulseAuthorizationPolicyEvaluator is registered*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Policy_UsesRegisteredEvaluator(bool allowed)
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IPulseAuthorizationPolicyEvaluator>(new TestPolicyEvaluator(allowed))
            .BuildServiceProvider();
        var descriptor = new AuthorizationDescriptor(policies: new[] { "tenant-owner" });
        using var scope = PulseContext.SetContext(PulseContextData.CreateUserContext("user-1"));

        var act = () => AuthorizationGate.Enforce(provider, descriptor, ProtocolId, MethodName);

        if (allowed)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<UnauthorizedAccessException>()
                .WithMessage("*policy 'tenant-owner' was not satisfied*");
        }
    }

    private sealed class TestPolicyEvaluator : IPulseAuthorizationPolicyEvaluator
    {
        private readonly bool _allowed;

        public TestPolicyEvaluator(bool allowed)
        {
            _allowed = allowed;
        }

        public bool Evaluate(string policy, IPulseContext context) => _allowed;
    }
}
