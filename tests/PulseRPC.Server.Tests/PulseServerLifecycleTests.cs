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
    public async Task Stop_MustWaitForInFlightConnectionRollback()
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

        var closeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new MockServerTransport("conn-rollback")
        {
            CloseHandler = async _ =>
            {
                closeEntered.TrySetResult();
                await releaseClose.Task;
            }
        };

        await server.StartAsync();
        try
        {
            listener.Accept(transport);
            await closeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var stop = server.StopAsync();
            await Task.Delay(50);
            stop.IsCompleted.Should().BeFalse("server shutdown must join accepted connection rollback tasks");

            releaseClose.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            transport.DisposeCount.Should().Be(1);
        }
        finally
        {
            releaseClose.TrySetResult();
            await server.StopAsync();
            server.Dispose();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task ConnectionAcceptedDuringStopping_MustCloseAndDisposeUnpublishedTransport()
    {
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        var rejectedTransport = new MockServerTransport("accepted-during-stop");
        server.StateChanged += (_, args) =>
        {
            if (args.NewState == ServerState.Stopping)
            {
                listener.Accept(rejectedTransport);
            }
        };

        await server.StartAsync();
        try
        {
            await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));

            rejectedTransport.DisposeCount.Should().Be(1);
            rejectedTransport.State.Should().Be(ConnectionState.Disconnected);
            channelManager.ConnectionCount.Should().Be(0);
        }
        finally
        {
            await server.DisposeAsync();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task ClientConnectedCallback_CanSynchronouslyStopAndDisposeWithoutSelfWait()
    {
        var engine = new ControllableMessageEngine();
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientConnected += (_, _) =>
        {
#pragma warning disable xUnit1031 // Deliberately reproduces synchronous lifecycle-callback reentrancy.
            server.StopAsync().GetAwaiter().GetResult();
            server.Dispose();
#pragma warning restore xUnit1031
            callbackCompleted.TrySetResult();
        };
        var transport = new MockServerTransport("connected-reentrant-stop");

        await server.StartAsync();
        await Task.Run(() => listener.Accept(transport)).WaitAsync(TimeSpan.FromSeconds(3));
        await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        engine.StopCount.Should().Be(1);
        transport.DisposeCount.Should().Be(1);
        channelManager.Dispose();
    }

    [Fact]
    public async Task ClientDisconnectedCallback_CanSynchronouslyStopAndDisposeWithoutSelfWait()
    {
        var engine = new ControllableMessageEngine();
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        var transport = new MockServerTransport("disconnected-reentrant-stop");
        var callbackCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientDisconnected += (_, _) =>
        {
#pragma warning disable xUnit1031 // Deliberately reproduces synchronous lifecycle-callback reentrancy.
            server.StopAsync().GetAwaiter().GetResult();
            server.Dispose();
#pragma warning restore xUnit1031
            callbackCompleted.TrySetResult();
        };

        await server.StartAsync();
        listener.Accept(transport);
        (await SpinAsync(() => channelManager.ConnectionCount == 1, TimeSpan.FromSeconds(3)))
            .Should().BeTrue();

        await Task.Run(async () => await server.StopAsync()).WaitAsync(TimeSpan.FromSeconds(3));
        await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        engine.StopCount.Should().Be(1);
        transport.DisposeCount.Should().Be(1);
        channelManager.Dispose();
    }

    [Fact]
    public async Task DisposeWithoutStart_MustStopConstructorStartedMessageResources()
    {
        var engine = new ControllableMessageEngine();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(new TestServerListener()),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        await server.DisposeAsync();

        engine.StartCount.Should().Be(0);
        engine.StopCount.Should().Be(1);
        channelManager.Dispose();
    }

    [Fact]
    public async Task Stop_MustCloseAllActiveChannelsBeforeReturning()
    {
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        var transport = new MockServerTransport("active-at-stop");

        await server.StartAsync();
        try
        {
            listener.Accept(transport);
            (await SpinAsync(
                () => channelManager.ConnectionCount == 1,
                TimeSpan.FromSeconds(3))).Should().BeTrue();

            await server.StopAsync();

            channelManager.ConnectionCount.Should().Be(0);
            transport.State.Should().Be(ConnectionState.Disconnected);
        }
        finally
        {
            await server.DisposeAsync();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task ConcurrentStopAndDispose_MustJoinTheSameShutdown()
    {
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new ControllableMessageEngine
        {
            StopHandler = async () =>
            {
                stopEntered.TrySetResult();
                await releaseStop.Task;
            }
        };
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        await server.StartAsync();
        try
        {
            var firstStop = server.StopAsync();
            await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var secondStop = server.StopAsync();
            var dispose = server.DisposeAsync().AsTask();

            secondStop.Should().BeSameAs(firstStop);
            await Task.Delay(50);
            firstStop.IsCompleted.Should().BeFalse();
            secondStop.IsCompleted.Should().BeFalse();
            dispose.IsCompleted.Should().BeFalse();

            releaseStop.TrySetResult();
            await Task.WhenAll(firstStop, secondStop, dispose).WaitAsync(TimeSpan.FromSeconds(3));

            engine.StopCount.Should().Be(1);
            listener.DisposeCount.Should().Be(1);
            server.State.Should().Be(ServerState.Stopped);
        }
        finally
        {
            releaseStop.TrySetResult();
            await server.DisposeAsync();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task StoppingEventReentrancy_MustJoinPublishedStopTask()
    {
        var engine = new ControllableMessageEngine();
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        server.StateChanged += (_, args) =>
        {
            if (args.NewState == ServerState.Stopping)
            {
                server.Dispose();
            }
        };

        await server.StartAsync();
        await Task.Run(async () => await server.StopAsync())
            .WaitAsync(TimeSpan.FromSeconds(3));

        engine.StopCount.Should().Be(1);
        listener.DisposeCount.Should().Be(1);
        server.State.Should().Be(ServerState.Stopped);
        channelManager.Dispose();
    }

    [Fact]
    public async Task DisposeInitiatedStoppingEventReentrancy_MustNotWaitOnOwnDisposeTask()
    {
        var engine = new ControllableMessageEngine();
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        server.StateChanged += (_, args) =>
        {
            if (args.NewState == ServerState.Stopping)
            {
                server.Dispose();
            }
        };

        await server.StartAsync();
        await Task.Run(server.Dispose).WaitAsync(TimeSpan.FromSeconds(3));

        engine.StopCount.Should().Be(1);
        listener.DisposeCount.Should().Be(1);
        server.State.Should().Be(ServerState.Stopped);
        channelManager.Dispose();
    }

    [Fact]
    public async Task StartFailure_MustRollbackEngineAndDisposeUnpublishedListener()
    {
        var engine = new ControllableMessageEngine();
        var acceptedBeforeFailure = new MockServerTransport("accepted-before-start-failure");
        TestServerListener? listener = null;
        listener = new TestServerListener
        {
            StartHandler = _ =>
            {
                listener!.Accept(acceptedBeforeFailure);
                throw new InvalidOperationException("listener start failed");
            }
        };
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        try
        {
            var start = () => server.StartAsync();
            await start.Should().ThrowAsync<InvalidOperationException>();

            server.State.Should().Be(ServerState.Stopped);
            engine.StartCount.Should().Be(1);
            engine.StopCount.Should().Be(1);
            listener.DisposeCount.Should().Be(1);
            channelManager.ConnectionCount.Should().Be(0);
            acceptedBeforeFailure.State.Should().Be(ConnectionState.Disconnected);

            await server.DisposeAsync();
            engine.StopCount.Should().Be(1, "completed start rollback must not be repeated by Dispose");
        }
        finally
        {
            await server.DisposeAsync();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task DisposeDuringStart_MustWaitAndCannotReturnToRunning()
    {
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new ControllableMessageEngine();
        var listener = new TestServerListener
        {
            StartHandler = async _ =>
            {
                startEntered.TrySetResult();
                await releaseStart.Task;
            }
        };
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            engine,
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        var start = server.StartAsync();
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var dispose = server.DisposeAsync().AsTask();

        try
        {
            await Task.Delay(50);
            dispose.IsCompleted.Should().BeFalse();

            releaseStart.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
            await dispose.WaitAsync(TimeSpan.FromSeconds(3));

            server.State.Should().Be(ServerState.Stopped);
            server.IsRunning.Should().BeFalse();
            engine.StopCount.Should().Be(1);
            listener.DisposeCount.Should().Be(1);
        }
        finally
        {
            releaseStart.TrySetResult();
            await server.DisposeAsync();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task PublicEvents_MustUseThePulseServerFacadeAsSender()
    {
        var listener = new TestServerListener();
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(listener),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        var senders = new List<object?>();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StateChanged += (sender, _) => senders.Add(sender);
        server.ClientConnected += (sender, _) =>
        {
            senders.Add(sender);
            connected.TrySetResult();
        };
        server.ClientDisconnected += (sender, _) =>
        {
            senders.Add(sender);
            disconnected.TrySetResult();
        };

        await server.StartAsync();
        try
        {
            listener.Accept(new MockServerTransport("event-sender"));
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            channelManager.RemoveChannel("event-sender").Should().BeTrue();
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await server.StopAsync();

            senders.Should().NotBeEmpty().And.OnlyContain(sender => ReferenceEquals(sender, server));
        }
        finally
        {
            await server.DisposeAsync();
            channelManager.Dispose();
        }
    }

    [Fact]
    public async Task ClientDisconnected_MustForwardChannelManagerEvent()
    {
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(new TestServerListener()),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));
        var transport = new MockServerTransport("conn-disconnected");
        var channel = channelManager.AddChannel(transport);
        var disconnected = new TaskCompletionSource<ClientDisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ClientDisconnected += (_, e) => disconnected.TrySetResult(e);

        try
        {
            channelManager.RemoveChannel(channel.Id).Should().BeTrue();

            var eventArgs = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(3));
            eventArgs.Channel.Should().BeSameAs(channel);
        }
        finally
        {
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
    public void ChannelPool_MustBeAViewOfTheRuntimeChannelRegistry()
    {
#pragma warning disable CS0618 // Intentional coverage of the read-only compatibility view.
        var channelManager = new ServerChannelManager(NullLogger<ServerChannelManager>.Instance);
        var server = new PulseServer(
            new TestMessageEngine(),
            channelManager,
            new TestTransportIntegrationManager(new TestServerListener()),
            NullLoggerFactory.Instance,
            Options.Create(new PulseServerOptions().AddTcp("main", 5000)));

        try
        {
            var channel = channelManager.AddChannel(new MockServerTransport("conn-pool"));

            server.ChannelPool.Count.Should().Be(1);
            server.ChannelPool.GetChannel("conn-pool").Should().BeSameAs(channel);
            server.ChannelPool.GetAllConnectionIds().Should().Equal("conn-pool");

            var mutate = () => server.ChannelPool.Unregister("conn-pool");
            mutate.Should().Throw<NotSupportedException>();

            channelManager.RemoveChannel("conn-pool").Should().BeTrue();
            channelManager.ConnectionCount.Should().Be(0);
            server.GetChannel("conn-pool").Should().BeNull();
            server.ChannelPool.Count.Should().Be(0);
        }
        finally
        {
            server.Dispose();
            channelManager.Dispose();
        }
#pragma warning restore CS0618
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
        public Func<CancellationToken, Task>? StartHandler { get; init; }
        public Func<CancellationToken, Task>? StopHandler { get; init; }
        public int DisposeCount { get; private set; }

        public event EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (StartHandler is not null)
            {
                await StartHandler(cancellationToken);
            }

            IsListening = true;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (StopHandler is not null)
            {
                await StopHandler(cancellationToken);
            }

            IsListening = false;
        }

        public void Accept(IServerTransport transport)
            => ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(transport));

        public void Dispose()
        {
            DisposeCount++;
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

        public void RegisterConnection(IServerChannel channel)
        {
        }

        public void UnregisterConnection(string connectionId)
        {
        }

        public void UnregisterConnection(IServerChannel channel)
        {
        }

        public bool TryEnqueueMessage(string connectionId, MessagePacketHolder message, MessagePriority priority)
            => true;

        public bool TryEnqueueMessage(IServerChannel sourceChannel, MessagePacketHolder message, MessagePriority priority)
            => true;

        public EngineStatistics GetStatistics()
            => _statistics;
    }

    private sealed class ControllableMessageEngine : ITieredMessageEngine
    {
        public Func<CancellationToken, Task>? StartHandler { get; init; }
        public Func<Task>? StopHandler { get; init; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            if (StartHandler is not null)
            {
                await StartHandler(cancellationToken);
            }
        }

        public async Task StopAsync()
        {
            StopCount++;
            if (StopHandler is not null)
            {
                await StopHandler();
            }
        }

        public void RegisterConnection(string connectionId)
        {
        }

        public void RegisterConnection(IServerChannel channel)
        {
        }

        public void UnregisterConnection(string connectionId)
        {
        }

        public void UnregisterConnection(IServerChannel channel)
        {
        }

        public bool TryEnqueueMessage(string connectionId, MessagePacketHolder message, MessagePriority priority)
            => true;

        public bool TryEnqueueMessage(IServerChannel sourceChannel, MessagePacketHolder message, MessagePriority priority)
            => true;

        public EngineStatistics GetStatistics() => new();
    }
}
