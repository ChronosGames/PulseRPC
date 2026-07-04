using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// P8/P7：<see cref="StaticClusterMembership"/> —— 静态成员 + 失败累计下线 + 成功/隔离到期恢复，
/// 为集群路由的故障接管提供"存活成员"依据。
/// </summary>
public class StaticClusterMembershipTests
{
    private const string Local = "node-local";
    private const string A = "node-a";
    private const string B = "node-b";

    private static StaticClusterMembership Create(
        int failureThreshold = 3,
        TimeSpan? quarantine = null,
        string localNodeId = Local)
    {
        var topology = new ClusterTopologyOptions
        {
            LocalNodeId = localNodeId,
            Members =
            {
                new ClusterNodeEndpoint { NodeId = localNodeId, Host = "127.0.0.1", Port = 5000 },
                new ClusterNodeEndpoint { NodeId = A, Host = "127.0.0.1", Port = 5001 },
                new ClusterNodeEndpoint { NodeId = B, Host = "127.0.0.1", Port = 5002 },
            },
        };
        var membershipOptions = new StaticClusterMembershipOptions
        {
            FailureThreshold = failureThreshold,
            QuarantineDuration = quarantine ?? TimeSpan.FromSeconds(30),
        };
        return new StaticClusterMembership(Options.Create(topology), Options.Create(membershipOptions));
    }

    [Fact]
    public void LiveNodeIds_Initially_MustContainAllConfiguredMembers()
    {
        using var membership = Create();

        membership.LiveNodeIds.Should().BeEquivalentTo(new[] { Local, A, B });
    }

    [Fact]
    public void ReportNodeFailure_BelowThreshold_MustNotEvict()
    {
        using var membership = Create(failureThreshold: 3);

        membership.ReportNodeFailure(A);
        membership.ReportNodeFailure(A);

        membership.LiveNodeIds.Should().Contain(A);
    }

    [Fact]
    public void ReportNodeFailure_ReachingThreshold_MustEvictAndRaiseChanged()
    {
        using var membership = Create(failureThreshold: 3);
        var changedCount = 0;
        membership.Changed += () => Interlocked.Increment(ref changedCount);

        membership.ReportNodeFailure(A);
        membership.ReportNodeFailure(A);
        membership.ReportNodeFailure(A);

        membership.LiveNodeIds.Should().NotContain(A);
        membership.LiveNodeIds.Should().BeEquivalentTo(new[] { Local, B });
        changedCount.Should().Be(1);
    }

    [Fact]
    public void ReportNodeSuccess_AfterEviction_MustReinstateAndRaiseChanged()
    {
        using var membership = Create(failureThreshold: 1);
        var changedCount = 0;
        membership.Changed += () => Interlocked.Increment(ref changedCount);

        membership.ReportNodeFailure(A); // threshold=1 -> evicted immediately
        membership.LiveNodeIds.Should().NotContain(A);

        membership.ReportNodeSuccess(A);

        membership.LiveNodeIds.Should().Contain(A);
        changedCount.Should().Be(2); // one for eviction, one for reinstatement
    }

    [Fact]
    public void ReportNodeFailure_MustNeverEvictLocalNode()
    {
        using var membership = Create(failureThreshold: 1);

        membership.ReportNodeFailure(Local);
        membership.ReportNodeFailure(Local);

        membership.LiveNodeIds.Should().Contain(Local);
    }

    [Fact]
    public void ReportNodeFailure_ForUnknownNode_MustBeIgnored()
    {
        using var membership = Create(failureThreshold: 1);

        membership.ReportNodeFailure("node-not-in-cluster");

        membership.LiveNodeIds.Should().BeEquivalentTo(new[] { Local, A, B });
    }

    [Fact]
    public void QuarantineExpiry_MustReadmitEvictedNode()
    {
        using var membership = Create(failureThreshold: 1, quarantine: TimeSpan.FromMilliseconds(200));

        membership.ReportNodeFailure(A);
        membership.LiveNodeIds.Should().NotContain(A);

        // 隔离到期（200ms）后由内部计时器（周期 = clamp(quarantine/2, [1s,30s]) = 1s）半开重新纳入。
        var readmitted = SpinUntil(() => membership.LiveNodeIds.Contains(A), TimeSpan.FromSeconds(5));

        readmitted.Should().BeTrue("隔离到期后节点应被半开重新纳入存活集");
    }

    private static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }
}
