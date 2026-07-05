using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;
using PulseRPC.Infrastructure.Discovery;
using Xunit;

namespace PulseRPC.Infrastructure.Tests;

/// <summary>
/// P8：<see cref="DiscoveryClusterMembership"/> 的后端无关逻辑单元测试（自注册 / 拉取 / watch 触发 /
/// 变更检测 / 存活集 / 端点解析 / 健康提示），使用可控的 <see cref="FakeDiscoveryProvider"/> 驱动。
/// </summary>
public class DiscoveryClusterMembershipTests
{
    private const string Local = "node-local";
    private const string A = "node-a";
    private const string B = "node-b";

    private sealed class FakeDiscoveryProvider : IDiscoveryProvider
    {
        private volatile IReadOnlyList<DiscoveredNode> _nodes = Array.Empty<DiscoveredNode>();
        public int RegisterCount;
        public int DeregisterCount;
        public event Action? Changed;

        public void SetNodes(params DiscoveredNode[] nodes) => _nodes = nodes;
        public void RaiseChanged() => Changed?.Invoke();

        public Task RegisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref RegisterCount);
            return Task.CompletedTask;
        }

        public Task DeregisterAsync(DiscoveredNode self, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref DeregisterCount);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiscoveredNode>> FetchNodesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_nodes);
    }

    private static DiscoveredNode Node(string id, int port) => new(id, new NodeEndpoint("10.0.0." + port, port));

    private static (DiscoveryClusterMembership membership, FakeDiscoveryProvider provider) Create(TimeSpan? poll = null)
    {
        var provider = new FakeDiscoveryProvider();
        var options = Options.Create(new DiscoveryOptions
        {
            LocalNodeId = Local,
            AdvertiseHost = "10.0.0.1",
            AdvertisePort = 5000,
            PollInterval = poll ?? TimeSpan.FromMilliseconds(200),
        });
        var membership = new DiscoveryClusterMembership(provider, options, NullLogger<DiscoveryClusterMembership>.Instance);
        return (membership, provider);
    }

    [Fact]
    public void BeforeStart_LiveSetContainsLocalNode()
    {
        var (membership, _) = Create();
        using (membership)
        {
            membership.LiveNodeIds.Should().Contain(Local);
            membership.TryResolve(Local, out var ep).Should().BeTrue();
            ep.Should().Be(new NodeEndpoint("10.0.0.1", 5000));
        }
    }

    [Fact]
    public async Task StartAsync_RegistersSelf_AndFetchesInitialMembers()
    {
        var (membership, provider) = Create();
        provider.SetNodes(Node(A, 5001), Node(B, 5002));

        await membership.StartAsync(CancellationToken.None);
        try
        {
            provider.RegisterCount.Should().Be(1);
            membership.LiveNodeIds.Should().BeEquivalentTo(new[] { Local, A, B });
            membership.TryResolve(A, out var epA).Should().BeTrue();
            epA.Should().Be(new NodeEndpoint("10.0.0.5001", 5001));
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
            membership.Dispose();
        }
    }

    [Fact]
    public async Task StopAsync_DeregistersSelf()
    {
        var (membership, provider) = Create();
        await membership.StartAsync(CancellationToken.None);
        await membership.StopAsync(CancellationToken.None);
        membership.Dispose();

        provider.DeregisterCount.Should().Be(1);
    }

    [Fact]
    public async Task ProviderChanged_TriggersRefresh_AndRaisesChanged()
    {
        var (membership, provider) = Create(poll: TimeSpan.FromMinutes(5)); // 长轮询，确保是 watch 触发而非轮询
        provider.SetNodes(Node(A, 5001));
        await membership.StartAsync(CancellationToken.None);
        try
        {
            membership.LiveNodeIds.Should().BeEquivalentTo(new[] { Local, A });

            var changedFired = 0;
            membership.Changed += () => Interlocked.Increment(ref changedFired);

            provider.SetNodes(Node(A, 5001), Node(B, 5002));
            provider.RaiseChanged(); // 模拟后端 watch 触发

            var converged = await SpinAsync(
                () => membership.LiveNodeIds.Contains(B) && Volatile.Read(ref changedFired) >= 1,
                TimeSpan.FromSeconds(3));
            converged.Should().BeTrue("watch 触发后应立即拉取并纳入新节点 B");
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
            membership.Dispose();
        }
    }

    [Fact]
    public async Task Polling_ConvergesWithoutWatch()
    {
        var (membership, provider) = Create(poll: TimeSpan.FromMilliseconds(150));
        provider.SetNodes(Node(A, 5001));
        await membership.StartAsync(CancellationToken.None);
        try
        {
            // 不触发 watch，仅靠轮询发现 B 加入。
            provider.SetNodes(Node(A, 5001), Node(B, 5002));
            var converged = await SpinAsync(() => membership.LiveNodeIds.Contains(B), TimeSpan.FromSeconds(3));
            converged.Should().BeTrue("纯轮询应在若干周期内收敛到最新成员集");
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
            membership.Dispose();
        }
    }

    [Fact]
    public async Task ReportNodeFailure_TemporarilyEvicts_ReportSuccessReinstates()
    {
        var (membership, provider) = Create(poll: TimeSpan.FromMinutes(5));
        provider.SetNodes(Node(A, 5001), Node(B, 5002));
        await membership.StartAsync(CancellationToken.None);
        try
        {
            membership.ReportNodeFailure(A);
            membership.LiveNodeIds.Should().NotContain(A);
            membership.LiveNodeIds.Should().Contain(B);

            membership.ReportNodeSuccess(A);
            membership.LiveNodeIds.Should().Contain(A);
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
            membership.Dispose();
        }
    }

    [Fact]
    public async Task ReportNodeFailure_NeverEvictsLocalNode()
    {
        var (membership, provider) = Create(poll: TimeSpan.FromMinutes(5));
        provider.SetNodes(Node(A, 5001));
        await membership.StartAsync(CancellationToken.None);
        try
        {
            membership.ReportNodeFailure(Local);
            membership.LiveNodeIds.Should().Contain(Local);
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
            membership.Dispose();
        }
    }

    [Fact]
    public async Task BackendRemovingNode_TakesPrecedence_ClearsSuspicion()
    {
        var (membership, provider) = Create(poll: TimeSpan.FromMilliseconds(150));
        provider.SetNodes(Node(A, 5001), Node(B, 5002));
        await membership.StartAsync(CancellationToken.None);
        try
        {
            membership.ReportNodeFailure(A); // 先临时可疑移除
            membership.LiveNodeIds.Should().NotContain(A);

            // 后端权威把 A 移除：A 应彻底下线（并清理可疑标记，不再依赖健康提示维持）。
            provider.SetNodes(Node(B, 5002));
            provider.RaiseChanged();

            var converged = await SpinAsync(
                () => !membership.TryResolve(A, out _) && !membership.LiveNodeIds.Contains(A) && membership.LiveNodeIds.Contains(B),
                TimeSpan.FromSeconds(3));
            converged.Should().BeTrue();

            // A 重新出现在后端后应恢复存活（此前的可疑标记已被清理，不会残留把它挡在门外）。
            provider.SetNodes(Node(A, 5001), Node(B, 5002));
            provider.RaiseChanged();

            var readmitted = await SpinAsync(() => membership.LiveNodeIds.Contains(A), TimeSpan.FromSeconds(3));
            readmitted.Should().BeTrue("后端重新发现 A 后应恢复存活（旧可疑标记不应残留）");
        }
        finally
        {
            await membership.StopAsync(CancellationToken.None);
            membership.Dispose();
        }
    }

    private static async Task<bool> SpinAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }
}
