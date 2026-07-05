using FluentAssertions;
using PulseRPC.Infrastructure.Etcd;
using Xunit;

namespace PulseRPC.Infrastructure.Tests;

/// <summary>
/// P8：etcd 后端 <c>host:port</c> 值解析（按最后一个冒号切分，兼容 IPv6 字面量）。
/// </summary>
public class EtcdEndpointParseTests
{
    [Theory]
    [InlineData("10.0.0.5:5000", "10.0.0.5", 5000)]
    [InlineData("game-host.internal:8080", "game-host.internal", 8080)]
    [InlineData("[fe80::1]:9000", "[fe80::1]", 9000)]
    public void TryParseEndpoint_ValidValues_Parse(string value, string expectedHost, int expectedPort)
    {
        EtcdDiscoveryProvider.TryParseEndpoint(value, out var endpoint).Should().BeTrue();
        endpoint.Host.Should().Be(expectedHost);
        endpoint.Port.Should().Be(expectedPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-port")]
    [InlineData("host:")]
    [InlineData(":5000")]
    [InlineData("host:notaport")]
    [InlineData("host:70000")]
    [InlineData("host:0")]
    public void TryParseEndpoint_InvalidValues_Fail(string value)
    {
        EtcdDiscoveryProvider.TryParseEndpoint(value, out _).Should().BeFalse();
    }
}
