using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Messaging;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Security;
using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests;

public class PulseServerLifecycleTests
{
    [Fact]
    public async Task ClientConnected_MustReceiveCreatedServerChannel()
    {
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        var connected = new TaskCompletionSource<IServerChannel>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += (_, e) => connected.TrySetResult(e.Channel);

        await server.StartAsync();
        try
        {
            listener.Accept(new MockServerTransport("conn-1"));

            var channel = await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            channel.Id.Should().Be("conn-1");
            channelManager.GetChannel("conn-1").Should().BeSameAs(channel);
        }
        finally
        {
            await server.StopAsync();
            server.Dispose();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task ClientConnectedFailure_MustRemoveRegisteredChannelAndCloseTransport()
    {
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        server.ClientConnected += (_, _) => throw new InvalidOperationException("handler failed");

        await server.StartAsync();
        try
        {
            var transport = new MockServerTransport("conn-failed");
            listener.Accept(transport);

            var cleaned = await SpinAsync(
                () => channelManager.ConnectionCount == 0 && transport.State == ConnectionState.Disconnected,
                TimeSpan.FromSeconds(3));

            cleaned.Should().BeTrue();
            channelManager.GetChannel("conn-failed").Should().BeNull();
        }
        finally
        {
            await server.StopAsync();
            server.Dispose();
            channelManager.Dispose();
        }
    }

    [Fact]
    public void PulseServerBuilder_Build_MustApplyConfiguredTransports()
    {
        var services = new ServiceCollection();

        services
            .AddPulseServerBuilder()
            .AddTcpTransport("main", 5000, isDefault: true)
            .AddKcpTransport("kcp", 5001)
            .Build();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PulseServerOptions>>().Value;

        options.Transports.Should().ContainSingle(x =>
            x.Name == "main" &&
            x.Type == TransportType.TCP &&
            x.Port == 5000 &&
            x.IsDefault);
        options.Transports.Should().ContainSingle(x =>
            x.Name == "kcp" &&
            x.Type == TransportType.KCP &&
            x.Port == 5001 &&
            !x.IsDefault);
    }

    [Fact]
    public async Task ManagementChannelsAndFilteredBroadcast_MustUseRegisteredServerChannels()
    {
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(new TestServerListener()),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        var transportA = new MockServerTransport("conn-a");
        var channelA = channelManager.AddChannel(transportA);
        var authA = new AuthenticationContext("conn-a");
        authA.SetClientAuthentication("user-a", "user-a");
        channelA.SetAuthentication(authA);

        var transportB = new MockServerTransport("conn-b");
        var channelB = channelManager.AddChannel(transportB);
        var authB = new AuthenticationContext("conn-b");
        authB.SetClientAuthentication("user-b", "user-b");
        channelB.SetAuthentication(authB);

        try
        {
            server.GetChannel("conn-a").Should().BeSameAs(channelA);
            server.GetAllChannels().Should().ContainSingle(x => x.ConnectionId == "conn-a")
                .And.ContainSingle(x => x.ConnectionId == "conn-b");

            var sent = await server.BroadcastAsync(
                new byte[] { 1, 2, 3 },
                context => context.ConnectionId == "conn-a");

            sent.Should().Be(1);
            transportA.SentFrames.Should().ContainSingle();
            transportB.SentFrames.Should().BeEmpty();
        }
        finally
        {
            server.Dispose();
            channelManager.Dispose();
        }
    }

    [Fact]
    public void GetRegisteredServices_MustReturnGeneratedManifestServices()
    {
        ServiceManifestRegistry.Clear();

        var expected = new ServiceInfo
        {
            ServiceName = "RoomHub",
            HubType = typeof(ITestHub),
            ChannelName = "main",
            Methods = new[]
            {
                new ServiceMethodInfo
                {
                    MethodName = "JoinAsync",
                    DeclaringHubTypeName = typeof(ITestHub).FullName!,
                    ProtocolId = 0x1234,
                    ReturnTypeName = "System.Threading.Tasks.Task",
                    IsAsync = true,
                    Parameters = new[]
                    {
                        new ServiceParameterInfo
                        {
                            Name = "roomId",
                            TypeName = "System.String"
                        }
                    }
                }
            }
        };

        ServiceManifestRegistry.Register(new TestServiceManifest(new[] { expected }));

        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(new TestServerListener()),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        try
        {
            var services = server.GetRegisteredServices();

            services.Should().ContainSingle().Which.Should().BeSameAs(expected);
        }
        finally
        {
            server.Dispose();
            channelManager.Dispose();
            ServiceManifestRegistry.Clear();
        }
    }

    [Fact]
    public void GetPerformanceMetrics_MustUseEngineStatistics()
    {
        var stats = new EngineStatistics
        {
            TotalMessagesProcessed = 11,
            TotalMessagesDropped = 2,
            AverageLatencyMs = 3.5,
            CurrentThroughput = 42
        };

        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(stats),
            channelManager,
            new TestTransportIntegrationManager(new TestServerListener()),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        try
        {
            var metrics = server.GetPerformanceMetrics();

            metrics.TotalMessagesProcessed.Should().Be(11);
            metrics.TotalMessagesDropped.Should().Be(2);
            metrics.AverageLatencyMs.Should().Be(3.5);
            metrics.ThroughputMsgsPerSec.Should().Be(42);
            metrics.CpuUsagePercent.Should().BeNaN();
        }
        finally
        {
            server.Dispose();
            channelManager.Dispose();
        }
    }

    private static async Task<bool> SpinAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return predicate();
    }

    private sealed class TestTransportIntegrationManager(IServerListener listener) : ITransportIntegrationManager
    {
        public void RegisterProvider(ITransportProvider provider)
        {
        }

        public IServerListener CreateListener(TransportChannelConfiguration config, ILoggerFactory loggerFactory)
            => listener;

        public IReadOnlyList<string> GetSupportedTransportTypes()
            => new[] { TransportType.TCP.ToString(), TransportType.KCP.ToString() };

        public bool IsSupported(string transportType)
            => true;
    }

    private sealed class TestServerListener : IServerListener
    {
        public string Name => "test";
        public TransportType Type => TransportType.TCP;
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 0);
        public bool IsListening { get; private set; }

        public event EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsListening = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsListening = false;
            return Task.CompletedTask;
        }

        public void Accept(IServerTransport transport)
            => ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(transport));

        public void Dispose()
        {
            IsListening = false;
        }
    }

    private interface ITestHub
    {
    }

    private sealed class TestServiceManifest(IReadOnlyList<ServiceInfo> services) : IServiceManifest
    {
        public IReadOnlyList<ServiceInfo> Services { get; } = services;
    }

    private sealed class TestMessageEngine(EngineStatistics? statistics = null) : ITieredMessageEngine
    {
        private readonly EngineStatistics _statistics = statistics ?? new EngineStatistics();

        public Task StartAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAsync()
            => Task.CompletedTask;

        public void RegisterConnection(string connectionId)
        {
        }

        public void UnregisterConnection(string connectionId)
        {
        }

        public bool TryEnqueueMessage(string connectionId, MessagePacketHolder message, MessagePriority priority)
            => true;

        public EngineStatistics GetStatistics()
            => _statistics;
    }
}
