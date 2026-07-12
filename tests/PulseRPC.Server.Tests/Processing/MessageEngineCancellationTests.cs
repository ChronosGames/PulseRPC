using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Messaging;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class MessageEngineCancellationTests
{
    [Fact]
    public async Task UnauthenticatedNetworkMessage_MustUseMinimalExternalContext()
    {
        const string connectionId = "anonymous-conn-1";
        var dispatcher = new ContextCapturingDispatcher();
        var channelManager = Substitute.For<IServerChannelManager>();
        var transport = Substitute.For<IServerTransport>();
        transport.Id.Returns(connectionId);
        using var channel = new ServerTransportChannel(
            transport,
            NullLogger<ServerTransportChannel>.Instance);
        channelManager.GetChannel(connectionId).Returns(channel);

        await using var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            Options.Create(new PulseServerOptions
            {
                MessageWorkerShardCount = 1,
                MessageQueueCapacityPerShard = 16
            }),
            NullLogger<MessageEngine>.Instance,
            channelManager,
            new NoopResponseProcessor());

        await engine.StartAsync();
        engine.RegisterConnection(connectionId);

        Assert.True(engine.TryEnqueueMessage(
            connectionId,
            CreatePacket(MessageType.OneWay, Guid.NewGuid(), protocolId: 0x1234)));

        var captured = await dispatcher.ContextReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(CallSourceType.ExternalUser, captured.SourceType);
        Assert.False(captured.HasWildcardPermission);
        Assert.Null(captured.UserId);
    }

    [Fact]
    public async Task CancelFrame_MustCancelInFlightRequestToken()
    {
        var dispatcher = new CapturingDispatcher();
        var channelManager = Substitute.For<IServerChannelManager>();
        var responseProcessor = new NoopResponseProcessor();
        await using var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            Options.Create(new PulseServerOptions
            {
                MessageWorkerShardCount = 1,
                MessageQueueCapacityPerShard = 16
            }),
            NullLogger<MessageEngine>.Instance,
            channelManager,
            responseProcessor);

        const string connectionId = "conn-1";
        var messageId = Guid.NewGuid();

        await engine.StartAsync();
        engine.RegisterConnection(connectionId);

        Assert.True(engine.TryEnqueueMessage(
            connectionId,
            CreatePacket(MessageType.Request, messageId, protocolId: 0x1234)));

        var token = await dispatcher.TokenReceived.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(token.IsCancellationRequested);

        Assert.True(engine.TryEnqueueMessage(
            connectionId,
            CreatePacket(MessageType.Cancel, messageId)));

        await WaitUntilCanceledAsync(token);
    }

    private static MessagePacketHolder CreatePacket(MessageType type, Guid messageId, ushort protocolId = 0)
    {
        var header = new MessageHeader
        {
            Type = type,
            MessageId = messageId,
            ProtocolId = protocolId,
            ServiceName = string.Empty,
            MethodName = string.Empty,
            Flags = type == MessageType.Request ? MessageFlags.RequireResponse : MessageFlags.None,
            Timestamp = DateTimeOffset.UtcNow.Ticks
        };

        return new MessagePacketHolder(header, Array.Empty<byte>(), "conn-1");
    }

    private static async Task WaitUntilCanceledAsync(CancellationToken token)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(10, timeoutCts.Token);
        }
    }

    private sealed class CapturingDispatcher : IMessageDispatcher
    {
        public TaskCompletionSource<CancellationToken> TokenReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            TokenReceived.TrySetResult(cancellationToken);
            return new ValueTask<object?>(WaitForCancellationAsync(cancellationToken));
        }

        public void Dispose()
        {
        }

        private static async Task<object?> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class ContextCapturingDispatcher : IMessageDispatcher
    {
        public TaskCompletionSource<(CallSourceType SourceType, bool HasWildcardPermission, string? UserId)> ContextReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            var context = PulseContext.Current;
            ContextReceived.TrySetResult((
                context?.SourceType ?? CallSourceType.InternalService,
                context?.Permissions.Contains("*") == true,
                context?.UserId));
            return new ValueTask<object?>((object?)null);
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoopResponseProcessor : IResponseProcessor
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
