using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// P7 L3：<see cref="ActorMigrationCoordinator"/> —— keyed Actor 的优雅迁出/迁入（静默排空 → 捕获快照 →
/// 跨节点搬运 → 目标恢复+激活 → 释放租约），实现跨激活状态保留。
/// </summary>
public class ActorMigrationCoordinatorTests
{
    private const string Local = "node-local";
    private const string Target = "node-target";
    private const string Hub = "RoomHub";
    private const string Key = "room-42";

    /// <summary>可迁移的假 Actor：记录生命周期调用顺序，状态为一个字符串。</summary>
    private sealed class FakeMigratableActor : IPulseService, IActorStateSnapshot
    {
        public string State = string.Empty;
        public readonly ConcurrentQueue<string> Calls = new();
        public bool FailOnStart { get; init; }

        public string ServiceType => "Room";
        public string ServiceId => Key;
        ServiceLifecycleState IPulseService.State => ServiceLifecycleState.Running;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Calls.Enqueue("Start");
            return FailOnStart
                ? Task.FromException(new InvalidOperationException("start failed"))
                : Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) { Calls.Enqueue("Stop"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Calls.Enqueue("Dispose"); return default; }

        public ValueTask<byte[]> CaptureStateAsync(CancellationToken cancellationToken = default)
        {
            Calls.Enqueue("Capture");
            return new ValueTask<byte[]>(Encoding.UTF8.GetBytes(State));
        }

        public ValueTask RestoreStateAsync(byte[] state, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue("Restore");
            State = Encoding.UTF8.GetString(state);
            return default;
        }
    }

