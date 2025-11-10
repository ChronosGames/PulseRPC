using System.Security.Cryptography;
using System.Text;

namespace DistributedGameApp.BattleServer.Services.Backend;

/// <summary>
/// 一致性哈希算法实现
/// </summary>
/// <typeparam name="T">节点类型</typeparam>
public class ConsistentHash<T> where T : notnull
{
    private readonly SortedDictionary<uint, T> _ring = new();
    private readonly Dictionary<T, HashSet<uint>> _nodeHashes = new();
    private readonly int _virtualNodeCount;
    private readonly object _lock = new();

    /// <summary>
    /// 创建一致性哈希环
    /// </summary>
    /// <param name="virtualNodeCount">每个物理节点对应的虚拟节点数量（增加虚拟节点可以提高负载均衡效果）</param>
    public ConsistentHash(int virtualNodeCount = 150)
    {
        if (virtualNodeCount <= 0)
            throw new ArgumentException("Virtual node count must be positive", nameof(virtualNodeCount));

        _virtualNodeCount = virtualNodeCount;
    }

    /// <summary>
    /// 添加节点到哈希环
    /// </summary>
    public void AddNode(T node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));

        lock (_lock)
        {
            if (_nodeHashes.ContainsKey(node))
            {
                return; // 节点已存在
            }

            var hashes = new HashSet<uint>();

            // 为每个物理节点创建多个虚拟节点
            for (int i = 0; i < _virtualNodeCount; i++)
            {
                var virtualKey = $"{node}:{i}";
                var hash = ComputeHash(virtualKey);

                // 避免哈希冲突
                while (_ring.ContainsKey(hash))
                {
                    hash = (hash + 1) % uint.MaxValue;
                }

                _ring[hash] = node;
                hashes.Add(hash);
            }

            _nodeHashes[node] = hashes;
        }
    }

    /// <summary>
    /// 从哈希环中移除节点
    /// </summary>
    public bool RemoveNode(T node)
    {
        if (node == null)
            throw new ArgumentNullException(nameof(node));

        lock (_lock)
        {
            if (!_nodeHashes.TryGetValue(node, out var hashes))
            {
                return false;
            }

            foreach (var hash in hashes)
            {
                _ring.Remove(hash);
            }

            _nodeHashes.Remove(node);
            return true;
        }
    }

    /// <summary>
    /// 根据 key 获取对应的节点
    /// </summary>
    public T? GetNode(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        lock (_lock)
        {
            if (_ring.Count == 0)
            {
                return default;
            }

            var hash = ComputeHash(key);

            // 查找大于等于该 hash 的第一个节点
            foreach (var kvp in _ring)
            {
                if (kvp.Key >= hash)
                {
                    return kvp.Value;
                }
            }

            // 如果没找到，返回环上的第一个节点（环形）
            return _ring.First().Value;
        }
    }

    /// <summary>
    /// 获取多个节点（用于备份或容错）
    /// </summary>
    public List<T> GetNodes(string key, int count)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (count <= 0)
            throw new ArgumentException("Count must be positive", nameof(count));

        lock (_lock)
        {
            var result = new List<T>();
            var seen = new HashSet<T>();

            if (_ring.Count == 0)
            {
                return result;
            }

            var hash = ComputeHash(key);
            var started = false;

            // 从 hash 位置开始顺时针查找不同的物理节点
            foreach (var kvp in _ring)
            {
                if (!started && kvp.Key < hash)
                    continue;

                started = true;

                if (seen.Add(kvp.Value))
                {
                    result.Add(kvp.Value);
                    if (result.Count >= count)
                    {
                        return result;
                    }
                }
            }

            // 如果还没找够，从头继续查找（环形）
            if (result.Count < count)
            {
                foreach (var kvp in _ring)
                {
                    if (seen.Add(kvp.Value))
                    {
                        result.Add(kvp.Value);
                        if (result.Count >= count)
                        {
                            break;
                        }
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// 获取所有节点
    /// </summary>
    public List<T> GetAllNodes()
    {
        lock (_lock)
        {
            return _nodeHashes.Keys.ToList();
        }
    }

    /// <summary>
    /// 获取节点数量
    /// </summary>
    public int NodeCount
    {
        get
        {
            lock (_lock)
            {
                return _nodeHashes.Count;
            }
        }
    }

    /// <summary>
    /// 清空哈希环
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _ring.Clear();
            _nodeHashes.Clear();
        }
    }

    /// <summary>
    /// 计算字符串的哈希值（使用 MD5）
    /// </summary>
    private static uint ComputeHash(string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key);
        var hashBytes = MD5.HashData(bytes);

        // 使用前 4 个字节作为 hash 值
        return BitConverter.ToUInt32(hashBytes, 0);
    }

    /// <summary>
    /// 获取哈希环的统计信息
    /// </summary>
    public ConsistentHashStats GetStats()
    {
        lock (_lock)
        {
            return new ConsistentHashStats
            {
                NodeCount = _nodeHashes.Count,
                VirtualNodeCount = _ring.Count,
                VirtualNodesPerNode = _virtualNodeCount
            };
        }
    }
}

/// <summary>
/// 一致性哈希统计信息
/// </summary>
public class ConsistentHashStats
{
    /// <summary>
    /// 物理节点数量
    /// </summary>
    public int NodeCount { get; init; }

    /// <summary>
    /// 虚拟节点数量
    /// </summary>
    public int VirtualNodeCount { get; init; }

    /// <summary>
    /// 每个物理节点的虚拟节点数
    /// </summary>
    public int VirtualNodesPerNode { get; init; }
}
