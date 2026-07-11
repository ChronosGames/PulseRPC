using System.Collections.Concurrent;
using FluentAssertions;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests.Services;

public sealed class ActorLifecycleIntegrationTests
{
    [Fact]
    public async Task NonGenericEnqueue_SerializesOneHundredCommands_AndWaitsForCompletion()
    {
        var repository = new FakeStateRepository();
        await using var actor = new CounterActor("counter-1", repository, commandsInitiallyBlocked: true);
        await actor.StartAsync();

        var commands = Enumerable.Range(0, 100)
            .Select(_ => actor.EnqueueAsync(actor.IncrementAsync))
            .ToArray();
        var allCommands = Task.WhenAll(commands);

        await actor.FirstCommandStarted.WaitAsync(TimeSpan.FromSeconds(5));
        allCommands.IsCompleted.Should().BeFalse(
            "non-generic EnqueueAsync must complete only after the queued work has run");

        actor.ReleaseCommands();
        await allCommands;

        actor.Value.Should().Be(100);
        actor.MaxConcurrentCommands.Should().Be(1);
    }

    [Fact]
    public async Task NonGenericEnqueue_PropagatesWorkException()
    {
        var repository = new FakeStateRepository();
        await using var actor = new CounterActor("counter-1", repository);
        await actor.StartAsync();

        var action = () => actor.EnqueueAsync(
            () => Task.FromException(new InvalidOperationException("command failed")));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("command failed");
    }

    [Fact]
    public async Task StopAsync_DrainsAcceptedCommands_BeforeOnStoppingSavesState()
    {
        var repository = new FakeStateRepository();
        await using var actor = new CounterActor("counter-1", repository, commandsInitiallyBlocked: true);
        await actor.StartAsync();

        var firstCommand = actor.EnqueueAsync(actor.IncrementAsync);
        await actor.FirstCommandStarted.WaitAsync(TimeSpan.FromSeconds(5));
        var secondCommand = actor.EnqueueAsync(actor.IncrementAsync);

        var stop = actor.StopAsync();
        await WaitUntilAsync(() => actor.State == ServiceLifecycleState.Stopping);

        stop.IsCompleted.Should().BeFalse("the first accepted command is still in flight");
        repository.SaveCount.Should().Be(0, "OnStoppingAsync must run only after the mailbox is drained");

        actor.ReleaseCommands();
        await Task.WhenAll(firstCommand, secondCommand, stop);

        actor.Value.Should().Be(2);
        repository.GetSavedValue("counter-1").Should().Be(2);
        repository.SaveCount.Should().Be(1);
        actor.State.Should().Be(ServiceLifecycleState.Stopped);
    }

