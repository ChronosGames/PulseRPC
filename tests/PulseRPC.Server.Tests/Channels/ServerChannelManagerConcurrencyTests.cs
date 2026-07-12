using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Authentication;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Channels;

public sealed class ServerChannelManagerConcurrencyTests
{
    [Fact]
    public async Task GetOrRegisterVirtualChannel_ConcurrentSameId_CreatesAndPublishesOnce()
    {
        const int concurrency = 64;
        const string connectionId = "gateway-1:client-1";

        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        using var callersReady = new CountdownEvent(concurrency);
        using var startCallers = new ManualResetEventSlim();
        var createdChannels = new ConcurrentBag<TrackingServerChannel>();
        var factoryCount = 0;
        var connectedCount = 0;
        var disconnectedCount = 0;

        manager.ChannelConnected += (_, _) => Interlocked.Increment(ref connectedCount);
        manager.ChannelDisconnected += (_, _) => Interlocked.Increment(ref disconnectedCount);

        var registrations = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    callersReady.Signal();
                    startCallers.Wait();
                    return manager.GetOrRegisterVirtualChannel(connectionId, id =>
                    {
                        Interlocked.Increment(ref factoryCount);
                        Thread.Sleep(10);
                        var channel = new TrackingServerChannel(id);
                        createdChannels.Add(channel);
                        return channel;
                    });
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        Assert.True(callersReady.Wait(TimeSpan.FromSeconds(5)));
        startCallers.Set();

        var returnedChannels = await Task.WhenAll(registrations)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var winner = Assert.IsType<TrackingServerChannel>(returnedChannels[0]);

        Assert.All(returnedChannels, channel => Assert.Same(winner, channel));
        Assert.Equal(1, Volatile.Read(ref factoryCount));
        Assert.Single(createdChannels);
        Assert.Equal(1, manager.ConnectionCount);
        Assert.Same(winner, manager.GetChannel(connectionId));
        Assert.Equal(1, Volatile.Read(ref connectedCount));
        Assert.Equal(1, manager.GetChannelManagerStats().TotalChannelsCreated);
        Assert.Equal(1, winner.StateChangedSubscriptionCount);
        Assert.Equal(1, winner.MessageParsedSubscriptionCount);
        Assert.Equal(0, winner.DisposeCount);

        Assert.True(manager.RemoveChannel(connectionId));
        Assert.Equal(1, Volatile.Read(ref disconnectedCount));
        Assert.Equal(1, winner.StateChangedUnsubscriptionCount);
        Assert.Equal(1, winner.MessageParsedUnsubscriptionCount);
        Assert.Equal(1, winner.DisposeCount);
    }

