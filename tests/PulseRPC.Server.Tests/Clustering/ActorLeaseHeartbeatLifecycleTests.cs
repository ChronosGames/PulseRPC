using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Services.Management;
using System.Reflection;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

public sealed class ActorLeaseHeartbeatLifecycleTests
{
    [Fact]
    public async Task ReleaseAsync_UntracksAndCompareReleasesCurrentLease()
    {
        var directory = Substitute.For<IActorDirectory>();
        using var heartbeat = new ActorLeaseHeartbeat(
            directory,
            new ActorLeaseHeartbeatOptions { Interval = TimeSpan.FromHours(1) });
        heartbeat.Track(
            "RoomHub",
            "room-1",
            new ActorPlacement("node-a", "lease-1", DateTime.UtcNow.AddMinutes(1).Ticks));

        await ((IServiceInstanceLeaseLifetime)heartbeat).ReleaseAsync("RoomHub", "room-1");

        await directory.Received(1).ReleaseAsync(
            "RoomHub",
            "room-1",
            "node-a",
            "lease-1",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedRenewalOfOldLease_MustNotRemoveConcurrentReplacement()
    {
        var directory = Substitute.For<IActorDirectory>();
        var oldRenewEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finishOldRenew = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        directory.RenewAsync(
                "RoomHub",
                "room-1",
                "node-a",
                "lease-old",
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                oldRenewEntered.TrySetResult(true);
                return new ValueTask<bool>(finishOldRenew.Task);
            });
        directory.RenewAsync(
                "RoomHub",
                "room-1",
                "node-a",
                "lease-new",
                Arg.Any<CancellationToken>())
            .Returns(true);
        using var heartbeat = new ActorLeaseHeartbeat(
            directory,
            new ActorLeaseHeartbeatOptions { Interval = TimeSpan.FromHours(1) });
        heartbeat.Track(
            "RoomHub",
            "room-1",
            new ActorPlacement("node-a", "lease-old", DateTime.UtcNow.AddMinutes(1).Ticks));

        var renewMethod = typeof(ActorLeaseHeartbeat).GetMethod(
            "RenewAllAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var renew = (Task)renewMethod.Invoke(heartbeat, null)!;
        await oldRenewEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        heartbeat.Track(
            "RoomHub",
            "room-1",
            new ActorPlacement("node-a", "lease-new", DateTime.UtcNow.AddMinutes(1).Ticks));
        finishOldRenew.TrySetResult(false);
        await renew.WaitAsync(TimeSpan.FromSeconds(5));

        await ((IServiceInstanceLeaseLifetime)heartbeat).ReleaseAsync("RoomHub", "room-1");

        await directory.Received(1).ReleaseAsync(
            "RoomHub",
            "room-1",
            "node-a",
            "lease-new",
            Arg.Any<CancellationToken>());
        await directory.DidNotReceive().ReleaseAsync(
            "RoomHub",
            "room-1",
            "node-a",
            "lease-old",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dispose_MustWaitForInFlightRenewal()
    {
        var directory = Substitute.For<IActorDirectory>();
        var renewEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRenew = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        directory.RenewAsync(
                "RoomHub",
                "room-1",
                "node-a",
                "lease-1",
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                renewEntered.TrySetResult(true);
                return new ValueTask<bool>(finishRenew.Task);
            });
        var heartbeat = new ActorLeaseHeartbeat(
            directory,
            new ActorLeaseHeartbeatOptions { Interval = TimeSpan.FromMilliseconds(10) });
        heartbeat.Track(
            "RoomHub",
            "room-1",
            new ActorPlacement("node-a", "lease-1", DateTime.UtcNow.AddMinutes(1).Ticks));
        await renewEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = Task.Run(heartbeat.Dispose);
        await Task.Delay(50);
        Assert.False(disposal.IsCompleted);

        finishRenew.TrySetResult(true);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
