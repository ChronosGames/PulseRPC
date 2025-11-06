using dotnet_etcd;
using Etcdserverpb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace PulseRPC.Server.Routing;

/// <summary>
/// Service路由器实现
/// 支持固定映射优先策略，零迁移扩缩容方案
/// </summary>
public sealed class ServiceRouter : IServiceRouter, IAsyncDisposable
{
    private readonly EtcdClient _etcdClient;
    private readonly ILogger<ServiceRouter> _logger;
    private readonly ClusterRoutingOptions _options;
    private readonly NodeConsistentHashRing _hashRing;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // 路由指标
    private long _consistentHashRouteCount;
    private long _fixedMappingRouteCount;
    private long _currentHashRingVersion;

    // Etcd键路径
    private string HashRingKey => $"{_options.EtcdKeyPrefix}/hashring";
    private string FixedMappingPrefix => $"{_options.EtcdKeyPrefix}/fixed/";

    // 监听哈希环变化的任务
    private Task? _watchHashRingTask;
    private CancellationTokenSource? _watchCts;

    public ServiceRouter(
        EtcdClient etcdClient,
        ILogger<ServiceRouter> logger,
        IOptions<ClusterRoutingOptions> options)
    {
        _etcdClient = etcdClient ?? throw new ArgumentNullException(nameof(etcdClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _hashRing = new NodeConsistentHashRing(_options.VirtualNodesPerNode);
    }

    /// <summary>
    /// 初始化路由器（加载哈希环，启动监听）
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("初始化ServiceRouter，节点ID: {NodeId}", _options.NodeId);

        // 1. 从Etcd加载当前哈希环
        await LoadHashRingFromEtcdAsync(cancellationToken);

        // 2. 启动监听哈希环变化
        StartWatchingHashRing();

        _logger.LogInformation("ServiceRouter初始化完成，活跃节点数: {Count}", _hashRing.ActiveNodeCount);
    }

    /// <summary>
    /// 定位Service（固定映射优先，否则使用一致性哈希）
    /// </summary>
    public async Task<ushort> LocateServiceAsync(ulong serviceIdHash)
    {
        if (!_options.EnableFixedMapping)
        {
            // 如果未启用固定映射，直接使用一致性哈希
            Interlocked.Increment(ref _consistentHashRouteCount);
            return _hashRing.GetNode(serviceIdHash);
        }

        // 1. 优先查找固定映射
        var fixedLocation = await GetFixedLocationAsync(serviceIdHash);
        if (fixedLocation != null)
        {
            // 检查是否过期
            if (!fixedLocation.IsExpired())
            {
                Interlocked.Increment(ref _fixedMappingRouteCount);

                _logger.LogDebug(
                    "ServiceIdHash={Hash:X16} 使用固定映射 -> 节点 {NodeId} (过期时间: {Expires}, 剩余: {Remaining})",
                    serviceIdHash,
                    fixedLocation.NodeId,
                    fixedLocation.ExpiresAt,
                    fixedLocation.RemainingTime());

                return fixedLocation.NodeId;
            }
            else
            {
                // 过期了，删除映射
                _logger.LogInformation(
                    "ServiceIdHash={Hash:X16} 固定映射已过期，删除并使用一致性哈希",
                    serviceIdHash);

                await RemoveFixedLocationAsync(serviceIdHash);
            }
        }

        // 2. 使用一致性哈希
        Interlocked.Increment(ref _consistentHashRouteCount);
        var nodeId = _hashRing.GetNode(serviceIdHash);

        _logger.LogDebug(
            "ServiceIdHash={Hash:X16} 使用一致性哈希 -> 节点 {NodeId}",
            serviceIdHash,
            nodeId);

        return nodeId;
    }

    /// <summary>
    /// 为新Service选择节点（总是使用最新的一致性哈希环）
    /// </summary>
    public Task<ushort> SelectNodeForNewServiceAsync(ulong serviceIdHash)
    {
        // 新Service直接使用一致性哈希，不考虑固定映射
        Interlocked.Increment(ref _consistentHashRouteCount);
        var nodeId = _hashRing.GetNode(serviceIdHash);

        _logger.LogDebug(
            "新Service ServiceIdHash={Hash:X16} 创建在节点 {NodeId} (使用一致性哈希)",
            serviceIdHash,
            nodeId);

        return Task.FromResult(nodeId);
    }

    /// <summary>
    /// Service下线时清理固定映射
    /// </summary>
    public async Task OnServiceShutdownAsync(ulong serviceIdHash)
    {
        var removed = await RemoveFixedLocationAsync(serviceIdHash);
        if (removed)
        {
            _logger.LogInformation(
                "Service ServiceIdHash={Hash:X16} 下线，已清理固定映射",
                serviceIdHash);
        }
    }

    /// <summary>
    /// 获取活跃节点列表
    /// </summary>
    public List<ushort> GetActiveNodes()
    {
        return _hashRing.GetActiveNodes();
    }

    /// <summary>
    /// 获取路由指标
    /// </summary>
    public ServiceRoutingMetrics GetMetrics()
    {
        return new ServiceRoutingMetrics
        {
            ConsistentHashRouteCount = Interlocked.Read(ref _consistentHashRouteCount),
            FixedMappingRouteCount = Interlocked.Read(ref _fixedMappingRouteCount),
            HashRingVersion = Interlocked.Read(ref _currentHashRingVersion),
            ActiveNodeCount = _hashRing.ActiveNodeCount
        };
    }

    // ========================================
    // 内部方法：Etcd操作
    // ========================================

    /// <summary>
    /// 从Etcd获取固定映射
    /// </summary>
    internal async Task<ServiceFixedLocation?> GetFixedLocationAsync(ulong serviceIdHash)
    {
        try
        {
            var key = $"{FixedMappingPrefix}{serviceIdHash:X16}";
            var response = await _etcdClient.GetAsync(key);

            if (response == null || response.Count == 0)
            {
                return null;
            }

            var json = response.Kvs[0].Value?.ToStringUtf8();
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            return JsonSerializer.Deserialize<ServiceFixedLocation>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取固定映射失败: ServiceIdHash={Hash:X16}", serviceIdHash);
            return null;
        }
    }

    /// <summary>
    /// 设置固定映射（带TTL）
    /// </summary>
    internal async Task SetFixedLocationAsync(ServiceFixedLocation location)
    {
        try
        {
            var key = $"{FixedMappingPrefix}{location.ServiceIdHash:X16}";
            var json = JsonSerializer.Serialize(location);

            // 计算Etcd的TTL（秒）
            var ttlSeconds = (long)(location.ExpiresAt - DateTime.UtcNow).TotalSeconds;
            if (ttlSeconds <= 0)
            {
                _logger.LogWarning("固定映射TTL已过期，跳过设置");
                return;
            }

            // 使用Etcd的lease机制设置TTL
            var leaseResponse = await _etcdClient.LeaseGrantAsync(new LeaseGrantRequest
            {
                TTL = ttlSeconds
            });

            var putRequest = new PutRequest
            {
                Key = Google.Protobuf.ByteString.CopyFromUtf8(key),
                Value = Google.Protobuf.ByteString.CopyFromUtf8(json),
                Lease = leaseResponse.ID
            };
            await _etcdClient.PutAsync(putRequest);

            _logger.LogInformation(
                "设置固定映射: ServiceIdHash={Hash:X16} -> 节点 {NodeId}, TTL={TTL}小时, 原因: {Reason}",
                location.ServiceIdHash,
                location.NodeId,
                (location.ExpiresAt - location.FixedAt).TotalHours,
                location.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置固定映射失败: ServiceIdHash={Hash:X16}", location.ServiceIdHash);
            throw;
        }
    }

    /// <summary>
    /// 删除固定映射
    /// </summary>
    internal async Task<bool> RemoveFixedLocationAsync(ulong serviceIdHash)
    {
        try
        {
            var key = $"{FixedMappingPrefix}{serviceIdHash:X16}";
            await _etcdClient.DeleteAsync(key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除固定映射失败: ServiceIdHash={Hash:X16}", serviceIdHash);
            return false;
        }
    }

    /// <summary>
    /// 从Etcd加载哈希环
    /// </summary>
    private async Task LoadHashRingFromEtcdAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _etcdClient.GetAsync(HashRingKey, cancellationToken: cancellationToken);

            if (response == null || response.Count == 0)
            {
                _logger.LogWarning("Etcd中未找到哈希环配置，使用空环");
                return;
            }

            var json = response.Kvs[0].Value.ToStringUtf8();
            var snapshot = JsonSerializer.Deserialize<HashRingSnapshot>(json);

            if (snapshot != null)
            {
                _hashRing.Rebuild(snapshot.ActiveNodes);
                Interlocked.Exchange(ref _currentHashRingVersion, snapshot.Version);

                _logger.LogInformation(
                    "从Etcd加载哈希环: 版本={Version}, 节点数={Count}, 固定模式={UseFixed}",
                    snapshot.Version,
                    snapshot.ActiveNodes.Count,
                    snapshot.UseFixedMapping);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从Etcd加载哈希环失败");
            throw;
        }
    }

    /// <summary>
    /// 启动监听哈希环变化
    /// </summary>
    private void StartWatchingHashRing()
    {
        _watchCts = new CancellationTokenSource();
        _watchHashRingTask = Task.Run(async () =>
        {
            await WatchHashRingChangesAsync(_watchCts.Token);
        });

        _logger.LogInformation("开始监听哈希环变化");
    }

    /// <summary>
    /// 监听哈希环变化（使用轮询方式）
    /// </summary>
    private async Task WatchHashRingChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pollInterval = TimeSpan.FromSeconds(10);  // 每10秒轮询一次
            _logger.LogInformation("开始轮询哈希环变化，间隔: {Interval}秒", pollInterval.TotalSeconds);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var response = await _etcdClient.GetAsync(HashRingKey, cancellationToken: cancellationToken);

                    if (response != null && response.Count > 0)
                    {
                        var json = response.Kvs[0].Value.ToStringUtf8();
                        var snapshot = JsonSerializer.Deserialize<HashRingSnapshot>(json);

                        if (snapshot != null && snapshot.Version != Interlocked.Read(ref _currentHashRingVersion))
                        {
                            await OnHashRingChangedAsync(snapshot);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "轮询哈希环变化失败");
                }

                await Task.Delay(pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("停止监听哈希环变化");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "监听哈希环变化失败");
        }
    }

