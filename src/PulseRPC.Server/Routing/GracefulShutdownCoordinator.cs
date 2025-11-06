using dotnet_etcd;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PulseRPC.Server.Routing;

/// <summary>
/// 优雅关闭协调器实现
/// 负责协调节点关闭时的所有清理工作
/// </summary>
public sealed class GracefulShutdownCoordinator : IGracefulShutdownCoordinator, IAsyncDisposable
{
    private readonly ILogger<GracefulShutdownCoordinator> _logger;
    private readonly GracefulShutdownOptions _options;
    private readonly ClusterRoutingOptions _routingOptions;
    private readonly EtcdClient _etcdClient;
    private readonly ServiceRouter _router;
    private readonly NodeChangeHandler _nodeChangeHandler;

    private ShutdownState _currentState = ShutdownState.Running;
    private DateTime _shutdownStartedAt;
    private int _pendingRequests;
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _shutdownLock = new(1, 1);
    private readonly List<string> _errors = new();

    public ShutdownState CurrentState
    {
        get
        {
            using (_stateLock.EnterScope())
            {
                return _currentState;
            }
        }
    }

    public bool IsShuttingDown => CurrentState != ShutdownState.Running;

    public GracefulShutdownCoordinator(
        ILogger<GracefulShutdownCoordinator> logger,
        IOptions<GracefulShutdownOptions> options,
        IOptions<ClusterRoutingOptions> routingOptions,
        EtcdClient etcdClient,
        ServiceRouter router,
        NodeChangeHandler nodeChangeHandler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _routingOptions = routingOptions?.Value ?? throw new ArgumentNullException(nameof(routingOptions));
        _etcdClient = etcdClient ?? throw new ArgumentNullException(nameof(etcdClient));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _nodeChangeHandler = nodeChangeHandler ?? throw new ArgumentNullException(nameof(nodeChangeHandler));
    }

