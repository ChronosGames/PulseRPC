using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Diagnostics;
using PulseRPC.Messaging;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Shared;
using Xunit;
using MessageStatus = PulseRPC.Server.Processing.Memory.MessageStatus;

namespace PulseRPC.Server.Tests.Processing;

public sealed class MessageWorkerShardLifecycleTests
{
    private sealed class CountingDisposable : IDisposable
    {
        private int _count;
        public int DisposeCount => Volatile.Read(ref _count);
        public void Dispose() => Interlocked.Increment(ref _count);
    }

    [Fact]
    public async Task AcceptedMessages_MustProcessInOrder_AndFinalizeExactlyOnce()
    {
        const int total = 2000;
        var shardId = $"ordered-{Guid.NewGuid():N}";
        var owners = Enumerable.Range(0, total).Select(_ => new CountingDisposable()).ToArray();
        var processed = new ConcurrentQueue<Guid>();
        var finalized = new ConcurrentDictionary<Guid, int>();
        var slots = owners
            .Select((owner, index) => CreateSlot(owner, BitConverter.GetBytes(index)))
            .ToArray();

        var shard = new MessageWorkerShard(
            shardId,
            total,
            (slot, _) =>
            {
                processed.Enqueue(slot.MessageId);
                return ValueTask.FromResult(ProcessingResult.SuccessResult(null));
            },
            slot => finalized.AddOrUpdate(slot.MessageId, 1, (_, count) => count + 1),
            NullLogger.Instance);

        try
        {
            foreach (var slot in slots)
            {
                shard.TryEnqueue(slot).Should().BeTrue();
            }

            var completed = await WaitUntilAsync(
                () => owners.All(owner => owner.DisposeCount == 1),
                TimeSpan.FromSeconds(10));
            completed.Should().BeTrue();

            processed.Should().Equal(slots.Select(slot => slot.MessageId));
            finalized.Count.Should().Be(total);
            finalized.Values.Should().OnlyContain(count => count == 1);
        }
        finally
        {
            await shard.DisposeAsync();
        }

        RuntimeQueueMetrics.GetSnapshots()
            .Should().NotContain(snapshot => snapshot.InstanceId == shardId);
    }

    [Fact]
    public async Task CapacityOne_MustRejectThirdMessage_AndDisposeMustJoinInFlightAndDropBacklog()
    {
        var shardId = $"bounded-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owners = Enumerable.Range(0, 3).Select(_ => new CountingDisposable()).ToArray();
        var handlerCalls = 0;

        var shard = new MessageWorkerShard(
            shardId,
            capacity: 1,
            async (slot, cancellationToken) =>
            {
                Interlocked.Increment(ref handlerCalls);
                entered.TrySetResult(true);
                await release.Task;
                return ProcessingResult.SuccessResult(null);
            },
            _ => { },
            NullLogger.Instance);
        Task? disposeTask = null;

        try
        {
            shard.TryEnqueue(CreateSlot(owners[0], [0])).Should().BeTrue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            shard.TryEnqueue(CreateSlot(owners[1], [1])).Should().BeTrue();
            shard.TryEnqueue(CreateSlot(owners[2], [2])).Should().BeFalse();
            owners[2].Dispose();

            disposeTask = shard.DisposeAsync().AsTask();
            disposeTask.IsCompleted.Should().BeFalse("关闭必须等待已经进入 handler 的消息");

            release.TrySetResult(true);
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));

