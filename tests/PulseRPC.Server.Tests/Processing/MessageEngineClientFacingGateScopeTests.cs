using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

[Collection(ClientFacingGateTestCollection.Name)]
public sealed class MessageEngineClientFacingGateScopeTests
{
    [Fact]
    public async Task ParallelNamedDispatch_MustFlowHostPolicyIntoNestedRootProviderRoutes()
    {
        var previous = ClientFacingGate.EnforcementEnabled;
        ClientFacingGate.EnforcementEnabled = false;

        try
        {
            var rootProvider = EmptyServiceProvider.Instance;
            var parallelGate = new ParallelDispatchGate(participantCount: 2);
            var enabledDispatcher = new NestedRootGateDispatcher(rootProvider, parallelGate);
            var disabledDispatcher = new NestedRootGateDispatcher(rootProvider, parallelGate);

            var (enabledEngine, enabledChannel) = CreateEngine(
                "enabled-connection",
                enabledDispatcher,
                new ClientFacingGateServiceProvider(
                    rootProvider,
                    new ClientFacingGatePolicy(enforcementEnabled: true)));
            var (disabledEngine, disabledChannel) = CreateEngine(
                "disabled-connection",
                disabledDispatcher,
                new ClientFacingGateServiceProvider(
                    rootProvider,
                    new ClientFacingGatePolicy(enforcementEnabled: false)));

            await using (enabledEngine)
            await using (disabledEngine)
            using (enabledChannel)
            using (disabledChannel)
            {
                await enabledEngine.StartAsync();
                await disabledEngine.StartAsync();
                enabledEngine.RegisterConnection("enabled-connection");
                disabledEngine.RegisterConnection("disabled-connection");

                enabledEngine.TryEnqueueMessage(
                        "enabled-connection",
                        CreatePacket("enabled-connection"))
                    .Should().BeTrue();
                disabledEngine.TryEnqueueMessage(
                        "disabled-connection",
                        CreatePacket("disabled-connection"))
                    .Should().BeTrue();

                var enabledDenied = await enabledDispatcher.Denied.Task
                    .WaitAsync(TimeSpan.FromSeconds(5));
                var disabledDenied = await disabledDispatcher.Denied.Task
                    .WaitAsync(TimeSpan.FromSeconds(5));

                enabledDenied.Should().BeTrue();
                disabledDenied.Should().BeFalse();
            }
        }
        finally
        {
            ClientFacingGate.EnforcementEnabled = previous;
        }
    }

    private static (MessageEngine Engine, ServerTransportChannel Channel) CreateEngine(
        string connectionId,
        IMessageDispatcher dispatcher,
        IServiceProvider serviceProvider)
    {
        var channelManager = Substitute.For<IServerChannelManager>();
        var transport = Substitute.For<IServerTransport>();
        transport.Id.Returns(connectionId);
        var channel = new ServerTransportChannel(
            transport,
            NullLogger<ServerTransportChannel>.Instance);
        channelManager.GetChannel(connectionId).Returns(channel);

        var engine = new MessageEngine(
            dispatcher,
            serviceProvider,
            Options.Create(new MessageEngineConfiguration()),
            NullLogger<MessageEngine>.Instance,
            channelManager,
            new NoopResponseProcessor());
        return (engine, channel);
    }

    private static MessagePacketHolder CreatePacket(string connectionId)
    {
        var header = new MessageHeader
        {
            Type = MessageType.OneWay,
            MessageId = Guid.NewGuid(),
            ProtocolId = 0x1234,
            ServiceName = "TestHub",
            MethodName = "InvokeAsync",
            Timestamp = DateTimeOffset.UtcNow.Ticks,
        };
        return new MessagePacketHolder(header, Array.Empty<byte>(), connectionId);
    }

    private sealed class NestedRootGateDispatcher : IMessageDispatcher
    {
        private readonly IServiceProvider _rootProvider;
        private readonly ParallelDispatchGate _parallelGate;

        public NestedRootGateDispatcher(
            IServiceProvider rootProvider,
            ParallelDispatchGate parallelGate)
        {
            _rootProvider = rootProvider;
            _parallelGate = parallelGate;
        }

        public TaskCompletionSource<bool> Denied { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            await _parallelGate.SignalAndWaitAsync(cancellationToken);

            try
            {
                ClientFacingGate.Enforce(
                    _rootProvider,
                    isClientFacing: false,
                    protocolId: 0x1234,
                    methodDisplayName: "ITestHub.InvokeAsync");
                Denied.TrySetResult(false);
            }
            catch (ClientFacingAccessDeniedException)
            {
                Denied.TrySetResult(true);
            }

            return null;
        }

        public void Dispose() { }
    }

    private sealed class ParallelDispatchGate
    {
        private readonly int _participantCount;
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public ParallelDispatchGate(int participantCount)
        {
            _participantCount = participantCount;
        }

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == _participantCount)
            {
                _release.TrySetResult(true);
            }

            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class NoopResponseProcessor : IResponseProcessor
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs)
            => ValueTask.CompletedTask;

        public void Dispose() { }
    }
}
