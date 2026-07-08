using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// Phase D 回归：租约目录的 CAS + TTL 后端边界、owner 心跳续租与失效后重激活语义。
/// </summary>
public class PhaseDLeaseDirectoryRecoveryTests
{
    [Fact]
    public async Task InMemoryActorLeaseStore_MustKeepSingleOwnerUntilTtlExpires_ThenAllowReactivation()
    {
        var store = new InMemoryActorLeaseStore();

        var first = await store.ActivateAsync("RoomHub", "room-1", "node-a", TimeSpan.FromMilliseconds(20));
        var second = await store.ActivateAsync("RoomHub", "room-1", "node-b", TimeSpan.FromMilliseconds(20));

        second.NodeId.Should().Be("node-a", "有效 TTL 内 CAS 激活应返回既有 owner，避免重复激活");
        second.LeaseId.Should().Be(first.LeaseId);

        await Task.Delay(TimeSpan.FromMilliseconds(60));
        var reactivated = await store.ActivateAsync("RoomHub", "room-1", "node-b", TimeSpan.FromSeconds(1));

        reactivated.NodeId.Should().Be("node-b", "租约过期后新 owner 应能重新激活");
        reactivated.LeaseId.Should().NotBe(first.LeaseId);
    }

    [Fact]
    public async Task LeaseActorDirectory_MustDelegateLeaseDurationToStore()
    {
        var store = Substitute.For<IActorLeaseStore>();
        var expected = new ActorPlacement("node-a", "lease-1", DateTime.UtcNow.AddSeconds(30).Ticks);
        store.ActivateAsync("RoomHub", "room-1", "node-a", TimeSpan.FromSeconds(12), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(expected));
        var directory = new LeaseActorDirectory(
            Options.Create(new LeaseActorDirectoryOptions { LeaseDuration = TimeSpan.FromSeconds(12) }),
            store);

        var placement = await directory.ActivateAsync("RoomHub", "room-1", "node-a");

        placement.Should().Be(expected);
        await store.Received(1).ActivateAsync("RoomHub", "room-1", "node-a", TimeSpan.FromSeconds(12), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActorLeaseHeartbeat_MustRenewTrackedLease_AndUntrackWhenRenewFails()
    {
        var directory = Substitute.For<IActorDirectory>();
        var renewAttempts = 0;
        directory.RenewAsync("RoomHub", "room-1", "node-a", "lease-1", Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<bool>(Interlocked.Increment(ref renewAttempts) == 1));

        using var heartbeat = new ActorLeaseHeartbeat(directory, new ActorLeaseHeartbeatOptions { Interval = TimeSpan.FromMilliseconds(10) });
        heartbeat.Track("RoomHub", "room-1", new ActorPlacement("node-a", "lease-1", DateTime.UtcNow.AddSeconds(30).Ticks));

        await Task.Delay(TimeSpan.FromMilliseconds(80));

        renewAttempts.Should().BeGreaterThanOrEqualTo(2, "第一次续租成功，第二次续租失败后应停止跟踪");
        var attemptsAfterUntrack = renewAttempts;
        await Task.Delay(TimeSpan.FromMilliseconds(40));
        renewAttempts.Should().Be(attemptsAfterUntrack, "续租失败代表租约被抢占或失效，应停止继续心跳");
    }
}