            Volatile.Read(ref handlerCalls).Should().Be(1, "关闭后的队列积压不应继续派发");
            owners.Should().OnlyContain(owner => owner.DisposeCount == 1);
        }
        finally
        {
            release.TrySetResult(true);
            if (disposeTask != null)
            {
                await disposeTask;
            }
            else
            {
                await shard.DisposeAsync();
            }
        }

        RuntimeQueueMetrics.GetSnapshots()
            .Should().NotContain(snapshot => snapshot.InstanceId == shardId);
    }

    [Fact]
    public async Task DisposeAsync_MustContinueCleanupWhenCancellationCallbackThrows()
    {
        var shardId = $"throwing-callback-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new CountingDisposable();

        var shard = new MessageWorkerShard(
            shardId,
            capacity: 1,
            async (slot, cancellationToken) =>
            {
                using var registration = cancellationToken.Register(() =>
                {
                    cancellationObserved.TrySetResult(true);
                    throw new InvalidOperationException("test cancellation callback failure");
                });
                entered.TrySetResult(true);
                await release.Task;
                return ProcessingResult.SuccessResult(null);
            },
            _ => { },
            NullLogger.Instance);
        Task? disposeTask = null;

        try
        {
            shard.TryEnqueue(CreateSlot(owner, [0])).Should().BeTrue();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            disposeTask = shard.DisposeAsync().AsTask();
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            disposeTask.IsCompleted.Should().BeFalse();

            release.TrySetResult(true);
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));

            owner.DisposeCount.Should().Be(1);
        }
        finally
        {
            release.TrySetResult(true);
            if (disposeTask != null)
            {
                await disposeTask;
            }
            else
            {
                await shard.DisposeAsync();
            }
        }

        RuntimeQueueMetrics.GetSnapshots()
            .Should().NotContain(snapshot => snapshot.InstanceId == shardId);
    }

    [Fact]
    public async Task ConnectionLease_MustNotDisposeTokenSourceUntilCancellationCompletes()
    {
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        var lease = new MessageConnectionLease(
            "connection-lease",
            shardIndex: 0,
            CancellationToken.None,
            NullLogger.Instance);

        lease.TryAcquire().Should().BeTrue();
        using var registration = lease.CancellationToken.Register(() =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Wait();
            throw new InvalidOperationException("test callback failure");
        });

        var deactivate = lease.DeactivateAsync();
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        lease.Release();
        lease.PendingMessageCount.Should().Be(0);
        lease.IsDisposed.Should().BeFalse(
            "CancelAsync is still executing the registered callback");

        releaseCallback.Set();
        await deactivate.WaitAsync(TimeSpan.FromSeconds(3));

        lease.IsActive.Should().BeFalse();
        lease.IsDisposed.Should().BeTrue();
        lease.DeactivateAsync().Should().BeSameAs(deactivate);
    }

    [Fact]
    public async Task IdleWorker_MustNotRetainLastCompletedMessageOwner()
    {
        var finalized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var shard = new MessageWorkerShard(
            $"retention-{Guid.NewGuid():N}",
            capacity: 1,
            (_, _) => ValueTask.FromResult(ProcessingResult.SuccessResult(null)),
            _ => finalized.TrySetResult(),
            NullLogger.Instance);

        var ownerReference = EnqueueRetentionProbe(shard);
        await finalized.Task.WaitAsync(TimeSpan.FromSeconds(3));

        ForceFullCollection();

        ownerReference.IsAlive.Should().BeFalse(
            "the shard's lifetime-long async state machine must clear its completed slot local");
    }

    private static MessageSlot CreateSlot(CountingDisposable owner, byte[] payload)
    {
        var messageId = Guid.NewGuid();
        return new MessageSlot
        {
            MessageId = messageId,
            ConnectionId = "test",
            Header = new MessageHeader(MessageType.Request, "svc", "method") { MessageId = messageId },
            Payload = payload,
            PayloadOwner = owner,
            Priority = MessagePriority.Normal,
            EnqueueTime = Stopwatch.GetTimestamp(),
            Status = MessageStatus.Pending
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference EnqueueRetentionProbe(MessageWorkerShard shard)
    {
        var owner = new CountingDisposable();
        var ownerReference = new WeakReference(owner);
        shard.TryEnqueue(CreateSlot(owner, [0])).Should().BeTrue();
        return ownerReference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullCollection()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return predicate();
    }
}
