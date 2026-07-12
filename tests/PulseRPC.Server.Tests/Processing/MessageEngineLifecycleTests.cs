using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Diagnostics;
using PulseRPC.Messaging;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Transport;
using Xunit;

namespace PulseRPC.Server.Tests.Processing;

public sealed class MessageEngineLifecycleTests
{
    [Fact]
    public async Task Connections_MustUseRoundRobinShards_AndBoundDispatchConcurrency()
    {
        const int workerCount = 3;
        const int connectionCount = 12;
        var dispatcher = new ConcurrencyTrackingDispatcher(workerCount);
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerCount, queueCapacityPerShard: 16),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());
        var connectionIds = Enumerable.Range(0, connectionCount)
            .Select(index => $"round-robin-{index}-{Guid.NewGuid():N}")
            .ToArray();

        try
        {
            await engine.StartAsync();

            for (var i = 0; i < connectionIds.Length; i++)
            {
                engine.RegisterConnection(connectionIds[i]);
                engine.TryGetWorkerShardIndex(connectionIds[i], out var shardIndex).Should().BeTrue();
                shardIndex.Should().Be(i % workerCount);
                engine.TryEnqueueMessage(
                        connectionIds[i],
                        CreatePacket(connectionIds[i]),
                        MessagePriority.Normal)
                    .Should().BeTrue();
            }

            await dispatcher.ReachedExpectedConcurrency.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(100);

            dispatcher.MaxConcurrency.Should().Be(workerCount);
            dispatcher.CurrentConcurrency.Should().Be(workerCount);

            dispatcher.Release.TrySetResult(true);
            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task HighFrequencyRegisterUnregister_MustKeepFixedShardResources()
    {
        const int iterations = 10_000;
        var connectionId = $"connection-churn-{Guid.NewGuid():N}";
        var engine = new MessageEngine(
            new BlockingDispatcher(),
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            var shardIds = engine.WorkerShardIds.ToArray();
            CountRegisteredShards(shardIds).Should().Be(shardIds.Length);

            for (var i = 0; i < iterations; i++)
            {
                engine.RegisterConnection(connectionId);
                engine.UnregisterConnection(connectionId);
            }

            CountRegisteredShards(shardIds).Should().Be(
                shardIds.Length,
                "连接抖动不能创建额外 worker 或队列");

            (await WaitUntilAsync(
                () => engine.TrackedConnectionDeactivationCount == 0,
                TimeSpan.FromSeconds(5))).Should().BeTrue(
                "连接停用任务不能随高频连接/断开持续累积");

            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

            engine.GetStatistics().ActiveConnections.Should().Be(0);
            engine.TrackedConnectionDeactivationCount.Should().Be(0);
            engine.PendingRequestCancellationCount.Should().Be(0);
            CountRegisteredShards(shardIds).Should().Be(0);
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentRegisterSameConnection_MustCreateSingleConnectionState()
    {
        var connectionId = $"register-race-{Guid.NewGuid():N}";
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new MessageEngine(
            new BlockingDispatcher(),
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            var shardIds = engine.WorkerShardIds.ToArray();

            var registrations = Enumerable.Range(0, 32)
                .Select(_ => Task.Run(async () =>
                {
                    await start.Task;
                    engine.RegisterConnection(connectionId);
                }))
                .ToArray();

            start.TrySetResult(true);
            await Task.WhenAll(registrations);

            engine.GetStatistics().ActiveConnections.Should().Be(1);
            CountRegisteredShards(shardIds).Should().Be(shardIds.Length);

            engine.UnregisterConnection(connectionId);
            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));

            CountRegisteredShards(shardIds).Should().Be(0);
        }
        finally
        {
            start.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task StopAsync_MustDisposeShardsWithActiveConnection()
    {
        var connectionId = $"active-stop-{Guid.NewGuid():N}";
        var dispatcher = new BlockingDispatcher();
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            var shardIds = engine.WorkerShardIds.ToArray();
            engine.RegisterConnection(connectionId);
            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId),
                    MessagePriority.Normal)
                .Should().BeTrue();

            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var stopTask = engine.StopAsync();
            await dispatcher.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stopTask.IsCompleted.Should().BeFalse(
                "停机必须等待 shard 中已经进入 handler 的消息退出");

            dispatcher.Release.TrySetResult(true);
            await stopTask.WaitAsync(TimeSpan.FromSeconds(3));

            engine.GetStatistics().ActiveConnections.Should().Be(0);
            CountRegisteredShards(shardIds).Should().Be(0);
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnregisterConnection_MustReleaseQueuedRequestCancellationState()
    {
        const int total = 256;
        var connectionId = $"request-cleanup-{Guid.NewGuid():N}";
        var dispatcher = new BlockingDispatcher();
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());
        var messageIds = Enumerable.Range(0, total).Select(_ => Guid.NewGuid()).ToArray();

        try
        {
            await engine.StartAsync();
            engine.RegisterConnection(connectionId);

            foreach (var messageId in messageIds)
            {
                engine.TryEnqueueMessage(
                        connectionId,
                        CreatePacket(connectionId, MessageType.Request, messageId),
                        MessagePriority.Normal)
                    .Should().BeTrue();
            }

            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            engine.UnregisterConnection(connectionId);
            await dispatcher.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
            dispatcher.Release.TrySetResult(true);

            engine.RegisterConnection(connectionId);
            var accepted = await WaitUntilAsync(
                () => engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId, MessageType.Request, messageIds[^1]),
                    MessagePriority.Normal),
                TimeSpan.FromSeconds(3));
            accepted.Should().BeTrue("旧 generation 的积压消息必须终结并移除请求取消状态");

            var newGenerationDispatched = await WaitUntilAsync(
                () => dispatcher.DispatchedMessageIds.Count(id => id == messageIds[^1]) == 1,
                TimeSpan.FromSeconds(3));
            newGenerationDispatched.Should().BeTrue();
            dispatcher.DispatchedMessageIds.Should().HaveCount(2,
                "旧 generation 的其余积压消息不能在重连后派发");

            engine.UnregisterConnection(connectionId);
            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnregisterConnection_MustCancelGeneration_AndStopMustJoinShard()
    {
        var connectionId = $"lifecycle-{Guid.NewGuid():N}";
        var dispatcher = new BlockingDispatcher();
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            engine.RegisterConnection(connectionId);
            var shardIds = engine.WorkerShardIds.ToArray();

            CountRegisteredShards(shardIds).Should().Be(shardIds.Length);

            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId),
                    MessagePriority.Normal)
                .Should().BeTrue();

            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            engine.UnregisterConnection(connectionId);

            await dispatcher.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));

            var stopTask = engine.StopAsync();
            stopTask.IsCompleted.Should().BeFalse(
                "停机必须等待断连 generation 中的在途 handler 完成");

            dispatcher.Release.TrySetResult(true);
            await stopTask.WaitAsync(TimeSpan.FromSeconds(3));

            engine.GetStatistics().ActiveConnections.Should().Be(0);
            CountRegisteredShards(shardIds).Should().Be(0);
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task QueueFull_MustReleaseRejectedRequestStateAndLease()
    {
        var connectionId = $"queue-full-{Guid.NewGuid():N}";
        var dispatcher = new BlockingDispatcher();
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 1),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            engine.RegisterConnection(connectionId);

            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId, MessageType.Request),
                    MessagePriority.Normal)
                .Should().BeTrue();
            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId, MessageType.Request),
                    MessagePriority.Normal)
                .Should().BeTrue();
            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId, MessageType.Request),
                    MessagePriority.Normal)
                .Should().BeFalse("the one-slot shard queue is full");

            engine.PendingRequestCancellationCount.Should().Be(2,
                "the rejected request must remove its cancellation state immediately");

            dispatcher.Release.TrySetResult(true);
            (await WaitUntilAsync(
                () => engine.PendingRequestCancellationCount == 0,
                TimeSpan.FromSeconds(3))).Should().BeTrue();

            engine.UnregisterConnection(connectionId);
            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            engine.TrackedConnectionDeactivationCount.Should().Be(0);
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stop_MustAbortResponseEnqueueBeforeWaitingForShard()
    {
        var connectionId = $"response-stop-{Guid.NewGuid():N}";
        var responseProcessor = new StopReleasesResponseProcessor();
        var engine = new MessageEngine(
            new ImmediateDispatcher(),
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 1),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            responseProcessor);

        try
        {
            await engine.StartAsync();
            engine.RegisterConnection(connectionId);
            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId, MessageType.Request),
                    MessagePriority.Normal)
                .Should().BeTrue();

            await responseProcessor.EnqueueEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));

            responseProcessor.StopCount.Should().Be(1);
            engine.PendingRequestCancellationCount.Should().Be(0);
        }
        finally
        {
            responseProcessor.Release.TrySetResult(true);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task OldGenerationResponse_MustNotBePublishedToSameIdReconnect()
    {
        var connectionId = $"response-aba-{Guid.NewGuid():N}";
        var dispatcher = new BlockingDispatcher();
        var responseProcessor = new RecordingResponseProcessor();
        using var channelManager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 4),
            NullLogger<MessageEngine>.Instance,
            channelManager,
            responseProcessor);

        try
        {
            await engine.StartAsync();
            var oldChannel = channelManager.AddChannel(new MockServerTransport(connectionId));
            engine.TryEnqueueMessage(
                    oldChannel,
                    CreatePacket(connectionId, MessageType.Request),
                    MessagePriority.Normal)
                .Should().BeTrue();
            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            channelManager.RemoveChannel(connectionId).Should().BeTrue();
            var newChannel = channelManager.AddChannel(new MockServerTransport(connectionId));
            newChannel.Should().NotBeSameAs(oldChannel);

            dispatcher.Release.TrySetResult(true);
            (await WaitUntilAsync(
                () => engine.PendingRequestCancellationCount == 0,
                TimeSpan.FromSeconds(3))).Should().BeTrue();

            responseProcessor.ProcessCount.Should().Be(0,
                "an old generation must not enqueue a response addressed only by reused connection id");
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            channelManager.RemoveChannel(connectionId);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task LateCancelFromOldGeneration_MustNotCancelReconnectRequest()
    {
        var connectionId = $"cancel-aba-{Guid.NewGuid():N}";
        var messageId = Guid.NewGuid();
        var dispatcher = new BlockingDispatcher();
        using var channelManager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 4),
            NullLogger<MessageEngine>.Instance,
            channelManager,
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            var oldChannel = channelManager.AddChannel(new MockServerTransport(connectionId));
            channelManager.RemoveChannel(connectionId).Should().BeTrue();
            var newChannel = channelManager.AddChannel(new MockServerTransport(connectionId));

            engine.TryEnqueueMessage(
                    newChannel,
                    CreatePacket(connectionId, MessageType.Request, messageId),
                    MessagePriority.Normal)
                .Should().BeTrue();
            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            engine.TryEnqueueMessage(
                    oldChannel,
                    CreatePacket(connectionId, MessageType.Cancel, messageId),
                    MessagePriority.Normal)
                .Should().BeFalse();
            await Task.Delay(50);
            dispatcher.CancellationObserved.Task.IsCompleted.Should().BeFalse();

            engine.TryEnqueueMessage(
                    newChannel,
                    CreatePacket(connectionId, MessageType.Cancel, messageId),
                    MessagePriority.Normal)
                .Should().BeTrue();
            await dispatcher.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            channelManager.RemoveChannel(connectionId);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task IdOnlyApi_MustNotMutateOrEnqueueIntoChannelBoundGeneration()
    {
        var connectionId = $"id-only-guard-{Guid.NewGuid():N}";
        using var channelManager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var engine = new MessageEngine(
            new ImmediateDispatcher(),
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 4),
            NullLogger<MessageEngine>.Instance,
            channelManager,
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            var channel = channelManager.AddChannel(new MockServerTransport(connectionId));

            engine.UnregisterConnection(connectionId);
            engine.TryGetWorkerShardIndex(connectionId, out _).Should().BeTrue(
                "an ID-only stale caller must not unregister a physical connection generation");
            engine.TryEnqueueMessage(
                    connectionId,
                    CreatePacket(connectionId),
                    MessagePriority.Normal)
                .Should().BeFalse(
                    "physical traffic must prove the exact source channel generation");
            engine.TryEnqueueMessage(
                    channel,
                    CreatePacket(connectionId),
                    MessagePriority.Normal)
                .Should().BeTrue();
        }
        finally
        {
            channelManager.RemoveChannel(connectionId);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task SuccessfulChannelEventEnqueue_WhenTraceLoggerThrows_MustNotReturnHolderUpstream()
    {
        var connectionId = $"throwing-trace-{Guid.NewGuid():N}";
        var dispatcher = new BlockingDispatcher();
        using var channelManager = new ServerChannelManager(
            NullLogger<ServerChannelManager>.Instance);
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 4),
            new ThrowingTraceLogger(),
            channelManager,
            new NoopResponseProcessor());

        try
        {
            await engine.StartAsync();
            var channel = channelManager.AddChannel(new MockServerTransport(connectionId));
            var holder = CreatePacket(connectionId);
            var eventArgs = new MessageParsedEventArgs(
                connectionId,
                holder,
                DateTime.UtcNow,
                processorId: 0);
            var callback = typeof(MessageEngine).GetMethod(
                "OnChannelMessageParsed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            callback.Should().NotBeNull();

            var invoke = () => callback!.Invoke(engine, [channel, eventArgs]);
            invoke.Should().NotThrow(
                "after enqueue, logging failure must not make ServerChannelManager reclaim the holder");
            await dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            GetHolderDisposedState(holder).Should().Be(0);

            dispatcher.Release.TrySetResult(true);
            (await WaitUntilAsync(
                () => GetHolderDisposedState(holder) == 1,
                TimeSpan.FromSeconds(3))).Should().BeTrue();
        }
        finally
        {
            dispatcher.Release.TrySetResult(true);
            channelManager.RemoveChannel(connectionId);
            await engine.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConstructorStartedShards_WhenLoggerThrows_MustStillDisposeWorkersAndMetrics()
    {
        var registrationsBefore = RuntimeQueueMetrics.GetSnapshots()
            .Count(snapshot => snapshot.QueueName == "message-engine.shard");
        var engine = new MessageEngine(
            new ImmediateDispatcher(),
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 2, queueCapacityPerShard: 4),
            new ThrowingAllLogger(),
            Substitute.For<IServerChannelManager>(),
            new NoopResponseProcessor());

        await engine.DisposeAsync();

        RuntimeQueueMetrics.GetSnapshots()
            .Count(snapshot => snapshot.QueueName == "message-engine.shard")
            .Should().Be(registrationsBefore);
    }

    [Fact]
    public async Task StartFailure_MustRollbackStartedDownstreamComponentsOnce()
    {
        var dispatcher = new TrackingLifecycleDispatcher();
        var responseProcessor = new FailingStartResponseProcessor();
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 1),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            responseProcessor);

        try
        {
            var start = () => engine.StartAsync();
            await start.Should().ThrowAsync<InvalidOperationException>();

            dispatcher.StartCount.Should().Be(1);
            dispatcher.StopCount.Should().Be(1);
            responseProcessor.StartCount.Should().Be(1);
            responseProcessor.StopCount.Should().Be(1);
        }
        finally
        {
            await engine.DisposeAsync();
        }

        dispatcher.StopCount.Should().Be(1);
        responseProcessor.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task ResponseRollbackFailure_MustNotSkipDispatcherRollback()
    {
        var dispatcher = new TrackingLifecycleDispatcher();
        var responseProcessor = new FailingStartAndStopResponseProcessor();
        var engine = new MessageEngine(
            dispatcher,
            Substitute.For<IServiceProvider>(),
            CreateEngineOptions(workerShardCount: 1, queueCapacityPerShard: 1),
            NullLogger<MessageEngine>.Instance,
            Substitute.For<IServerChannelManager>(),
            responseProcessor);

        try
        {
            var start = () => engine.StartAsync();
            await start.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("response start failed");

            dispatcher.StopCount.Should().Be(1);
            responseProcessor.StopCount.Should().Be(1);
        }
        finally
        {
            await engine.DisposeAsync();
        }
    }

    private static MessagePacketHolder CreatePacket(
        string connectionId,
        MessageType messageType = MessageType.OneWay,
        Guid? messageId = null)
    {
        var header = new MessageHeader
        {
            Type = messageType,
            MessageId = messageId ?? Guid.NewGuid(),
            ProtocolId = 0x1234,
            ServiceName = string.Empty,
            MethodName = string.Empty,
            Timestamp = DateTimeOffset.UtcNow.Ticks
        };

        return new MessagePacketHolder(header, Array.Empty<byte>(), connectionId);
    }

    private static IOptions<PulseServerOptions> CreateEngineOptions(
        int workerShardCount = 2,
        int queueCapacityPerShard = 512)
    {
        return Options.Create(new PulseServerOptions
        {
            MessageWorkerShardCount = workerShardCount,
            MessageQueueCapacityPerShard = queueCapacityPerShard
        });
    }

    private static int CountRegisteredShards(IReadOnlyCollection<string> shardIds)
    {
        return RuntimeQueueMetrics.GetSnapshots()
            .Count(snapshot => shardIds.Contains(snapshot.InstanceId));
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

    private static int GetHolderDisposedState(MessagePacketHolder holder)
    {
        var field = typeof(MessagePacketHolder).GetField(
            "_disposed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (int)field!.GetValue(holder)!;
    }

    private sealed class BlockingDispatcher : IMessageDispatcher
    {
        public ConcurrentQueue<Guid> DispatchedMessageIds { get; } = new();

        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            DispatchedMessageIds.Enqueue(message.MessageId);
            Entered.TrySetResult(true);
            cancellationToken.Register(() => CancellationObserved.TrySetResult(true));
            return new ValueTask<object?>(WaitForReleaseAsync());
        }

        public void Dispose()
        {
        }

        private async Task<object?> WaitForReleaseAsync()
        {
            await Release.Task;
            return null;
        }
    }

    private sealed class ConcurrencyTrackingDispatcher : IMessageDispatcher
    {
        private readonly int _expectedConcurrency;
        private int _currentConcurrency;
        private int _maxConcurrency;

        public ConcurrencyTrackingDispatcher(int expectedConcurrency)
        {
            _expectedConcurrency = expectedConcurrency;
        }

        public int CurrentConcurrency => Volatile.Read(ref _currentConcurrency);
        public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

        public TaskCompletionSource<bool> ReachedExpectedConcurrency { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            UpdateMaxConcurrency(current);
            if (current >= _expectedConcurrency)
            {
                ReachedExpectedConcurrency.TrySetResult(true);
            }

            try
            {
                await Release.Task;
                return null;
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }

        public void Dispose()
        {
        }

        private void UpdateMaxConcurrency(int current)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrency);
                if (current <= observed ||
                    Interlocked.CompareExchange(ref _maxConcurrency, current, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class ImmediateDispatcher : IMessageDispatcher
    {
        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<object?>(null);

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingTraceLogger : ILogger<MessageEngine>
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
        {
            if (logLevel == LogLevel.Trace &&
                formatter(state, exception).StartsWith("[消息路由]", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("trace logger failed");
            }
        }
    }

    private sealed class ThrowingAllLogger : ILogger<MessageEngine>
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

    private sealed class TrackingLifecycleDispatcher : IMessageDispatcher
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask<object?> DispatchAsync(
            MessageEnvelope message,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<object?>(null);

        public void Dispose()
        {
        }
    }

    private sealed class FailingStartResponseProcessor : IResponseProcessor
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            throw new InvalidOperationException("response start failed");
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs)
            => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FailingStartAndStopResponseProcessor : IResponseProcessor
    {
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("response start failed");

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            throw new InvalidOperationException("response stop failed");
        }

        public ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs)
            => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class StopReleasesResponseProcessor : IResponseProcessor
    {
        private int _stopCount;

        public TaskCompletionSource<bool> EnqueueEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCount => Volatile.Read(ref _stopCount);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _stopCount);
            Release.TrySetResult(true);
            return Task.CompletedTask;
        }

        public async ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs)
        {
            EnqueueEntered.TrySetResult(true);
            await Release.Task;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingResponseProcessor : IResponseProcessor
    {
        private int _processCount;

        public int ProcessCount => Volatile.Read(ref _processCount);

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask ProcessMessageResultAsync(MessageProcessedEventArgs eventArgs)
        {
            Interlocked.Increment(ref _processCount);
            return ValueTask.CompletedTask;
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
