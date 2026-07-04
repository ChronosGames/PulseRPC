using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// 回归测试：<see cref="LeaseActorDirectory"/>（§P4 L2 租约 <c>IActorDirectory</c>）单一激活语义。
/// </summary>
public class LeaseActorDirectoryTests
{
    private static LeaseActorDirectory CreateDirectory(TimeSpan? leaseDuration = null)
    {
        var options = new LeaseActorDirectoryOptions
        {
            LeaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30),
        };
        return new LeaseActorDirectory(Options.Create(options));
    }

    [Fact]
    public async Task ResolveAsync_UnactivatedInstance_MustReturnNull()
    {
        var directory = CreateDirectory();

        var placement = await directory.ResolveAsync("ChatRoomHub", "room-1");

        placement.Should().BeNull();
    }

    [Fact]
    public async Task ActivateAsync_ThenResolve_MustReturnSamePlacement()
    {
        var directory = CreateDirectory();

        var activated = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");
        var resolved = await directory.ResolveAsync("ChatRoomHub", "room-1");

        resolved.Should().NotBeNull();
        resolved!.Value.NodeId.Should().Be("node-a");
        resolved.Value.LeaseId.Should().Be(activated.LeaseId);
    }

    [Fact]
    public async Task ActivateAsync_WhileValidLeaseHeldByOtherNode_MustReturnExistingPlacementNotOverride()
    {
        var directory = CreateDirectory();

        var first = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");
        // node-b 尝试激活同一实例：既有租约仍有效，必须返回既有放置（属主仍是 node-a），保证单一激活。
        var second = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-b");

        second.NodeId.Should().Be("node-a");
        second.LeaseId.Should().Be(first.LeaseId);
    }

    [Fact]
    public async Task RenewAsync_WithCorrectNodeAndLease_MustSucceedAndExtendExpiry()
    {
        var directory = CreateDirectory();
        var placement = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");

        var renewed = await directory.RenewAsync("ChatRoomHub", "room-1", "node-a", placement.LeaseId);

        renewed.Should().BeTrue();
    }

    [Fact]
    public async Task RenewAsync_WithWrongLeaseId_MustFail()
    {
        var directory = CreateDirectory();
        await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");

        var renewed = await directory.RenewAsync("ChatRoomHub", "room-1", "node-a", "bogus-lease-id");

        renewed.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_WithCorrectOwnerAndLease_MustAllowReactivationByAnotherNode()
    {
        var directory = CreateDirectory();
        var placement = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");

        await directory.ReleaseAsync("ChatRoomHub", "room-1", "node-a", placement.LeaseId);
        var resolved = await directory.ResolveAsync("ChatRoomHub", "room-1");
        var reactivated = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-b");

        resolved.Should().BeNull("释放后目录不应再返回放置信息");
        reactivated.NodeId.Should().Be("node-b", "释放后其它节点应可成功激活");
    }

    [Fact]
    public async Task ReleaseAsync_WithMismatchedLease_MustNotRemoveExistingLease()
    {
        var directory = CreateDirectory();
        var placement = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");

        await directory.ReleaseAsync("ChatRoomHub", "room-1", "node-a", "wrong-lease-id");
        var resolved = await directory.ResolveAsync("ChatRoomHub", "room-1");

        resolved.Should().NotBeNull("租约 ID 不匹配时不应释放既有有效租约");
        resolved!.Value.LeaseId.Should().Be(placement.LeaseId);
    }

    [Fact]
    public async Task ActivateAsync_AfterLeaseExpires_MustAllowNewNodeToActivate()
    {
        var directory = CreateDirectory(leaseDuration: TimeSpan.FromMilliseconds(1));
        await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");

        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var reactivated = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-b");

        reactivated.NodeId.Should().Be("node-b");
    }

    [Fact]
    public async Task DifferentKeys_MustBeActivatedIndependently()
    {
        var directory = CreateDirectory();

        var placementA = await directory.ActivateAsync("ChatRoomHub", "room-1", "node-a");
        var placementB = await directory.ActivateAsync("ChatRoomHub", "room-2", "node-b");

        placementA.NodeId.Should().Be("node-a");
        placementB.NodeId.Should().Be("node-b");
    }
}