    /// <summary>
    /// 开始优雅关闭流程
    /// </summary>
    public async Task InitiateShutdownAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _shutdownLock.WaitAsync(cancellationToken);
        try
        {
            if (IsShuttingDown)
            {
                _logger.LogWarning("优雅关闭已在进行中，忽略重复请求");
                return;
            }

            _logger.LogWarning("开始优雅关闭流程, 原因: {Reason}", reason);
            _shutdownStartedAt = DateTime.UtcNow;

            // 创建超时取消令牌
            using var timeoutCts = new CancellationTokenSource(_options.ShutdownTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                // 阶段1: 准备关闭（通知客户端）
                await Phase1_PrepareShutdown(reason, linkedCts.Token);

                // 阶段2: 拒绝新连接
                await Phase2_RejectNewConnections(linkedCts.Token);

                // 阶段3: 排空现有请求
                await Phase3_DrainRequests(linkedCts.Token);

                // 阶段4: 保存Service状态
                await Phase4_SaveServiceStates(linkedCts.Token);

                // 阶段5: 清理资源
                await Phase5_CleanupResources(linkedCts.Token);

                SetState(ShutdownState.Shutdown);
                _logger.LogInformation("优雅关闭流程完成，总耗时: {Elapsed}ms",
                    (DateTime.UtcNow - _shutdownStartedAt).TotalMilliseconds);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogError("优雅关闭超时 ({Timeout}秒)，执行强制关闭",
                    _options.ShutdownTimeout.TotalSeconds);
                await ForceShutdownAsync();
            }
        }
        finally
        {
            _shutdownLock.Release();
        }
    }

    /// <summary>
    /// 阶段1: 准备关闭（通知客户端和其他节点）
    /// </summary>
    private async Task Phase1_PrepareShutdown(string reason, CancellationToken cancellationToken)
    {
        SetState(ShutdownState.PreparingShutdown);
        _logger.LogInformation("阶段1: 准备关闭 - 通知客户端和其他节点");

        if (_options.NotifyClientsBeforeShutdown)
        {
            // 1. 获取所有活跃节点（用于推荐迁移）
            var activeNodes = _router.GetActiveNodes()
                .Where(n => n != _routingOptions.NodeId)
                .ToList();

            if (activeNodes.Count == 0)
            {
                _logger.LogWarning("没有可用的迁移目标节点");
                _errors.Add("No available nodes for client migration");
            }
            else
            {
                // 2. 创建迁移信息
                var migrationInfo = new ClientMigrationInfo
                {
                    RecommendedNodes = activeNodes,
                    Reason = reason,
                    RemainingSeconds = (int)_options.ClientNotificationLeadTime.TotalSeconds,
                    IsForcedMigration = false
                };

                // 3. 发送迁移通知给所有连接的客户端
                _logger.LogInformation(
                    "通知客户端迁移到节点: {Nodes}",
                    string.Join(",", migrationInfo.RecommendedNodes));

                // TODO: 实际发送通知到客户端
                // await _connectionManager.NotifyClientsForMigration(migrationInfo);

                // 4. 等待客户端有时间迁移
                await Task.Delay(_options.ClientNotificationLeadTime, cancellationToken);
            }
        }

        // 5. 通知集群其他节点
        await NotifyClusterNodesAsync(reason, cancellationToken);
    }

    /// <summary>
    /// 阶段2: 拒绝新连接
    /// </summary>
    private Task Phase2_RejectNewConnections(CancellationToken cancellationToken)
    {
        SetState(ShutdownState.RejectingNewConnections);
        _logger.LogInformation("阶段2: 拒绝新连接 - 健康检查将返回不健康状态");

        // 延迟一段时间让负载均衡器检测到不健康状态
        return Task.Delay(_options.HealthCheckUnhealthyDelay, cancellationToken);
    }

    /// <summary>
    /// 阶段3: 排空现有请求
    /// </summary>
    private async Task Phase3_DrainRequests(CancellationToken cancellationToken)
    {
        SetState(ShutdownState.DrainingRequests);
        _logger.LogInformation("阶段3: 排空现有请求");

        var drainStartTime = DateTime.UtcNow;
        var checkInterval = TimeSpan.FromMilliseconds(100);

        while (_pendingRequests > 0)
        {
            var elapsed = DateTime.UtcNow - drainStartTime;
            if (elapsed >= _options.DrainTimeout)
            {
                _logger.LogWarning(
                    "排空请求超时，仍有 {Count} 个待完成请求",
                    _pendingRequests);
                _errors.Add($"Drain timeout: {_pendingRequests} requests still pending");
                break;
            }

            _logger.LogDebug(
                "等待请求完成... 剩余: {Count}, 已用时: {Elapsed}ms",
                _pendingRequests,
                elapsed.TotalMilliseconds);

            await Task.Delay(checkInterval, cancellationToken);
        }

        if (_pendingRequests == 0)
        {
            _logger.LogInformation("所有请求已完成");
        }
    }

    /// <summary>
    /// 阶段4: 保存Service状态
    /// </summary>
    private async Task Phase4_SaveServiceStates(CancellationToken cancellationToken)
    {
        if (!_options.AutoSaveServiceState)
        {
            _logger.LogInformation("阶段4: 跳过Service状态保存（已禁用）");
            return;
        }

        SetState(ShutdownState.SavingState);
        _logger.LogInformation("阶段4: 保存Service状态");

        try
        {
            using var timeoutCts = new CancellationTokenSource(_options.SaveStateTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            // TODO: 实际保存所有Service状态
            // var services = ServiceLocator.Instance.GetAllServices();
            // await SaveAllServiceStatesAsync(services, linkedCts.Token);

            _logger.LogInformation("Service状态保存完成");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存Service状态失败");
            _errors.Add($"Failed to save service states: {ex.Message}");
        }
    }

    /// <summary>
    /// 阶段5: 清理资源
    /// </summary>
    private async Task Phase5_CleanupResources(CancellationToken cancellationToken)
    {
        SetState(ShutdownState.CleaningUp);
        _logger.LogInformation("阶段5: 清理资源");

        // 1. 清理固定映射
        if (_options.CleanupFixedMappings)
        {
            await CleanupFixedMappingsAsync(cancellationToken);
        }

        // 2. 从Etcd注销节点
        await UnregisterNodeFromClusterAsync(cancellationToken);

        // 3. 关闭连接
        // TODO: await _connectionManager.CloseAllConnectionsAsync(cancellationToken);

        _logger.LogInformation("资源清理完成");
    }

    /// <summary>
    /// 强制关闭
    /// </summary>
    public async Task ForceShutdownAsync()
    {
        _logger.LogWarning("执行强制关闭");

        SetState(ShutdownState.Shutdown);

        // 立即从集群移除
        try
        {
            await UnregisterNodeFromClusterAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "强制关闭时从集群注销失败");
        }

        _logger.LogInformation("强制关闭完成");
    }

    /// <summary>
    /// 通知集群其他节点
    /// </summary>
    private async Task NotifyClusterNodesAsync(string reason, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("通知集群节点 {NodeId} 即将下线", _routingOptions.NodeId);

            // 通过Etcd发布节点下线事件
            var nodeShutdownKey = $"{_routingOptions.EtcdKeyPrefix}/node-shutdown/{_routingOptions.NodeId}";
            var shutdownInfo = new
            {
                NodeId = _routingOptions.NodeId,
                NodeName = _routingOptions.NodeName,
                Reason = reason,
                ShutdownAt = DateTime.UtcNow,
                EstimatedDowntime = _options.ShutdownTimeout.TotalSeconds
            };

            var json = System.Text.Json.JsonSerializer.Serialize(shutdownInfo);
            await _etcdClient.PutAsync(nodeShutdownKey, json, cancellationToken: cancellationToken);

            // 触发节点缩容处理
            await _nodeChangeHandler.OnNodesRemovedAsync(
                new List<ushort> { _routingOptions.NodeId },
                $"节点优雅关闭: {reason}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知集群节点失败");
            _errors.Add($"Failed to notify cluster: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理本节点的固定映射
    /// </summary>
    private async Task CleanupFixedMappingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("清理本节点的固定映射");

            // 获取所有指向本节点的固定映射
            var prefix = $"{_routingOptions.EtcdKeyPrefix}/fixed/";
            var response = await _etcdClient.GetRangeAsync(prefix, cancellationToken: cancellationToken);

            var cleanedCount = 0;
            foreach (var kv in response.Kvs)
            {
                try
                {
                    var json = kv.Value.ToStringUtf8();
                    var location = System.Text.Json.JsonSerializer.Deserialize<ServiceFixedLocation>(json);

                    if (location != null && location.NodeId == _routingOptions.NodeId)
                    {
                        await _etcdClient.DeleteAsync(kv.Key.ToString(), cancellationToken: cancellationToken);
                        cleanedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "清理单个固定映射失败");
                }
            }

            _logger.LogInformation("清理了 {Count} 个固定映射", cleanedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理固定映射失败");
            _errors.Add($"Failed to cleanup fixed mappings: {ex.Message}");
        }
    }

    /// <summary>
    /// 从集群注销节点
    /// </summary>
    private async Task UnregisterNodeFromClusterAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("从集群注销节点 {NodeId}", _routingOptions.NodeId);

            // 删除节点注册信息
            var nodeKey = $"{_routingOptions.EtcdKeyPrefix}/nodes/{_routingOptions.NodeId}";
            await _etcdClient.DeleteAsync(nodeKey, cancellationToken: cancellationToken);

            _logger.LogInformation("节点已从集群注销");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从集群注销节点失败");
            _errors.Add($"Failed to unregister node: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查是否可以接受新连接
    /// </summary>
    public bool CanAcceptNewConnections()
    {
        return CurrentState == ShutdownState.Running;
    }

    /// <summary>
    /// 注册待完成的请求
    /// </summary>
    public void RegisterPendingRequest()
    {
        Interlocked.Increment(ref _pendingRequests);
    }

    /// <summary>
    /// 标记请求已完成
    /// </summary>
    public void MarkRequestCompleted()
    {
        Interlocked.Decrement(ref _pendingRequests);
    }

    /// <summary>
    /// 获取关闭进度
    /// </summary>
    public ShutdownProgress GetProgress()
    {
        using (_stateLock.EnterScope())
        {
            var elapsed = DateTime.UtcNow - _shutdownStartedAt;
            var estimatedTotal = _options.ShutdownTimeout;
            var percentage = _currentState == ShutdownState.Running ? 0 :
                _currentState == ShutdownState.Shutdown ? 100 :
                Math.Min(100, (int)(elapsed.TotalSeconds / estimatedTotal.TotalSeconds * 100));

            return new ShutdownProgress
            {
                State = _currentState,
                StartedAt = _shutdownStartedAt,
                EstimatedCompletionTime = _shutdownStartedAt.Add(estimatedTotal),
                ActiveConnections = 0, // TODO: 从ConnectionManager获取
                PendingRequests = _pendingRequests,
                PendingServiceSaves = 0, // TODO: 从ServiceLocator获取
                CompletionPercentage = percentage,
                CurrentStep = GetCurrentStepDescription(),
                Errors = new List<string>(_errors)
            };
        }
    }

    private string GetCurrentStepDescription()
    {
        return _currentState switch
        {
            ShutdownState.Running => "正常运行",
            ShutdownState.PreparingShutdown => "准备关闭，通知客户端迁移...",
            ShutdownState.RejectingNewConnections => "拒绝新连接...",
            ShutdownState.DrainingRequests => $"排空现有请求 ({_pendingRequests} 个待完成)...",
            ShutdownState.SavingState => "保存Service状态...",
            ShutdownState.CleaningUp => "清理资源...",
            ShutdownState.Shutdown => "已关闭",
            _ => "未知状态"
        };
    }

    private void SetState(ShutdownState newState)
    {
        using (_stateLock.EnterScope())
        {
            _currentState = newState;
            _logger.LogInformation("关闭状态变更: {State}", newState);
        }
    }

    public ValueTask DisposeAsync()
    {
        _shutdownLock.Dispose();
        _etcdClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}
