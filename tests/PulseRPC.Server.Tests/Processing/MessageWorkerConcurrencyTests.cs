using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Shared;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class MessageWorkerConcurrencyTests
{
    [Fact]
    public async Task SlowConnection_DoesNotBlockAnotherConnection_AndRetainsItsOwnOrder()
    {
        var slowEntered = Signal();
        var releaseSlow = Signal();
        var nextOnSlow = Signal();
        var fastCompleted = Signal();
        var slowCalls = 0;
        await using var shard = new MessageWorkerShard("fair", 16, async (slot, _) =>
        {
            if (slot.ConnectionId == "slow")
            {
                if (Interlocked.Increment(ref slowCalls) == 1)
                {
                    slowEntered.TrySetResult();
                    await releaseSlow.Task;
                }
                else nextOnSlow.TrySetResult();
            }
            else fastCompleted.TrySetResult();
            return ProcessingResult.SuccessResult(null);
        }, _ => { }, NullLogger.Instance, maxConcurrency: 2);
        try
        {
            Assert.True(shard.TryEnqueue(Slot("slow")));
            await slowEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(shard.TryEnqueue(Slot("slow")));
            Assert.True(shard.TryEnqueue(Slot("fast")));
            await fastCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(nextOnSlow.Task.IsCompleted);
            releaseSlow.TrySetResult();
            await nextOnSlow.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { releaseSlow.TrySetResult(); }
    }

    [Fact]
    public async Task MultiplexedConnection_CanRunIndependentRequestsConcurrently()
    {
        var entered = Signal();
        var release = Signal();
        var fast = Signal();
        var calls = 0;
        await using var shard = new MessageWorkerShard("multiplexed", 8, async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            else fast.TrySetResult();
            return ProcessingResult.SuccessResult(null);
        }, _ => { }, NullLogger.Instance, maxConcurrency: 2, maxConcurrencyPerConnection: 2);
        try
        {
            Assert.True(shard.TryEnqueue(Slot("node-a")));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(shard.TryEnqueue(Slot("node-a")));
            await fast.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ConcurrencyAndQueueAreBounded_DisposalKeepsInFlightPayloadsUntilCompletion()
    {
        var entered = Signal();
        var release = Signal();
        var backlogDropped = Signal();
        var calls = 0;
        var finalized = 0;
        var owners = Enumerable.Range(0, 5).Select(_ => new Owner()).ToArray();
        var shard = new MessageWorkerShard("bounded-parallel", 2, async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult();
            await release.Task; // Deliberately ignores cancellation.
            return ProcessingResult.SuccessResult(null);
        }, _ => { if (Interlocked.Increment(ref finalized) == 2) backlogDropped.TrySetResult(); },
            NullLogger.Instance, maxConcurrency: 2, maxConcurrencyPerConnection: 2);
        Task? disposal = null;
        try
        {
            Assert.True(shard.TryEnqueue(Slot("node", owners[0])));
            Assert.True(shard.TryEnqueue(Slot("node", owners[1])));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(shard.TryEnqueue(Slot("node", owners[2])));
            Assert.True(shard.TryEnqueue(Slot("node", owners[3])));
            Assert.False(shard.TryEnqueue(Slot("node", owners[4])));
            owners[4].Dispose();
            disposal = shard.DisposeAsync().AsTask();
            await backlogDropped.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, owners[0].Disposed);
            Assert.Equal(0, owners[1].Disposed);
            Assert.Equal(2, calls);
            release.TrySetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(owners, owner => Assert.Equal(1, owner.Disposed));
        }
        finally
        {
            release.TrySetResult();
            await (disposal ?? shard.DisposeAsync().AsTask());
        }
    }

    [Fact]
    public async Task ConcurrentDispatches_IsolateAsyncLocalContext()
    {
        var ambient = new AsyncLocal<string?> { Value = "seed" };
        var firstEntered = Signal();
        var secondEntered = Signal();
        var finished = Signal();
        var count = 0;
        var observed = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        await using var shard = new MessageWorkerShard("contexts", 8, async (slot, _) =>
        {
            observed[slot.ConnectionId + ":initial"] = ambient.Value!;
            ambient.Value = slot.ConnectionId;
            if (slot.ConnectionId == "a")
            {
                firstEntered.TrySetResult();
                await secondEntered.Task;
            }
            else
            {
                await firstEntered.Task;
                secondEntered.TrySetResult();
            }
            observed[slot.ConnectionId + ":final"] = ambient.Value!;
            return ProcessingResult.SuccessResult(null);
        }, _ => { if (Interlocked.Increment(ref count) == 2) finished.TrySetResult(); },
            NullLogger.Instance, maxConcurrency: 2);
        Assert.True(shard.TryEnqueue(Slot("a")));
        Assert.True(shard.TryEnqueue(Slot("b")));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("seed", observed["a:initial"]);
        Assert.Equal("seed", observed["b:initial"]);
        Assert.Equal("a", observed["a:final"]);
        Assert.Equal("b", observed["b:final"]);
        Assert.Equal("seed", ambient.Value);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MessageSlot Slot(string connection, Owner? owner = null)
    {
        var id = Guid.NewGuid();
        return new MessageSlot
        {
            ConnectionId = connection, MessageId = id,
            Header = new MessageHeader(MessageType.Request, "Test", "Call") { MessageId = id },
            PayloadOwner = owner, EnqueueTime = Stopwatch.GetTimestamp()
        };
    }
    private sealed class Owner : IDisposable
    {
        private int _disposed;
        public int Disposed => Volatile.Read(ref _disposed);
        public void Dispose() => Interlocked.Increment(ref _disposed);
    }
}
