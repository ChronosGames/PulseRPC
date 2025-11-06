using dotnet_etcd;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 节点变化处理器
/// 负责处理集群节点的扩容和缩容，实现零迁移策略
/// </summary>
/// <remarks>
/// <para><strong>核心策略</strong>：</para>
/// <list type="number">
/// <item><description>节点变化时，将所有使用一致性哈希的Service固定到当前节点（避免迁移）</description></item>
/// <item><description>固定映射带TTL（默认24小时），在Service自然下线后自动过期</description></item>
/// <item><description>新创建的Service使用新的哈希环，逐步分布到新节点</description></item>
/// <item><description>不触发主动迁移，由Service生命周期自然过渡</description></item>
/// </list>
/// </remarks>
public sealed class NodeChangeHandler
{
    private readonly ServiceRouter _router;
    private readonly EtcdClient _etcdClient;
    private readonly ILogger<NodeChangeHandler> _logger;
    private readonly ClusterRoutingOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Etcd键路径
    private string HashRingKey => $"{_options.EtcdKeyPrefix}/hashring";
    private string ServiceRegistryPrefix => $"{_options.EtcdKeyPrefix}/services/";

    public NodeChangeHandler(
        ServiceRouter router,
        EtcdClient etcdClient,
        ILogger<NodeChangeHandler> logger,
        IOptions<ClusterRoutingOptions> options)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _etcdClient = etcdClient ?? throw new ArgumentNullException(nameof(etcdClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 处理节点扩容
    /// </summary>
    /// <param name="addedNodes">新增的节点列表</param>
    /// <param name="reason">扩容原因</param>
    public async Task OnNodesAddedAsync(List<ushort> addedNodes, string reason)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogWarning(
                "检测到节点扩容: {Nodes}, 原因: {Reason}",
                string.Join(",", addedNodes), reason);

            // 1. 获取当前所有活跃的Service
            var activeServices = await GetAllActiveServicesAsync();

            _logger.LogInformation(
                "当前活跃Service数量: {Count}, 准备创建固定映射快照",
                activeServices.Count);

            // 2. 为每个一致性哈希的Service创建固定映射
            var fixedCount = 0;
            var now = DateTime.UtcNow;
            var ttl = _options.FixedMappingTTL;

            foreach (var service in activeServices)
            {
                // 只固定原本使用一致性哈希的Service
                if (service.Strategy == ServicePlacementStrategy.ConsistentHash)
                {
                    var currentNode = await _router.LocateServiceAsync(service.ServiceIdHash);

                    var fixedLocation = new ServiceFixedLocation
                    {
                        ServiceIdHash = service.ServiceIdHash,
                        NodeId = currentNode,
                        OriginalStrategy = service.Strategy,
                        FixedAt = now,
                        ExpiresAt = now.Add(ttl),
                        Reason = $"节点扩容: {reason}"
                    };

                    await _router.SetFixedLocationAsync(fixedLocation);
                    fixedCount++;
                }
            }

            // 3. 更新哈希环（新Service将使用新环）
            var currentNodes = _router.GetActiveNodes();
            var newNodes = currentNodes.Union(addedNodes).OrderBy(n => n).ToList();
            await UpdateHashRingAsync(newNodes, addedNodes, new List<ushort>(), reason);

            _logger.LogWarning(
                "节点扩容处理完成: 固定了 {FixedCount} 个Service, TTL={TTL}小时",
                fixedCount, ttl.TotalHours);

            _logger.LogInformation(
                "新创建的Service将使用新的一致性哈希环，包含节点: {Nodes}",
                string.Join(",", newNodes));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 处理节点缩容
    /// </summary>
    /// <param name="removedNodes">移除的节点列表</param>
    /// <param name="reason">缩容原因</param>
    public async Task OnNodesRemovedAsync(List<ushort> removedNodes, string reason)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogWarning(
                "检测到节点缩容: {Nodes}, 原因: {Reason}",
                string.Join(",", removedNodes), reason);

            // 1. 获取当前所有活跃的Service
            var activeServices = await GetAllActiveServicesAsync();

            // 2. 为每个一致性哈希的Service创建固定映射
            var fixedCount = 0;
            var affectedCount = 0;
            var now = DateTime.UtcNow;
            var ttl = _options.FixedMappingTTL;

            foreach (var service in activeServices)
            {
                if (service.Strategy == ServicePlacementStrategy.ConsistentHash)
                {
                    var currentNode = await _router.LocateServiceAsync(service.ServiceIdHash);

                    // 如果Service在被移除的节点上，需要特殊处理
                    if (removedNodes.Contains(currentNode))
                    {
                        affectedCount++;

                        // 策略：让这些Service自然下线（推荐）
                        _logger.LogWarning(
                            "Service ServiceIdHash={Hash:X16} 在被移除的节点 {NodeId} 上，将自然下线",
                            service.ServiceIdHash, currentNode);

                        // 可选策略：如果需要立即可用，可以在新节点重建（不推荐，因为有状态）
                        // var newNode = CalculateNewNode(service.ServiceIdHash, removedNodes);
                        // await RecreateServiceOnNodeAsync(service.ServiceIdHash, newNode);
                    }
                    else
                    {
                        // Service不在被移除的节点上，固定其位置
                        var fixedLocation = new ServiceFixedLocation
                        {
                            ServiceIdHash = service.ServiceIdHash,
                            NodeId = currentNode,
                            OriginalStrategy = service.Strategy,
                            FixedAt = now,
                            ExpiresAt = now.Add(ttl),
                            Reason = $"节点缩容: {reason}"
                        };

                        await _router.SetFixedLocationAsync(fixedLocation);
                        fixedCount++;
                    }
                }
            }

            // 3. 更新哈希环
            var currentNodes = _router.GetActiveNodes();
            var newNodes = currentNodes.Except(removedNodes).OrderBy(n => n).ToList();
            await UpdateHashRingAsync(newNodes, new List<ushort>(), removedNodes, reason);

            _logger.LogWarning(
                "节点缩容处理完成: 固定了 {FixedCount} 个Service, 受影响的Service={AffectedCount} (将自然下线)",
                fixedCount, affectedCount);

            _logger.LogInformation(
                "新创建的Service将使用新的一致性哈希环，包含节点: {Nodes}",
                string.Join(",", newNodes));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 手动触发集群重新平衡（在所有固定映射过期后）
    /// </summary>
    public async Task RebalanceClusterAsync(string reason)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("开始集群重新平衡, 原因: {Reason}", reason);

            // 获取所有固定映射的数量
            var fixedMappings = await GetAllFixedMappingsAsync();

            if (fixedMappings.Count > 0)
            {
                _logger.LogWarning(
                    "仍有 {Count} 个固定映射未过期，建议等待自然过期后再重新平衡",
                    fixedMappings.Count);
            }

            // 更新哈希环（不改变节点，只是刷新版本）
            var currentNodes = _router.GetActiveNodes();
            await UpdateHashRingAsync(currentNodes, new List<ushort>(), new List<ushort>(), reason);

            _logger.LogInformation("集群重新平衡完成");
        }
        finally
        {
            _lock.Release();
        }
    }

