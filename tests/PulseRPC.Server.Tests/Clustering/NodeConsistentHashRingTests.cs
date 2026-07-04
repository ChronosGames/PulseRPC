using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// 回归测试：<see cref="NodeConsistentHashRing"/> 对任意 Key 的属主解析必须确定且跨环实例一致
/// （§P4：静态成员 + 一致性哈希，全部节点各自构建的哈希环必须对同一 Key 算出相同属主）。
/// </summary>
public class NodeConsistentHashRingTests
{
    [Fact]
    public void GetOwner_SameKey_MustAlwaysResolveToSameNode()
    {
        var ring = new NodeConsistentHashRing(new[] { "node-a", "node-b", "node-c" });

        var first = ring.GetOwner("room-42");
        var second = ring.GetOwner("room-42");

        second.Should().Be(first);
    }

    [Fact]
    public void GetOwner_DifferentRingInstances_WithSameNodes_MustAgreeOnOwner()
    {
        // 模拟集群内每个节点各自独立构建一份哈希环：只要节点集合相同，结果必须一致（无需协调）。
        var ringOnNodeA = new NodeConsistentHashRing(new[] { "node-a", "node-b", "node-c" });
        var ringOnNodeB = new NodeConsistentHashRing(new[] { "node-c", "node-a", "node-b" });

        foreach (var key in new[] { "room-1", "room-2", "actor-xyz", "guild-99" })
        {
            ringOnNodeA.GetOwner(key).Should().Be(ringOnNodeB.GetOwner(key), $"Key='{key}' 在两个环实例上应算出相同属主");
        }
    }

    [Fact]
    public void GetOwner_MustOnlyReturnKnownNodes()
    {
        var nodes = new[] { "node-a", "node-b", "node-c" };
        var ring = new NodeConsistentHashRing(nodes);

        foreach (var key in Enumerable.Range(0, 200).Select(i => $"key-{i}"))
        {
            ring.GetOwner(key).Should().BeOneOf(nodes);
        }
    }

    [Fact]
    public void GetOwner_ManyKeys_MustDistributeAcrossAllNodes()
    {
        var nodes = new[] { "node-a", "node-b", "node-c" };
        var ring = new NodeConsistentHashRing(nodes);

        var counts = new Dictionary<string, int>();
        foreach (var key in Enumerable.Range(0, 3000).Select(i => $"key-{i}"))
        {
            var owner = ring.GetOwner(key);
            counts[owner] = counts.GetValueOrDefault(owner) + 1;
        }

        counts.Keys.Should().BeEquivalentTo(nodes, "大量随机 Key 应覆盖到全部节点");
        counts.Values.Should().OnlyContain(c => c > 0);
    }

    [Fact]
    public void Constructor_EmptyNodeList_MustThrow()
    {
        var act = () => new NodeConsistentHashRing(Array.Empty<string>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_DuplicateAndEmptyEntries_MustBeIgnored()
    {
        var ring = new NodeConsistentHashRing(new[] { "node-a", "node-a", "", "node-b" });

        ring.Nodes.Should().BeEquivalentTo(new[] { "node-a", "node-b" });
    }

    [Fact]
    public void GetOwner_NodeSetChange_OnlyAffectsFractionOfKeys()
    {
        // 一致性哈希的核心特性：新增一个节点后，大部分 Key 的属主应保持不变（少数 Key 迁移到新节点）。
        var before = new NodeConsistentHashRing(new[] { "node-a", "node-b", "node-c" });
        var after = new NodeConsistentHashRing(new[] { "node-a", "node-b", "node-c", "node-d" });

        var keys = Enumerable.Range(0, 1000).Select(i => $"key-{i}").ToArray();
        var unchanged = keys.Count(k => before.GetOwner(k) == after.GetOwner(k));

        // 4 节点中新增 1 个，理论上约 75% 保持不变；放宽阈值以避免哈希分布抖动导致的脆弱断言。
        unchanged.Should().BeGreaterThan((int)(keys.Length * 0.5));
    }
}