    /// <summary>
    /// 处理哈希环变化事件
    /// </summary>
    private async Task OnHashRingChangedAsync(HashRingSnapshot snapshot)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation(
                "收到哈希环更新: 版本={Version}, 节点数={Count}, 原因={Reason}",
                snapshot.Version,
                snapshot.ActiveNodes.Count,
                snapshot.ChangeReason);

            // 重建哈希环
            _hashRing.Rebuild(snapshot.ActiveNodes);
            Interlocked.Exchange(ref _currentHashRingVersion, snapshot.Version);

            _logger.LogInformation(
                "哈希环已更新: 活跃节点={Nodes}, 固定模式={UseFixed}",
                string.Join(",", snapshot.ActiveNodes),
                snapshot.UseFixedMapping);

            if (snapshot.AddedNodes.Count > 0)
            {
                _logger.LogInformation("新增节点: {Nodes}", string.Join(",", snapshot.AddedNodes));
            }

            if (snapshot.RemovedNodes.Count > 0)
            {
                _logger.LogWarning("移除节点: {Nodes}", string.Join(",", snapshot.RemovedNodes));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("ServiceRouter正在释放...");

        // 停止监听
        _watchCts?.Cancel();
        if (_watchHashRingTask != null)
        {
            try
            {
                await _watchHashRingTask;
            }
            catch (OperationCanceledException)
            {
                // 预期的取消
            }
        }

        _watchCts?.Dispose();
        _lock.Dispose();
        _etcdClient?.Dispose();

        _logger.LogInformation("ServiceRouter已释放");
    }
}