    [Fact]
    public void ObserverFailure_MustNotInterruptPublicationOrRemovalCleanup()
    {
        const string connectionId = "observer-failure";
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var channel = new TrackingServerChannel(connectionId);
        manager.ChannelConnected += (_, _) => throw new InvalidOperationException("connect observer failed");
        manager.ChannelDisconnected += (_, _) => throw new InvalidOperationException("disconnect observer failed");

        manager.GetOrRegisterVirtualChannel(connectionId, _ => channel)
            .Should().BeSameAs(channel);
        manager.GetChannel(connectionId).Should().BeSameAs(channel);

        manager.RemoveChannel(connectionId).Should().BeTrue();
        channel.StateChangedUnsubscriptionCount.Should().Be(1);
        channel.MessageParsedUnsubscriptionCount.Should().Be(1);
        channel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void StaleDisconnectedCallback_MustNotRemoveReplacementWithSameId()
    {
        const string connectionId = "same-id";
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var oldChannel = new TrackingServerChannel(connectionId);
        manager.GetOrRegisterVirtualChannel(connectionId, _ => oldChannel);
        var raiseStaleDisconnected = oldChannel.CaptureDisconnectedNotification();

        manager.RemoveChannel(connectionId).Should().BeTrue();
        var replacement = new TrackingServerChannel(connectionId);
        manager.GetOrRegisterVirtualChannel(connectionId, _ => replacement);

        raiseStaleDisconnected();

        manager.GetChannel(connectionId).Should().BeSameAs(replacement);
        replacement.DisposeCount.Should().Be(0);
    }

    [Fact]
    public void MessageWithoutDownstreamOwner_MustDisposePacketHolder()
    {
        const string connectionId = "no-message-owner";
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var channel = new TrackingServerChannel(connectionId);
        manager.GetOrRegisterVirtualChannel(connectionId, _ => channel);
        var header = new MessageHeader(MessageType.Request, "TestHub", "Call");
        var packet = new MessagePacket(header, new byte[] { 1, 2, 3 });
        var holder = new MessagePacketHolder(packet, connectionId);

        channel.RaiseMessageParsed(holder);

        var disposedField = typeof(MessagePacketHolder).GetField(
            "_disposed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        disposedField.Should().NotBeNull();
        disposedField!.GetValue(holder).Should().Be(1);
    }

    [Fact]
    public void MessageWhenEveryDownstreamHandlerFails_MustDisposePacketHolder()
    {
        const string connectionId = "failing-message-owner";
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var channel = new TrackingServerChannel(connectionId);
        manager.GetOrRegisterVirtualChannel(connectionId, _ => channel);
        manager.ChannelMessageParsed += (_, _) => throw new InvalidOperationException("handler failed");
        var holder = new MessagePacketHolder(
            new MessagePacket(new MessageHeader(MessageType.Request, "TestHub", "Call"), [1, 2, 3]),
            connectionId);

        channel.RaiseMessageParsed(holder);

        GetDisposedState(holder).Should().Be(1);
    }

    [Fact]
    public void StopAcceptingChannels_WhenEventUnsubscriptionThrows_MustStillDisposeEveryChannel()
    {
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var throwingChannel = new TrackingServerChannel("throwing-unsubscribe")
        {
            ThrowOnEventUnsubscribe = true
        };
        var followingChannel = new TrackingServerChannel("following-channel");
        manager.GetOrRegisterVirtualChannel(throwingChannel.Id, _ => throwingChannel);
        manager.GetOrRegisterVirtualChannel(followingChannel.Id, _ => followingChannel);

        manager.StopAcceptingChannelsAndCloseAll();

        manager.ConnectionCount.Should().Be(0);
        throwingChannel.DisposeCount.Should().Be(1);
        followingChannel.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentStopDuringDisconnectNotification_MustObserveChannelAlreadyDisposed()
    {
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var channel = new TrackingServerChannel("blocking-disconnect-observer");
        manager.GetOrRegisterVirtualChannel(channel.Id, _ => channel);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ChannelDisconnected += (_, _) =>
        {
            callbackEntered.TrySetResult();
#pragma warning disable xUnit1031 // Deliberately blocks the synchronous callback to test cleanup ordering.
            releaseCallback.Task.GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        };

        var removal = Task.Run(() => manager.RemoveChannel(channel.Id));
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        channel.DisposeCount.Should().Be(1,
            "resource release must precede a user disconnect notification that can block");
        await Task.Run(manager.StopAcceptingChannelsAndCloseAll)
            .WaitAsync(TimeSpan.FromSeconds(3));

        releaseCallback.TrySetResult();
        (await removal.WaitAsync(TimeSpan.FromSeconds(3))).Should().BeTrue();
    }

    [Fact]
    public void StopAcceptingChannels_MustClosePublishedChannelsAndDisposeRejectedCandidate()
    {
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var channel = new TrackingServerChannel("active");
        manager.GetOrRegisterVirtualChannel(channel.Id, _ => channel);

        manager.StopAcceptingChannelsAndCloseAll();

        manager.ConnectionCount.Should().Be(0);
        channel.DisposeCount.Should().Be(1);

        var lateTransport = new MockServerTransport("late");
        var addLate = () => manager.AddChannel(lateTransport);
        addLate.Should().Throw<InvalidOperationException>()
            .WithMessage("*stopped accepting channels*");
        lateTransport.State.Should().Be(ConnectionState.Disconnected,
            "the unpublished ServerTransportChannel still owns and disposes its transport");
    }

    [Fact]
    public void AddChannel_WhenWrapperEventSubscriptionThrows_MustDisposeTransport()
    {
        using var manager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var transport = new ThrowingSubscriptionTransport("constructor-subscription-failure");

        var add = () => manager.AddChannel(transport);

        add.Should().Throw<InvalidOperationException>()
            .WithMessage("data subscription failed");
        transport.StateChangedUnsubscriptionCount.Should().Be(1);
        transport.DisposeCount.Should().Be(1);
        manager.ConnectionCount.Should().Be(0);
    }

    [Fact]
    public void ExpirationCleanup_WhenLoggerThrows_MustStillDisposeExpiredChannel()
    {
        using var manager = new ServerChannelManager(new ThrowingLogger<ServerChannelManager>())
        {
            ChannelTimeout = TimeSpan.FromTicks(-1)
        };
        var channel = new TrackingServerChannel("expired-with-throwing-logger");
        manager.GetOrRegisterVirtualChannel(channel.Id, _ => channel);
        var cleanup = typeof(ServerChannelManager).GetMethod(
            "CleanupExpiredChannels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        cleanup.Should().NotBeNull();

        var invoke = () => cleanup!.Invoke(manager, [null]);

        invoke.Should().NotThrow();
        channel.DisposeCount.Should().Be(1);
        manager.ConnectionCount.Should().Be(0);
    }

    private sealed class TrackingServerChannel(string id) : IServerChannel
    {
        private EventHandler<TransportStateEventArgs>? _stateChanged;
        private EventHandler<MessageParsedEventArgs>? _messageParsed;
        private int _stateChangedSubscriptionCount;
        private int _stateChangedUnsubscriptionCount;
        private int _messageParsedSubscriptionCount;
        private int _messageParsedUnsubscriptionCount;
        private int _disposeCount;

        public string Id { get; } = id;
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
        public DateTime LastActiveTime { get; } = DateTime.UtcNow;
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 0);
        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1);
        public TransportType Type => TransportType.TCP;
        public bool IsAuthenticated => AuthenticationContext?.IsAuthenticated ?? false;
        public IAuthenticationContext? AuthenticationContext { get; set; }

        public int StateChangedSubscriptionCount => Volatile.Read(ref _stateChangedSubscriptionCount);
        public int StateChangedUnsubscriptionCount => Volatile.Read(ref _stateChangedUnsubscriptionCount);
        public int MessageParsedSubscriptionCount => Volatile.Read(ref _messageParsedSubscriptionCount);
        public int MessageParsedUnsubscriptionCount => Volatile.Read(ref _messageParsedUnsubscriptionCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool ThrowOnEventUnsubscribe { get; init; }

        public event EventHandler<TransportStateEventArgs>? StateChanged
        {
            add
            {
                _stateChanged += value;
                Interlocked.Increment(ref _stateChangedSubscriptionCount);
            }
            remove
            {
                _stateChanged -= value;
                Interlocked.Increment(ref _stateChangedUnsubscriptionCount);
                if (ThrowOnEventUnsubscribe)
                {
                    throw new InvalidOperationException("state event unsubscribe failed");
                }
            }
        }

        public event EventHandler<MessageParsedEventArgs>? MessageParsed
        {
            add
            {
                _messageParsed += value;
                Interlocked.Increment(ref _messageParsedSubscriptionCount);
            }
            remove
            {
                _messageParsed -= value;
                Interlocked.Increment(ref _messageParsedUnsubscriptionCount);
                if (ThrowOnEventUnsubscribe)
                {
                    throw new InvalidOperationException("message event unsubscribe failed");
                }
            }
        }

        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public void SetAuthentication(IAuthenticationContext authContext)
            => AuthenticationContext = authContext;

        public void ClearAuthentication()
            => AuthenticationContext = null;

        public Task<bool> SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public void Dispose()
            => Interlocked.Increment(ref _disposeCount);

        public Action CaptureDisconnectedNotification()
        {
            var handlers = _stateChanged;
            return () => handlers?.Invoke(
                this,
                new TransportStateEventArgs(
                    Id,
                    ConnectionState.Connected,
                    ConnectionState.Disconnected));
        }

        public void RaiseMessageParsed(MessagePacketHolder holder)
            => _messageParsed?.Invoke(
                this,
                new MessageParsedEventArgs(Id, holder, DateTime.UtcNow, processorId: 0));
    }

    private sealed class ThrowingSubscriptionTransport(string id) : IServerTransport
    {
        private EventHandler<TransportStateEventArgs>? _stateChanged;
        private int _stateChangedUnsubscriptionCount;
        private int _disposeCount;

        public string Id { get; } = id;
        public TransportType Type => TransportType.TCP;
        public bool IsConnected => true;
        public ConnectionState State => ConnectionState.Connected;
        public EndPoint LocalEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 0);
        public EndPoint RemoteEndPoint { get; } = new IPEndPoint(IPAddress.Loopback, 1);
        public int StateChangedUnsubscriptionCount => Volatile.Read(ref _stateChangedUnsubscriptionCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public event EventHandler<TransportStateEventArgs>? StateChanged
        {
            add => _stateChanged += value;
            remove
            {
                _stateChanged -= value;
                Interlocked.Increment(ref _stateChangedUnsubscriptionCount);
            }
        }

        public event EventHandler<TransportDataEventArgs>? DataReceived
        {
            add => throw new InvalidOperationException("data subscription failed");
            remove { }
        }

        public Task<bool> SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task CloseAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose()
            => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class ThrowingLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("logger failed");
    }

    private static int GetDisposedState(MessagePacketHolder holder)
    {
        var disposedField = typeof(MessagePacketHolder).GetField(
            "_disposed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        disposedField.Should().NotBeNull();
        return (int)disposedField!.GetValue(holder)!;
    }
}
