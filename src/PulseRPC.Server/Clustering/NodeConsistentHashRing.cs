using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Text;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 面向集群节点的一致性哈希环 —— 把任意 <c>(Hub, Key)</c> 字符串键确定性地映射到某个节点标识。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="PulseRPC.Server.Services.Scheduling.ConsistentHashRing"/>（线程索引版）思路一致，
/// 但节点标识为任意字符串（集群成员 NodeId），且节点集合在「静态成员」部署下于启动时固定。
/// 同一 Key 在所有节点上算出的属主节点相同（无需协调），P4 首版据此免去跨节点候选协商。
/// </para>
/// </remarks>
public sealed class NodeConsistentHashRing
{
    private readonly SortedDictionary<ulong, string> _ring = new();

    /// <summary>参与哈希环的全部节点标识（按字典序排列，便于测试断言）。</summary>
    public IReadOnlyList<string> Nodes { get; }

    /// <summary>
    /// 创建节点一致性哈希环。
    /// </summary>
    /// <param name="nodeIds">集群全部节点标识（至少一个）。</param>
    /// <param name="virtualNodesPerNode">每个节点的虚拟节点数量（默认 150，越大分布越均匀）。</param>
    public NodeConsistentHashRing(IEnumerable<string> nodeIds, int virtualNodesPerNode = 150)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        var nodes = nodeIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (nodes.Length == 0)
        {
            throw new ArgumentException("集群哈希环至少需要一个有效节点标识", nameof(nodeIds));
        }

        if (virtualNodesPerNode < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualNodesPerNode), virtualNodesPerNode, "虚拟节点数量必须大于 0");
        }

        Nodes = nodes;

        foreach (var nodeId in nodes)
        {
            for (var v = 0; v < virtualNodesPerNode; v++)
            {
                var hash = ComputeHash($"{nodeId}#{v}");
                // 极小概率的虚拟节点哈希碰撞：后写覆盖前写即可，不影响正确性（仍会有 nodes.Length*vnodes 量级的分布）
                _ring[hash] = nodeId;
            }
        }
    }

    /// <summary>
    /// 计算某个 Key 的属主节点标识（顺时针查找，环绕到第一个虚拟节点）。
    /// </summary>
    public string GetOwner(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var hash = ComputeHash(key);
        foreach (var kvp in _ring)
        {
            if (kvp.Key >= hash)
            {
                return kvp.Value;
            }
        }

        return _ring.First().Value;
    }

    private static ulong ComputeHash(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        return XxHash64.HashToUInt64(bytes);
    }
}
