using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Authentication;
using PulseRPC.Client.Configuration;
using Xunit;

namespace PulseRPC.Client.Tests;

public class PulseClientBuilderContractTests
{
    [Fact]
    public void Build_WithAuthentication_MustFailExplicitly()
    {
        var builder = new PulseClientBuilder()
            .WithAuthentication(new TestAuthenticationProvider());

        var ex = Assert.Throws<NotSupportedException>(() => builder.Build());
        Assert.Contains("WithAuthentication", ex.Message);
    }

    [Fact]
    public void Build_WithConnectionPooling_MustFailExplicitly()
    {
        var builder = new PulseClientBuilder()
            .WithConnectionPooling(ConnectionPoolOptions.FixedSize(2));

        var ex = Assert.Throws<NotSupportedException>(() => builder.Build());
        Assert.Contains("WithConnectionPooling", ex.Message);
    }

    [Fact]
    public void Build_WithRetryPolicy_MustFailExplicitly()
    {
        var builder = new PulseClientBuilder()
            .WithRetryPolicy(new RetryPolicy());

        var ex = Assert.Throws<NotSupportedException>(() => builder.Build());
        Assert.Contains("WithRetryPolicy", ex.Message);
    }

    [Fact]
    public void Build_WithLoadBalancingOptions_MustFailExplicitly()
    {
        var builder = new PulseClientBuilder()
            .WithLoadBalancing(
                LoadBalancingStrategy.RoundRobin,
                new Dictionary<string, object> { ["weight"] = 1 });

        var ex = Assert.Throws<NotSupportedException>(() => builder.Build());
        Assert.Contains("options", ex.Message);
    }

    [Fact]
    public void Build_WithLoadBalancingStrategyOnly_MustUseStrategy()
    {
        using var client = new PulseClientBuilder()
            .WithLoadBalancing(LoadBalancingStrategy.Random)
            .Build();

        Assert.Equal(LoadBalancingStrategy.Random, client.LoadBalancer.Strategy);
    }

    private sealed class TestAuthenticationProvider : IAuthenticationProvider
    {
        public string AuthenticationType => "Test";

        public Task<AuthenticationToken> GetTokenAsync(string serviceName, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateToken());

        public Task<AuthenticationToken> RefreshTokenAsync(AuthenticationToken currentToken, CancellationToken cancellationToken = default)
            => Task.FromResult(CreateToken());

        public bool IsTokenValid(AuthenticationToken token)
            => !token.IsExpired;

        public Task RevokeTokenAsync(AuthenticationToken token, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static AuthenticationToken CreateToken()
            => new()
            {
                Token = "test-token",
                ExpiresAt = DateTime.UtcNow.AddMinutes(5)
            };
    }
}
