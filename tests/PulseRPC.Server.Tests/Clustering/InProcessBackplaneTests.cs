using System;
using System.Threading.Tasks;
using FluentAssertions;
using PulseRPC.Clustering;
using PulseRPC.Routing;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// 回归测试：<see cref="InProcessBackplane"/> —— 单节点默认后端必须是纯 no-op（<see cref="IPulseBackplane.PublishAsync"/>
/// 不做任何事、<see cref="IPulseBackplane.ResolveAsync"/> 恒为空集合），且新增的 <see cref="IPulseBackplane.Subscribe"/>
/// 契约必须满足"可安全 Dispose、never invokes handler"（因为单节点下压根没有跨节点广播可转发）。
/// </summary>
public class InProcessBackplaneTests
{
    [Fact]
    public async Task PublishAsync_IsNoop_AndCompletesSynchronously()
    {
        var backplane = new InProcessBackplane();

        await backplane.PublishAsync(PulseAddress.AllClients("Hub"), 0x1, new byte[] { 1, 2 }, "node-a");
        // 无异常即通过：no-op 实现不应有任何可观察副作用。
    }

    [Fact]
    public async Task ResolveAsync_AlwaysReturnsEmptyCollection()
    {
        var backplane = new InProcessBackplane();

        var result = await backplane.ResolveAsync(PulseAddress.Group("Hub", "room-1"));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Subscribe_NeverInvokesHandler_EvenAfterPublish()
    {
        var backplane = new InProcessBackplane();
        var invoked = false;
        using var subscription = backplane.Subscribe((_, _, _, _, _) =>
        {
            invoked = true;
            return default;
        });

        _ = backplane.PublishAsync(PulseAddress.AllClients("Hub"), 0x1, ReadOnlyMemory<byte>.Empty, "node-a");

        invoked.Should().BeFalse("InProcessBackplane 是单节点 no-op：不存在其它节点，Publish 不应触发任何订阅回调");
    }

    [Fact]
    public void Subscribe_WithNullHandler_MustThrow()
    {
        var backplane = new InProcessBackplane();

        var act = () => backplane.Subscribe(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Subscribe_ReturnedDisposable_CanBeDisposedSafely_EvenMultipleTimes()
    {
        var backplane = new InProcessBackplane();
        var subscription = backplane.Subscribe((_, _, _, _, _) => default);

        var act = () =>
        {
            subscription.Dispose();
            subscription.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AddMemberAsync_AndRemoveMemberAsync_AreNoop_AndDoNotAffectResolveAsync()
    {
        var backplane = new InProcessBackplane();

        await backplane.AddMemberAsync("conn-1", PulseAddress.User("Hub", "alice"), "node-a");
        var afterAdd = await backplane.ResolveAsync(PulseAddress.User("Hub", "alice"));
        await backplane.RemoveMemberAsync("conn-1", PulseAddress.User("Hub", "alice"), "node-a");
        var afterRemove = await backplane.ResolveAsync(PulseAddress.User("Hub", "alice"));

        afterAdd.Should().BeEmpty();
        afterRemove.Should().BeEmpty();
    }
}
