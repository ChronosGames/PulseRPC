using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 三层服务路由器 - 实现三层寻址机制
/// 1. 本地注册表（同进程服务）
/// 2. 例外缓存表（已知的远程服务）
/// 3. 哈希环（一致性哈希路由）
/// </summary>
public class ThreeTierServiceRouter<TConnection> where TConnection : class
{
    private readonly ILogger _logger;

    // 第一层：本地注册表（PID -> Connection）
    private readonly ConcurrentDictionary<ServicePID, TConnection> _localRegistry = new();

    // 第二层：例外缓存表（ServiceId -> Connection）
    private readonly ConcurrentDictionary<string, TConnection> _exceptionCache = new();

    // 第三层：一致性哈希环
    private readonly ConsistentHash<string> _consistentHash;
    private readonly ConcurrentDictionary<string, TConnection> _hashRingConnections = new();

    // 配置选项
    private readonly ThreeTierRouterOptions _options;

    public ThreeTierServiceRouter(
        ILogger logger,
        ThreeTierRouterOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new ThreeTierRouterOptions();
        _consistentHash = new ConsistentHash<string>(_options.VirtualNodeCount);
    }

    /// <summary>
    /// 注册本地服务（第一层）
    /// </summary>
    public void RegisterLocal(ServicePID pid, TConnection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        _localRegistry[pid] = connection;

        _logger.LogInformation("本地服务已注册: PID={Pid}, IsSingleton={IsSingleton}",
            pid.GetFullPID(), pid.IsSingleton);
    }

    /// <summary>
    /// 注册远程服务到哈希环（第三层）
    /// </summary>
    public void RegisterRemote(string serviceId, TConnection connection)
    {
        if (string.IsNullOrEmpty(serviceId))
            throw new ArgumentException("ServiceId cannot be null or empty", nameof(serviceId));

        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        // 添加到哈希环
        if (!_hashRingConnections.ContainsKey(serviceId))
        {
            _consistentHash.AddNode(serviceId);
            _logger.LogInformation("远程服务已添加到哈希环: ServiceId={ServiceId}", serviceId);
        }

        _hashRingConnections[serviceId] = connection;
    }

    /// <summary>
    /// 添加到例外缓存（第二层）
    /// </summary>
    public void CacheException(string key, TConnection connection)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Cache key cannot be null or empty", nameof(key));

        if (connection == null)
            throw new ArgumentNullException(nameof(connection));

        _exceptionCache[key] = connection;

        // 自动清理缓存（防止无限增长）
        if (_exceptionCache.Count > _options.MaxCacheSize)
        {
            CleanupCache();
        }

