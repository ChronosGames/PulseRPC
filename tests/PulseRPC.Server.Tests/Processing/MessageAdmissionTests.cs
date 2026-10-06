using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Messaging;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Transport;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class MessageAdmissionTests
{
    [Fact]
    public async Task ByteBudgets_IncludeRunningWork_AndReturnOnlyAfterCompletion()
    {
        var entered = Signal();
        var release = Signal();
        var shard = new MessageWorkerShard("bytes", 16, async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return ProcessingResult.SuccessResult(null);
        }, _ => { }, NullLogger.Instance, maxPendingBytes: 12, maxPendingBytesPerConnection: 8);
        try
        {
            Assert.True(shard.TryEnqueue(Slot("a", 8)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(shard.TryEnqueue(Slot("a", 1)));
            Assert.True(shard.TryEnqueue(Slot("b", 4)));
            Assert.False(shard.TryEnqueue(Slot("c", 1)));
            Assert.Equal(12, shard.PendingBytes);
            release.TrySetResult();
        }
        finally { release.TrySetResult(); await shard.DisposeAsync(); }
        Assert.Equal(0, shard.PendingBytes);
    }

    [Fact]
    public async Task ConnectionQueueBudget_LeavesCapacityForOtherConnections()
    {
        var entered = Signal();
        var release = Signal();
        await using var shard = new MessageWorkerShard("per-connection", 16, async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return ProcessingResult.SuccessResult(null);
        }, _ => { }, NullLogger.Instance, maxQueuedPerConnection: 1);
        try
        {
            Assert.True(shard.TryEnqueue(Slot("hot", 0)));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(shard.TryEnqueue(Slot("hot", 0)));
            Assert.False(shard.TryEnqueue(Slot("hot", 0)));
            Assert.True(shard.TryEnqueue(Slot("other", 0)));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ExpiredQueuedRequest_ReportsTimeoutWithoutEnteringBusinessCode()
    {
        var rejected = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var shard = new MessageWorkerShard("deadline", 16, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(ProcessingResult.SuccessResult(null));
        }, _ => { }, NullLogger.Instance, rejectionHandler: (_, ex) => rejected.TrySetResult(ex));
        var slot = Slot("client", 1);
        slot.Header.TimeoutMs = 1;
        slot.EnqueueTime = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        Assert.True(shard.TryEnqueue(slot));
        Assert.IsType<TimeoutException>(await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Overload_ReportsBusyOrClosesExactConnection_WhenResponseQueueIsFull(bool responseAccepted)
    {
        var response = Substitute.For<IResponseProcessor>();
        MessageProcessedEventArgs? rejection = null;
        response.TryProcessMessageResult(Arg.Any<MessageProcessedEventArgs>()).Returns(call =>
        {
            rejection = call.Arg<MessageProcessedEventArgs>();
            return responseAccepted;
        });
        var channel = Substitute.For<IServerChannel>();
        channel.ConnectionId.Returns("overload");
        await using var engine = new MessageEngine(Substitute.For<IMessageDispatcher>(),
            Substitute.For<IServiceProvider>(), Options.Create(new PulseServerOptions
            {
                MessageWorkerShardCount = 1,
                MaxPendingMessageBytesPerShard = 1
            }), NullLogger<MessageEngine>.Instance, Substitute.For<IServerChannelManager>(), response);
        await engine.StartAsync();
        engine.RegisterConnection(channel);
        var slot = Slot(channel.ConnectionId, 2);
        Assert.False(engine.TryEnqueueMessage(channel,
            new MessagePacketHolder(slot.Header, slot.Payload.ToArray(), channel.ConnectionId)));
        Assert.NotNull(rejection);
        Assert.IsType<RpcAdmissionException>(rejection.Exception);
        Assert.Equal(slot.MessageId, rejection.CallContext.MessageId);
        Assert.Same(channel, rejection.CallContext.ExpectedChannel);
        Assert.Equal(0, engine.PendingRequestCancellationCount);
        if (responseAccepted) channel.DidNotReceive().Dispose();
        else channel.Received(1).Dispose();
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(5000, 1000)]
    [InlineData(100, 100)]
    public async Task ServerDeadline_CapsMissingOrExcessiveClientTimeouts(int clientTimeout, int expected)
    {
        var observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = Substitute.For<IMessageDispatcher>();
        dispatcher.DispatchAsync(Arg.Any<MessageEnvelope>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed.TrySetResult(call.Arg<MessageEnvelope>().Header.TimeoutMs);
                return ValueTask.FromResult<object?>(null);
            });
        await using var engine = new MessageEngine(dispatcher, Substitute.For<IServiceProvider>(),
            Options.Create(new PulseServerOptions { MessageWorkerShardCount = 1, MaxRequestTimeoutMs = 1000 }),
            NullLogger<MessageEngine>.Instance, Substitute.For<IServerChannelManager>(), Substitute.For<IResponseProcessor>());
        await engine.StartAsync();
        engine.RegisterConnection("deadline");
        var slot = Slot("deadline", 0);
        slot.Header.TimeoutMs = clientTimeout;
        Assert.True(engine.TryEnqueueMessage("deadline", new MessagePacketHolder(slot.Header, [], "deadline")));
        Assert.Equal(expected, await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MessageSlot Slot(string connection, int bytes)
    {
        var id = Guid.NewGuid();
        return new MessageSlot
        {
            MessageId = id, ConnectionId = connection, Payload = new byte[bytes],
            Header = new MessageHeader(MessageType.Request, "Test", "Call") { MessageId = id, ProtocolId = 0x1234 },
            EnqueueTime = Stopwatch.GetTimestamp()
        };
    }
}