    private sealed class NonMigratableActor : IPulseService
    {
        public readonly ConcurrentQueue<string> Calls = new();
        public string ServiceType => "Room";
        public string ServiceId => Key;
        ServiceLifecycleState IPulseService.State => ServiceLifecycleState.Running;
        public Task StartAsync(CancellationToken cancellationToken = default) { Calls.Enqueue("Start"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { Calls.Enqueue("Stop"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => default;
    }

    private static ActorMigrationCoordinator Create(
        IActorDirectory directory,
        IActorStateTransport transport,
        string localNodeId = Local,
        IActorLeaseHeartbeat? heartbeat = null)
        => new(directory, transport, Options.Create(new ClusterTopologyOptions { LocalNodeId = localNodeId }),
            heartbeat,
            NullLogger<ActorMigrationCoordinator>.Instance);

    [Fact]
    public async Task MigrateOut_QuiescesCapturesTransfersAndReleases_InOrder()
    {
        var directory = Substitute.For<IActorDirectory>();
        directory.ResolveAsync(Hub, Key, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement?>(new ActorPlacement(Local, "lease-1", DateTime.UtcNow.AddMinutes(1).Ticks)));
        var transport = Substitute.For<IActorStateTransport>();
        var actor = new FakeMigratableActor { State = "player-count=7" };

        var coordinator = Create(directory, transport);
        await coordinator.MigrateOutAsync(Hub, Key, Target, actor);

        // 顺序：先 Stop（排空在途）再 Capture（状态稳定）。
        actor.Calls.Should().ContainInOrder("Stop", "Capture", "Dispose");

        // 快照按捕获内容跨节点搬运。
        await transport.Received(1).SendSnapshotAsync(Target, Hub, Key,
            Arg.Is<byte[]>(b => Encoding.UTF8.GetString(b) == "player-count=7"), Arg.Any<CancellationToken>());

        // 释放本地租约（属主转移）。
        await directory.Received(1).ReleaseAsync(Hub, Key, Local, "lease-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateOut_NonMigratableActor_TransfersEmptySnapshot()
    {
        var directory = Substitute.For<IActorDirectory>();
        directory.ResolveAsync(Hub, Key, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement?>((ActorPlacement?)null));
        var transport = Substitute.For<IActorStateTransport>();
        var actor = new NonMigratableActor();

        var coordinator = Create(directory, transport);
        await coordinator.MigrateOutAsync(Hub, Key, Target, actor);

        actor.Calls.Should().Contain("Stop");
        await transport.Received(1).SendSnapshotAsync(Target, Hub, Key,
            Arg.Is<byte[]>(b => b.Length == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateOut_ToLocalNode_Throws()
    {
        var coordinator = Create(Substitute.For<IActorDirectory>(), Substitute.For<IActorStateTransport>());
        var act = async () => await coordinator.MigrateOutAsync(Hub, Key, Local, new FakeMigratableActor());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MigrateIn_ActivatesRestoresThenStarts_InOrder_AndPreservesState()
    {
        var directory = Substitute.For<IActorDirectory>();
        directory.ActivateAsync(Hub, Key, Local, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(new ActorPlacement(Local, "lease-2", DateTime.UtcNow.AddMinutes(1).Ticks)));
        var actor = new FakeMigratableActor();

        var heartbeat = Substitute.For<IActorLeaseHeartbeat>();
        var coordinator = Create(directory, Substitute.For<IActorStateTransport>(), heartbeat: heartbeat);
        var snapshot = Encoding.UTF8.GetBytes("player-count=7");
        var placement = await coordinator.MigrateInAsync(Hub, Key, snapshot, actor);

        placement.NodeId.Should().Be(Local);
        actor.State.Should().Be("player-count=7", "迁入后状态应从快照恢复（跨激活保留）");
        // 顺序：Restore 必须在 Start 之前（恢复完再开始处理消息）。
        actor.Calls.Should().ContainInOrder("Restore", "Start");
        heartbeat.Received(1).Track(Hub, Key, placement);
    }

    [Fact]
    public async Task MigrateIn_WhenStartFails_DisposesAndReleasesAcquiredLease()
    {
        var placement = new ActorPlacement(Local, "lease-2", DateTime.UtcNow.AddMinutes(1).Ticks);
        var directory = Substitute.For<IActorDirectory>();
        directory.ActivateAsync(Hub, Key, Local, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(placement));
        var heartbeat = Substitute.For<IActorLeaseHeartbeat>();
        var actor = new FakeMigratableActor { FailOnStart = true };

        var action = async () => await Create(
            directory,
            Substitute.For<IActorStateTransport>(),
            heartbeat: heartbeat).MigrateInAsync(Hub, Key, Encoding.UTF8.GetBytes("x"), actor);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("start failed");
        actor.Calls.Should().ContainInOrder("Restore", "Start", "Dispose");
        heartbeat.DidNotReceive().Track(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ActorPlacement>());
        await directory.Received(1).ReleaseAsync(Hub, Key, Local, placement.LeaseId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MigrateIn_WhenAlreadyOwnedByAnotherNode_DoesNotRestoreOrStart()
    {
        var directory = Substitute.For<IActorDirectory>();
        // 目录显示属主已是别的节点（并发抢占）：应放弃本地恢复，避免双激活。
        directory.ActivateAsync(Hub, Key, Local, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(new ActorPlacement("node-other", "lease-x", DateTime.UtcNow.AddMinutes(1).Ticks)));
        var actor = new FakeMigratableActor();

        var coordinator = Create(directory, Substitute.For<IActorStateTransport>());
        var placement = await coordinator.MigrateInAsync(Hub, Key, Encoding.UTF8.GetBytes("x"), actor);

        placement.NodeId.Should().Be("node-other");
        actor.Calls.Should().NotContain("Restore");
        actor.Calls.Should().NotContain("Start");
        actor.Calls.Should().Contain("Dispose");
    }

    [Fact]
    public async Task RoundTrip_OutThenIn_PreservesStateAcrossActivation()
    {
        // 端到端（进程内模拟）：迁出节点捕获 -> 传输 -> 迁入节点恢复，状态跨激活保留。
        byte[]? transferred = null;
        var transport = Substitute.For<IActorStateTransport>();
        transport.SendSnapshotAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(ci => { transferred = ci.ArgAt<byte[]>(3); return ValueTask.CompletedTask; });

        var outDir = Substitute.For<IActorDirectory>();
        outDir.ResolveAsync(Hub, Key, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement?>(new ActorPlacement(Local, "lease-1", DateTime.UtcNow.AddMinutes(1).Ticks)));
        var source = new FakeMigratableActor { State = "score=999" };
        await Create(outDir, transport, Local).MigrateOutAsync(Hub, Key, Target, source);

        transferred.Should().NotBeNull();

        var inDir = Substitute.For<IActorDirectory>();
        inDir.ActivateAsync(Hub, Key, Target, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<ActorPlacement>(new ActorPlacement(Target, "lease-2", DateTime.UtcNow.AddMinutes(1).Ticks)));
        var destination = new FakeMigratableActor();
        await Create(inDir, transport, Target).MigrateInAsync(Hub, Key, transferred!, destination);

        destination.State.Should().Be("score=999", "状态应端到端跨激活保留");
    }
}
