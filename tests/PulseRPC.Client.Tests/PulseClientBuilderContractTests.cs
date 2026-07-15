using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Client.Channels;
using PulseRPC.Client.Configuration;
using PulseRPC.Client.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Client.Tests;

public class PulseClientBuilderContractTests
{
    [Fact]
    public void ClientOptions_MustExposeOnlyRuntimeConsumedOptions()
    {
        Assert.NotNull(typeof(ClientOptions).GetProperty(nameof(ClientOptions.Name)));
        Assert.NotNull(typeof(ClientOptions).GetProperty(nameof(ClientOptions.LoadBalancing)));
        Assert.Null(typeof(ClientOptions).GetProperty("DefaultTimeout"));
        Assert.Null(typeof(ClientOptions).GetProperty("MaxConcurrentConnections"));
        Assert.Null(typeof(ClientOptions).GetProperty("Settings"));
    }

    [Fact]
    public void ServiceProxyOptions_MustExposeOnlyRoutingContext()
    {
        Assert.NotNull(typeof(ServiceProxyOptions).GetProperty(nameof(ServiceProxyOptions.LoadBalancingHint)));
        Assert.NotNull(typeof(ServiceProxyOptions).GetProperty(nameof(ServiceProxyOptions.StickyKey)));
        Assert.Null(typeof(ServiceProxyOptions).GetProperty("ConnectionId"));
        Assert.Null(typeof(ServiceProxyOptions).GetProperty("RetryPolicy"));
    }

    [Fact]
    public void RemovedCompatibilityModels_MustNotBeResolvable()
    {
        var assembly = typeof(ClientOptions).Assembly;
        Assert.Null(assembly.GetType("PulseRPC.Client.Configuration.ClientPresets"));
        Assert.Null(assembly.GetType("PulseRPC.Client.Configuration.ChannelPresets"));
        Assert.Null(assembly.GetType("PulseRPC.Client.Configuration.ConnectionPoolOptions"));
        Assert.Null(assembly.GetType("PulseRPC.Client.Configuration.ServiceDiscoveryOptions"));
        Assert.Null(assembly.GetType("PulseRPC.Client.Configuration.EventListenerOptions"));
    }

    [Fact]
    public void RemovedCompatibilityBuilderMethods_MustNotBeResolvable()
    {
        Assert.Null(typeof(PulseClientBuilder).GetMethod("UseDefaults"));
        Assert.Null(typeof(PulseClientBuilder).GetMethod("UseGameClientPreset"));
        Assert.Null(typeof(PulseClientBuilder).GetMethod("WithConnectionPooling"));
        Assert.Null(typeof(PulseClientBuilder).GetMethod("WithRetryPolicy"));
        Assert.Null(typeof(PulseClientBuilder).GetMethod("WithAuthentication"));
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

    private sealed class TestWeightProvider : IConnectionWeightProvider
    {
        public int GetWeight(IClientChannel connection) => 1;
    }
}
