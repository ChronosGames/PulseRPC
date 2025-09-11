using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PulseRPC.Server.Threading;

/// <summary>
/// 一致性哈希环 - 用于实现会话亲和性
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly SortedDictionary<uint, int> _ring;
    private readonly int _virtualNodes;
    private readonly int _nodeCount;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="nodeCount">节点数量</param>
    /// <param name="virtualNodes">每个节点的虚拟节点数量</param>
    public ConsistentHashRing(int nodeCount, int virtualNodes = 150)
    {
        if (nodeCount <= 0)
            throw new ArgumentException("节点数量必须大于0", nameof(nodeCount));
        
        if (virtualNodes <= 0)
            throw new ArgumentException("虚拟节点数量必须大于0", nameof(virtualNodes));
        
        _nodeCount = nodeCount;
        _virtualNodes = virtualNodes;
        _ring = new SortedDictionary<uint, int>();
        
        BuildRing();
    }
    
    /// <summary>
    /// 构建哈希环
    /// </summary>
    private void BuildRing()
    {
        for (int nodeId = 0; nodeId < _nodeCount; nodeId++)
        {
            for (int i = 0; i < _virtualNodes; i++)
            {
                var virtualNodeKey = $"Node-{nodeId}-VirtualNode-{i}";
                var hash = ComputeHash(virtualNodeKey);
                _ring[hash] = nodeId;
            }
        }
    }
    
    /// <summary>
    /// 获取指定键对应的节点
    /// </summary>
    /// <param name="key">键</param>
    /// <returns>节点ID</returns>
    public int GetNode(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("键不能为空", nameof(key));
        
        if (_ring.Count == 0)
            throw new InvalidOperationException("哈希环为空");
        
        var hash = ComputeHash(key);
        
        // 查找第一个大于等于hash的节点
        foreach (var kvp in _ring)
        {
            if (kvp.Key >= hash)
            {
                return kvp.Value;
            }
        }
        
        // 如果没找到，返回环中的第一个节点（环形特性）
        using var enumerator = _ring.GetEnumerator();
        enumerator.MoveNext();
        return enumerator.Current.Value;
    }
    
    /// <summary>
    /// 计算哈希值
    /// </summary>
    private static uint ComputeHash(string input)
    {
        using var sha1 = SHA1.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha1.ComputeHash(bytes);
        
        // 取前4个字节转换为uint
        return BitConverter.ToUInt32(hash, 0);
    }
    
    /// <summary>
    /// 获取哈希环统计信息
    /// </summary>
    public HashRingStatistics GetStatistics()
    {
        var nodeDistribution = new Dictionary<int, int>();
        
        for (int i = 0; i < _nodeCount; i++)
        {
            nodeDistribution[i] = 0;
        }
        
        foreach (var nodeId in _ring.Values)
        {
            nodeDistribution[nodeId]++;
        }
        
        return new HashRingStatistics
        {
            NodeCount = _nodeCount,
            VirtualNodes = _virtualNodes,
            TotalVirtualNodes = _ring.Count,
            NodeDistribution = nodeDistribution
        };
    }
}

/// <summary>
/// 哈希环统计信息
/// </summary>
public class HashRingStatistics
{
    public int NodeCount { get; set; }
    public int VirtualNodes { get; set; }
    public int TotalVirtualNodes { get; set; }
    public required Dictionary<int, int> NodeDistribution { get; set; }
}