    // ========================================
    // 私有辅助方法
    // ========================================

    /// <summary>
    /// 获取所有活跃的Service（从本地ServiceLocator）
    /// </summary>
    private async Task<List<ActiveServiceInfo>> GetAllActiveServicesAsync()
    {
        // 这里需要访问本地的ServiceLocator来获取活跃Service
        // 由于ServiceLocator是单例，我们可以直接访问

        var services = new List<ActiveServiceInfo>();

        // 从ServiceLocator获取所有服务
        var locator = ServiceLocator.Instance;

        // 这里简化处理，实际应该遍历所有注册的服务类型
        // 由于我们没有直接访问内部存储的方法，需要添加一个公共API

        // 临时方案：返回空列表，实际使用时需要扩展ServiceLocator
        _logger.LogWarning("GetAllActiveServicesAsync需要ServiceLocator提供GetAllActiveServices()方法");

        return services;
    }

    /// <summary>
    /// 获取所有固定映射
    /// </summary>
    private async Task<List<ServiceFixedLocation>> GetAllFixedMappingsAsync()
    {
        try
        {
            var prefix = $"{_options.EtcdKeyPrefix}/fixed/";
            var response = await _etcdClient.GetRangeAsync(prefix);

            var mappings = new List<ServiceFixedLocation>();

            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var location = JsonSerializer.Deserialize<ServiceFixedLocation>(json);
                    if (location != null)
                    {
                        mappings.Add(location);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "反序列化固定映射失败");
                }
            }

            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取所有固定映射失败");
            return new List<ServiceFixedLocation>();
        }
    }

    /// <summary>
    /// 更新哈希环并发布到Etcd
    /// </summary>
    private async Task UpdateHashRingAsync(
        List<ushort> newNodes,
        List<ushort> addedNodes,
        List<ushort> removedNodes,
        string reason)
    {
        try
        {
            var snapshot = new HashRingSnapshot
            {
                Version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                CreatedAt = DateTime.UtcNow,
                ActiveNodes = newNodes,
                UseFixedMapping = addedNodes.Count > 0 || removedNodes.Count > 0,
                ChangeReason = reason,
                AddedNodes = addedNodes,
                RemovedNodes = removedNodes
            };

            var json = JsonSerializer.Serialize(snapshot);
            await _etcdClient.PutAsync(HashRingKey, json);

            _logger.LogInformation(
                "哈希环已更新: 版本={Version}, 节点={Nodes}, 原因={Reason}",
                snapshot.Version,
                string.Join(",", newNodes),
                reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新哈希环失败");
            throw;
        }
    }

    /// <summary>
    /// 获取范围查询的结束键
    /// </summary>
    private static string GetRangeEnd(string key)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(key);
        var lastByte = bytes[^1];

        if (lastByte < 0xFF)
        {
            bytes[^1] = (byte)(lastByte + 1);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        return key + "~";
    }
}

/// <summary>
/// 活跃Service信息（用于内部查询）
/// </summary>
internal class ActiveServiceInfo
{
    public ulong ServiceIdHash { get; set; }
    public ServicePlacementStrategy Strategy { get; set; }
}