    [Fact]
    public async Task RecreatedActor_RestoresStateInOnStarting()
    {
        var repository = new FakeStateRepository();

        var first = new CounterActor("counter-1", repository);
        await first.StartAsync();
        await first.EnqueueAsync(first.IncrementAsync);
        await first.EnqueueAsync(first.IncrementAsync);
        await first.EnqueueAsync(first.IncrementAsync);
        await first.StopAsync();
        await first.DisposeAsync();

        repository.GetSavedValue("counter-1").Should().Be(3);

        var recreated = new CounterActor("counter-1", repository);
        try
        {
            await recreated.StartAsync();

            recreated.Value.Should().Be(3);
            repository.LoadCount.Should().Be(2);
        }
        finally
        {
            await recreated.StopAsync();
            await recreated.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(ServiceBackpressureMode.DropNewest, false)]
    [InlineData(ServiceBackpressureMode.DropNewest, true)]
    [InlineData(ServiceBackpressureMode.DropOldest, false)]
    [InlineData(ServiceBackpressureMode.DropOldest, true)]
    [InlineData(ServiceBackpressureMode.ThrowException, false)]
    [InlineData(ServiceBackpressureMode.ThrowException, true)]
    public async Task FullMailbox_DropOrThrow_CompletesRejectedRequest(
        ServiceBackpressureMode backpressureMode,
        bool useGenericEnqueue)
    {
        await using var actor = new MailboxActor(backpressureMode);
        await actor.StartAsync();

        Task Enqueue(Func<Task> work) => useGenericEnqueue
            ? actor.EnqueueAsync(async () =>
            {
                await work();
                return 42;
            })
            : actor.EnqueueAsync(work);

        var secondExecuted = false;
        var thirdExecuted = false;
        var first = Enqueue(actor.HoldFirstAsync);

        try
        {
            await actor.FirstWorkStarted.WaitAsync(TimeSpan.FromSeconds(5));

            var second = Enqueue(() =>
            {
                secondExecuted = true;
                return Task.CompletedTask;
            });
            var third = Enqueue(() =>
            {
                thirdExecuted = true;
                return Task.CompletedTask;
            });

            var rejected = backpressureMode == ServiceBackpressureMode.DropOldest ? second : third;
            var retained = backpressureMode == ServiceBackpressureMode.DropOldest ? third : second;
            var rejection = async () => await rejected.WaitAsync(TimeSpan.FromSeconds(5));

            await rejection.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*mailbox is full*");

            actor.ReleaseFirst();
            await Task.WhenAll(first, retained).WaitAsync(TimeSpan.FromSeconds(5));

            secondExecuted.Should().Be(backpressureMode != ServiceBackpressureMode.DropOldest);
            thirdExecuted.Should().Be(backpressureMode == ServiceBackpressureMode.DropOldest);
        }
        finally
        {
            actor.ReleaseFirst();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullMailbox_Block_WaitsForCapacityAndExecutesEveryRequest(bool useGenericEnqueue)
    {
        await using var actor = new MailboxActor(ServiceBackpressureMode.Block);
        await actor.StartAsync();

        Task Enqueue(Func<Task> work) => useGenericEnqueue
            ? actor.EnqueueAsync(async () =>
            {
                await work();
                return 42;
            })
            : actor.EnqueueAsync(work);

        var executionCount = 0;
        var first = Enqueue(actor.HoldFirstAsync);

        try
        {
            await actor.FirstWorkStarted.WaitAsync(TimeSpan.FromSeconds(5));
            var second = Enqueue(() =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.CompletedTask;
            });
            var third = Enqueue(() =>
            {
                Interlocked.Increment(ref executionCount);
                return Task.CompletedTask;
            });

            third.IsCompleted.Should().BeFalse("Block must wait while the mailbox is full");

            actor.ReleaseFirst();
            await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));

            executionCount.Should().Be(2);
        }
        finally
        {
            actor.ReleaseFirst();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class CounterActor : PulseServiceBase
    {
        private readonly FakeStateRepository _repository;
        private readonly TaskCompletionSource<bool> _commandGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _firstCommandStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _value;
        private int _activeCommands;
        private int _maxConcurrentCommands;

        public CounterActor(
            string serviceId,
            FakeStateRepository repository,
            bool commandsInitiallyBlocked = false)
            : base("Counter", serviceId, executionOptions: ServiceExecutionOptions.Actor)
        {
            _repository = repository;
            if (!commandsInitiallyBlocked)
            {
                _commandGate.TrySetResult(true);
            }
        }

        public int Value => Volatile.Read(ref _value);

        public int MaxConcurrentCommands => Volatile.Read(ref _maxConcurrentCommands);

        public Task FirstCommandStarted => _firstCommandStarted.Task;

        public void ReleaseCommands() => _commandGate.TrySetResult(true);

        public override async Task OnStartingAsync(CancellationToken cancellationToken = default)
        {
            _value = await _repository.LoadAsync(ServiceId, cancellationToken);
        }

        public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
            => _repository.SaveAsync(ServiceId, Value, cancellationToken);

        public async Task IncrementAsync()
        {
            var active = Interlocked.Increment(ref _activeCommands);
            UpdateMaximum(ref _maxConcurrentCommands, active);
            _firstCommandStarted.TrySetResult(true);

            try
            {
                await _commandGate.Task;
                var current = _value;
                await Task.Yield();
                _value = current + 1;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCommands);
            }
        }

        private static void UpdateMaximum(ref int target, int candidate)
        {
            int current;
            while (candidate > (current = Volatile.Read(ref target)))
            {
                if (Interlocked.CompareExchange(ref target, candidate, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class MailboxActor : PulseServiceBase
    {
        private readonly TaskCompletionSource<bool> _firstWorkStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _firstWorkGate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MailboxActor(ServiceBackpressureMode backpressureMode)
            : base(
                "Mailbox",
                "mailbox-1",
                executionOptions: new ServiceExecutionOptions
                {
                    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
                    MaxConcurrency = 1,
                    QueueCapacity = 1,
                    BackpressureMode = backpressureMode
                })
        {
        }

        public Task FirstWorkStarted => _firstWorkStarted.Task;

        public async Task HoldFirstAsync()
        {
            _firstWorkStarted.TrySetResult(true);
            await _firstWorkGate.Task;
        }

        public void ReleaseFirst() => _firstWorkGate.TrySetResult(true);
    }

    private sealed class FakeStateRepository
    {
        private readonly ConcurrentDictionary<string, int> _states = new(StringComparer.Ordinal);
        private int _loadCount;
        private int _saveCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public int SaveCount => Volatile.Read(ref _saveCount);

        public Task<int> LoadAsync(string actorId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            return Task.FromResult(_states.GetValueOrDefault(actorId));
        }

        public Task SaveAsync(string actorId, int value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _states[actorId] = value;
            Interlocked.Increment(ref _saveCount);
            return Task.CompletedTask;
        }

        public int GetSavedValue(string actorId) => _states.GetValueOrDefault(actorId);
    }
}
