using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Authentication;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Configuration;
using PulseRPC.Client.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Client.Tests;

#pragma warning disable CS0618 // Intentional compatibility coverage of unsupported builder APIs.
public class PulseClientBuilderContractTests
{
    [Theory]
    [InlineData(nameof(ClientOptions.DefaultTimeout))]
    [InlineData(nameof(ClientOptions.MaxConcurrentConnections))]
    [InlineData(nameof(ClientOptions.EnableDebugMode))]
    [InlineData(nameof(ClientOptions.EnableStatistics))]
    [InlineData(nameof(ClientOptions.AutoCleanupInterval))]
    [InlineData(nameof(ClientOptions.Settings))]
    public void UnwiredClientOptions_MustBeMarkedObsolete(string propertyName)
    {
        var property = typeof(ClientOptions).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(Attribute.GetCustomAttribute(property!, typeof(ObsoleteAttribute)));
    }

    [Theory]
    [InlineData(nameof(ServiceProxyOptions.ConnectionId))]
    [InlineData(nameof(ServiceProxyOptions.ChannelName))]
    [InlineData(nameof(ServiceProxyOptions.Tags))]
    [InlineData(nameof(ServiceProxyOptions.PreferredRegion))]
    [InlineData(nameof(ServiceProxyOptions.Timeout))]
    [InlineData(nameof(ServiceProxyOptions.RetryPolicy))]
    [InlineData(nameof(ServiceProxyOptions.UseCache))]
    public void UnwiredServiceProxyOptions_MustBeMarkedObsolete(string propertyName)
    {
        var property = typeof(ServiceProxyOptions).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(Attribute.GetCustomAttribute(property!, typeof(ObsoleteAttribute)));
    }

    [Fact]
    public void EffectiveServiceProxyOptions_MustRemainSupported()
    {
        Assert.Null(Attribute.GetCustomAttribute(
            typeof(ServiceProxyOptions).GetProperty(nameof(ServiceProxyOptions.LoadBalancingHint))!,
            typeof(ObsoleteAttribute)));
        Assert.Null(Attribute.GetCustomAttribute(
            typeof(ServiceProxyOptions).GetProperty(nameof(ServiceProxyOptions.StickyKey))!,
            typeof(ObsoleteAttribute)));
    }

    [Fact]
    public void UnwiredCompatibilityModels_MustBeMarkedObsolete()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ClientPresets), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ChannelPresets), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(TransportChannelOptions), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ConnectionPoolOptions), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ServiceConnectionOptions), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(RetryPolicy), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(PoolingStrategy), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(EventListenerOptions), typeof(ObsoleteAttribute)));
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(ServiceDiscoveryOptions), typeof(ObsoleteAttribute)));
    }

    [Theory]
    [InlineData(nameof(PulseClientBuilder.UseDefaults))]
    [InlineData(nameof(PulseClientBuilder.UseGameClientPreset))]
    [InlineData(nameof(PulseClientBuilder.UseHighThroughputPreset))]
    [InlineData(nameof(PulseClientBuilder.UseDevelopmentPreset))]
    public void LegacyPresetBuilderMethods_MustBeMarkedObsolete(string methodName)
    {
        var method = typeof(PulseClientBuilder).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.NotNull(Attribute.GetCustomAttribute(method!, typeof(ObsoleteAttribute)));
    }

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

    [Theory]
    [InlineData(LoadBalancingStrategy.WeightedRoundRobin)]
    [InlineData(LoadBalancingStrategy.ConsistentHash)]
    public void Build_WithAdvancedLoadBalancingStrategy_MustUseStrategy(
        LoadBalancingStrategy strategy)
    {
        using var client = new PulseClientBuilder()
            .WithLoadBalancing(strategy)
            .Build();

        Assert.Equal(strategy, client.LoadBalancer.Strategy);
    }

    [Fact]
    public void Build_WithDynamicWeightProvider_MustCreateContextualLoadBalancer()
    {
        using var client = new PulseClientBuilder()
            .Configure(options => options.LoadBalancing.WeightProvider = new TestWeightProvider())
            .WithLoadBalancing(LoadBalancingStrategy.WeightedRoundRobin)
            .Build();

        Assert.IsAssignableFrom<IContextualLoadBalancer>(client.LoadBalancer);
    }

    [Fact]
    public void Build_WithInvalidConsistentHashVirtualNodeCount_MustFailExplicitly()
    {
        var builder = new PulseClientBuilder()
            .Configure(options => options.LoadBalancing.ConsistentHashVirtualNodes = 0)
            .WithLoadBalancing(LoadBalancingStrategy.ConsistentHash);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("虚拟节点", exception.Message);
    }

    [Fact]
    public void Transport_WithCompression_MustBeConstructible()
    {
        var tcpOptions = new TcpTransportOptions
        {
            UseCompression = true
        };
        var kcpOptions = new KcpTransportOptions
        {
            UseCompression = true
        };

        using var tcp = new TcpClientTransport("tcp", tcpOptions);
        using var kcp = new KcpClientTransport("kcp", kcpOptions);
    }

    [Fact]
    public void Transport_WithEncryptionButNoKeyProvider_MustFailAtConstruction()
    {
        Assert.Throws<InvalidOperationException>(() => new TcpClientTransport(
            "tcp", new TcpTransportOptions { UseEncryption = true }));
        Assert.Throws<InvalidOperationException>(() => new KcpClientTransport(
            "kcp", new KcpTransportOptions { UseEncryption = true }));
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

    private sealed class TestWeightProvider : IConnectionWeightProvider
    {
        public int GetWeight(IClientChannel connection) => 1;
    }
}
#pragma warning restore CS0618