        _logger.LogDebug("服务已添加到例外缓存: Key={Key}", key);
    }

    /// <summary>
    /// 三层路由查找
    /// </summary>
    public TConnection? Route(ServicePID pid, string? fallbackKey = null)
    {
        // 第一层：本地注册表查找
        if (_localRegistry.TryGetValue(pid, out var localConnection))
        {
            _logger.LogDebug("本地注册表命中: PID={Pid}", pid.GetFullPID());
            return localConnection;
        }

        // 第二层：例外缓存表查找
        var cacheKey = fallbackKey ?? pid.GetRoutingKey();
        if (_exceptionCache.TryGetValue(cacheKey, out var cachedConnection))
        {
            _logger.LogDebug("例外缓存命中: Key={Key}", cacheKey);
            return cachedConnection;
        }

        // 第三层：一致性哈希环查找（如果启用）
        if (_options.EnableConsistentHash)
        {
            var routingKey = pid.GetRoutingKey();
            var serviceId = _consistentHash.GetNode(routingKey);

            if (serviceId != null && _hashRingConnections.TryGetValue(serviceId, out var hashConnection))
            {
                _logger.LogDebug("一致性哈希命中: RoutingKey={RoutingKey}, ServiceId={ServiceId}",
                    routingKey, serviceId);

                // 自动添加到例外缓存，加速后续查找
                if (_options.AutoCache)
                {
                    CacheException(cacheKey, hashConnection);
                }

                return hashConnection;
            }
        }

        _logger.LogWarning("路由失败: PID={Pid}, FallbackKey={FallbackKey}", pid.GetFullPID(), fallbackKey);
        return null;
    }

    /// <summary>
    /// 移除本地服务
    /// </summary>
    public void UnregisterLocal(ServicePID pid)
    {
        if (_localRegistry.TryRemove(pid, out _))
        {
            _logger.LogInformation("本地服务已注销: PID={Pid}", pid.GetFullPID());
        }
    }

    /// <summary>
    /// 移除远程服务
    /// </summary>
    public void UnregisterRemote(string serviceId)
    {
        if (_hashRingConnections.TryRemove(serviceId, out _))
        {
            _consistentHash.RemoveNode(serviceId);
            _logger.LogInformation("远程服务已从哈希环移除: ServiceId={ServiceId}", serviceId);
        }
    }

    /// <summary>
    /// 清理例外缓存（移除最旧的一半条目）
    /// </summary>
    private void CleanupCache()
    {
        var entriesToRemove = _exceptionCache.Count / 2;
        var removed = 0;

        foreach (var key in _exceptionCache.Keys)
        {
            if (_exceptionCache.TryRemove(key, out _))
            {
                removed++;
                if (removed >= entriesToRemove)
                    break;
            }
        }

        _logger.LogInformation("例外缓存已清理: 移除 {Removed} 个条目", removed);
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ThreeTierRouterStats GetStats()
    {
        var hashStats = _consistentHash.GetStats();

        return new ThreeTierRouterStats
        {
            LocalRegistryCount = _localRegistry.Count,
            ExceptionCacheCount = _exceptionCache.Count,
            HashRingNodeCount = hashStats.NodeCount,
            HashRingVirtualNodeCount = hashStats.VirtualNodeCount,
            EnableConsistentHash = _options.EnableConsistentHash
        };
    }

    /// <summary>
    /// 清空所有注册
    /// </summary>
    public void Clear()
    {
        _localRegistry.Clear();
        _exceptionCache.Clear();
        _hashRingConnections.Clear();
        _consistentHash.Clear();

        _logger.LogInformation("三层路由器已清空");
    }
}

/// <summary>
/// 三层路由器配置选项
/// </summary>
public class ThreeTierRouterOptions
{
    /// <summary>
    /// 是否启用一致性哈希（第三层）
    /// </summary>
    public bool EnableConsistentHash { get; set; } = true;

    /// <summary>
    /// 虚拟节点数量
    /// </summary>
    public int VirtualNodeCount { get; set; } = 150;

    /// <summary>
    /// 例外缓存最大容量
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// 是否自动缓存哈希环查找结果到例外缓存
    /// </summary>
    public bool AutoCache { get; set; } = true;
}

/// <summary>
/// 三层路由器统计信息
/// </summary>
public class ThreeTierRouterStats
{
    /// <summary>
    /// 本地注册表条目数
    /// </summary>
    public int LocalRegistryCount { get; init; }

    /// <summary>
    /// 例外缓存条目数
    /// </summary>
    public int ExceptionCacheCount { get; init; }

    /// <summary>
    /// 哈希环节点数
    /// </summary>
    public int HashRingNodeCount { get; init; }

    /// <summary>
    /// 哈希环虚拟节点数
    /// </summary>
    public int HashRingVirtualNodeCount { get; init; }

    /// <summary>
    /// 是否启用一致性哈希
    /// </summary>
    public bool EnableConsistentHash { get; init; }

    public override string ToString() =>
        $"Local={LocalRegistryCount}, Cache={ExceptionCacheCount}, HashRing={HashRingNodeCount} nodes ({HashRingVirtualNodeCount} virtual), ConsistentHash={EnableConsistentHash}";
}
