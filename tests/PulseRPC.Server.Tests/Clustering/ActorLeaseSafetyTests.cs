using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Services;
using PulseRPC.Server.Services.Management;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public sealed class ActorLeaseSafetyTests
{
    [Fact]
    public void ExpiredLease_CannotBeRevivedByLateRenewal()
    {
        var time = new ManualTime();
        var revocations = 0;
        var lease = new ActorLeaseState("Player", "1", Placement(time), time, _ => revocations++);
        time.Advance(TimeSpan.FromSeconds(31));

        Assert.False(lease.IsValid);
        Assert.False(lease.Refresh(Placement(time)));
        Assert.False(lease.IsValid);
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.Equal(1, revocations);
    }

    [Fact]
    public void BackwardWallClock_DoesNotExtendLeaseExecution()
    {
        var time = new ManualTime();
        var lease = new ActorLeaseState("Player", "1", Placement(time), time, _ => { });
        time.Advance(TimeSpan.FromSeconds(31), wallClockDelta: TimeSpan.FromMinutes(-1));
        Assert.False(lease.IsValid);
    }

    [Fact]
    public async Task FailedRenewal_RejectsQueuedAndNewWorkButRetainsInFlightOwnership()
    {
        var time = new ManualTime();
        var directory = Substitute.For<IActorDirectory>();
        var removed = new TaskCompletionSource<IPulseService>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heartbeat = CreateHeartbeat(directory, time, service => { removed.TrySetResult(service); return default; });
        await using var actor = new TestActor("1");
        Bind(heartbeat, actor, Placement(time));
        await actor.StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = actor.EnqueueAsync(async () => { entered.SetResult(); await finish.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queuedExecuted = false;
        var queued = actor.EnqueueAsync(() => { queuedExecuted = true; return Task.CompletedTask; });
        try
        {
            await RenewAsync(heartbeat);
            Assert.Same(actor, await removed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(actor.LeaseToken.IsCancellationRequested);
            Assert.False(first.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => actor.EnqueueAsync(() => Task.CompletedTask));
        }
        finally { finish.TrySetResult(); }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(queuedExecuted);
    }

    [Fact]
    public async Task Expiry_IsCheckedWhileBackendRenewalIsBlocked()
    {
        var time = new ManualTime();
        var directory = Substitute.For<IActorDirectory>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.RenewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.TrySetResult(); return new ValueTask<bool>(finish.Task); });
        directory.ResolveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<ActorPlacement?>(Placement(time)));
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var heartbeat = CreateHeartbeat(directory, time, _ => { removed.TrySetResult(); return default; });
        await using var actor = new TestActor("1");
        Bind(heartbeat, actor, Placement(time));
        await actor.StartAsync();
        var renewal = RenewAsync(heartbeat);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            time.Advance(TimeSpan.FromSeconds(31));
            heartbeat.CheckExpirations();
            await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(actor.LeaseToken.IsCancellationRequested);
        }
        finally { finish.TrySetResult(true); }
        await renewal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.EnqueueAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task TransientRenewalFailure_OnlyAllowsExecutionUntilConfirmedExpiry()
    {
        var time = new ManualTime();
        var directory = Substitute.For<IActorDirectory>();
        directory.RenewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<bool>(Task.FromException<bool>(new IOException("backend unavailable"))));
        using var heartbeat = CreateHeartbeat(directory, time, _ => default);
        await using var actor = new TestActor("1");
        Bind(heartbeat, actor, Placement(time));
        await actor.StartAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        await RenewAsync(heartbeat);
        Assert.Equal(42, await actor.EnqueueAsync(() => Task.FromResult(42)));
        time.Advance(TimeSpan.FromSeconds(26));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.EnqueueAsync(() => Task.FromResult(42)));
    }

    [Fact]
    public async Task SuccessfulRenewal_UsesConfirmedBackendDeadline()
    {
        var time = new ManualTime();
        var directory = Substitute.For<IActorDirectory>();
        directory.RenewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        directory.ResolveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<ActorPlacement?>(Placement(time)));
        using var heartbeat = CreateHeartbeat(directory, time, _ => default);
        await using var actor = new TestActor("1");
        Bind(heartbeat, actor, Placement(time));
        await actor.StartAsync();
        time.Advance(TimeSpan.FromSeconds(10));
        await RenewAsync(heartbeat);
        time.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal(42, await actor.EnqueueAsync(() => Task.FromResult(42)));
        time.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.EnqueueAsync(() => Task.FromResult(42)));
    }

    [Fact]
    public async Task TimedOutBackendOperations_RemainBoundedAcrossRenewalRounds()
    {
        var time = new ManualTime();
        var directory = Substitute.For<IActorDirectory>();
        var calls = 0;
        var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.RenewAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Interlocked.Increment(ref calls); return new ValueTask<bool>(finish.Task); });
        using var heartbeat = new ActorLeaseHeartbeat(directory, new ActorLeaseHeartbeatOptions
        {
            Interval = TimeSpan.FromHours(1), MaxConcurrentRenewals = 2,
            RenewalTimeout = TimeSpan.FromMilliseconds(50)
        }, null, time);
        for (var i = 0; i < 8; i++) heartbeat.Track("Player", i.ToString(), Placement(time));
        try
        {
            await RenewAsync(heartbeat).WaitAsync(TimeSpan.FromSeconds(5));
            await RenewAsync(heartbeat).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally { finish.TrySetResult(false); }
    }

    [Fact]
    public async Task ExistingInstance_CannotBeReboundToNewLease()
    {
        var time = new ManualTime();
        using var heartbeat = CreateHeartbeat(Substitute.For<IActorDirectory>(), time, _ => default);
        await using var actor = new TestActor("1");
        Bind(heartbeat, actor, Placement(time));
        await actor.StartAsync();
        Assert.Throws<InvalidOperationException>(() =>
            ((IActorLeaseBinding)heartbeat).BindService("Player", "1", Placement(time, "new-lease"), actor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => actor.EnqueueAsync(() => Task.CompletedTask));
    }

    [Fact]
    public async Task DelayedCleanup_CannotRemoveReplacementInstance()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        await using var manager = new PulseServiceManager(provider, NullLogger<PulseServiceManager>.Instance);
        manager.Register<TestActor>((_, key) => new TestActor(key));
        var old = await manager.GetOrCreateServiceAsync(nameof(TestActor), "1");
        Assert.True(await manager.RemoveServiceAsync(nameof(TestActor), "1"));
        var replacement = await manager.GetOrCreateServiceAsync(nameof(TestActor), "1");
        await manager.RemoveServiceIfSameAsync(old);
        Assert.Same(replacement, manager.GetService(nameof(TestActor), "1"));
        Assert.Equal(ServiceLifecycleState.Running, replacement.State);
    }

    private static ActorLeaseHeartbeat CreateHeartbeat(IActorDirectory directory, TimeProvider time,
        Func<IPulseService, ValueTask> cleanup)
        => new(directory, new ActorLeaseHeartbeatOptions { Interval = TimeSpan.FromHours(1) }, cleanup, time);

    private static ActorPlacement Placement(TimeProvider time, string leaseId = "lease-1")
        => new("node-1", leaseId, time.GetUtcNow().AddSeconds(30).UtcTicks);

    private static void Bind(ActorLeaseHeartbeat heartbeat, TestActor actor, ActorPlacement placement)
    {
        ((IActorLeaseBinding)heartbeat).BindService("Player", actor.ServiceId, placement, actor);
        heartbeat.Track("Player", actor.ServiceId, placement);
    }

    private static Task RenewAsync(ActorLeaseHeartbeat heartbeat)
        => (Task)typeof(ActorLeaseHeartbeat).GetMethod("RenewAllAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(heartbeat, null)!;

    [PulseService(StartupType = ServiceStartupType.OnDemand, InstanceScope = ServiceInstanceScope.MultiInstance)]
    private sealed class TestActor : PulseServiceBase
    {
        public TestActor(string key) : base(nameof(TestActor), key, executionOptions: ServiceExecutionOptions.Actor) { }
        public CancellationToken LeaseToken => ActorLeaseCancellationToken;
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _utc = DateTime.UtcNow.Ticks;
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utc), TimeSpan.Zero);
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan elapsed, TimeSpan? wallClockDelta = null)
        {
            Interlocked.Add(ref _timestamp, elapsed.Ticks);
            Interlocked.Add(ref _utc, (wallClockDelta ?? elapsed).Ticks);
        }
    }
}
