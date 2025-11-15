using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Consul;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// Service 例外管理器 - 处理扩缩容时的 Service 迁移例外
/// </summary>
/// <remarks>
/// <para><strong>使用场景</strong>：</para>
/// <para>扩缩容时，正在运行的 Service（有在线玩家）不能立即迁移到新的哈希节点，</para>
/// <para>需要标记为"例外"，并同步到 Consul，让其他节点能够路由到正确的节点。</para>
/// <para></para>
/// <para><strong>工作流程</strong>：</para>
/// <list type="number">
/// <item><description>扩容后，哈希环重新分布：玩家123 原在 Node1，新哈希计算 -> Node2</description></item>
/// <item><description>但玩家123还在线，Service 还在 Node1 上运行</description></item>
/// <item><description>Node1 检测到这个例外，将 "玩家123 -> Node1" 写入 Consul</description></item>
/// <item><description>其他节点从 Consul 读取例外表，路由玩家123的请求到 Node1</description></item>
/// <item><description>玩家123下线后，Node1 从 Consul 删除这条例外记录</description></item>
/// <item><description>玩家123下次上线时，正常使用新的哈希计算（Node2）</description></item>
/// </list>
/// </remarks>
public class ServiceExceptionManager : IAsyncDisposable
{
    private readonly IConsulClient _consulClient;
    private readonly ILogger<ServiceExceptionManager> _logger;
    private readonly string _basePath;
    private readonly string _currentNodeId;

    // 本地例外缓存：ServiceId -> NodeId
    private readonly ConcurrentDictionary<string, string> _localExceptions = new();

    // 全局例外缓存（从 Consul 同步）：ServiceId -> NodeId
    private readonly ConcurrentDictionary<string, string> _globalExceptions = new();

    private readonly Timer? _syncTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _isDisposed;

    public ServiceExceptionManager(
        IConsulClient consulClient,
        ILogger<ServiceExceptionManager> logger,
        string basePath = "pulserpc/exceptions",
        string? currentNodeId = null,
        TimeSpan? syncInterval = null)
    {
        _consulClient = consulClient ?? throw new ArgumentNullException(nameof(consulClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _basePath = basePath;
        _currentNodeId = currentNodeId ?? Environment.MachineName;

        // 定期从 Consul 同步全局例外表
        var interval = syncInterval ?? TimeSpan.FromSeconds(10);
        _syncTimer = new Timer(async _ => await SyncFromConsulAsync(), null, interval, interval);

        _logger.LogInformation("ServiceExceptionManager 已初始化: NodeId={NodeId}, BasePath={BasePath}, SyncInterval={Interval}s",
            _currentNodeId, _basePath, interval.TotalSeconds);
    }

    /// <summary>
    /// 添加例外并同步到 Consul
    /// </summary>
    /// <param name="serviceId">服务ID（如玩家ID、房间ID等）</param>
    /// <param name="nodeId">实际运行的节点ID（如果为null，使用当前节点）</param>
    public async Task<bool> AddExceptionAsync(string serviceId, string? nodeId = null)
    {
        if (string.IsNullOrEmpty(serviceId))
            throw new ArgumentException("ServiceId cannot be null or empty", nameof(serviceId));

        nodeId ??= _currentNodeId;

        try
        {
            // 添加到本地缓存
            _localExceptions[serviceId] = nodeId;

            // 同步到 Consul
            var key = $"{_basePath}/{serviceId}";
            var kvPair = new KVPair(key)
            {
                Value = Encoding.UTF8.GetBytes(nodeId)
            };

            var result = await _consulClient.KV.Put(kvPair);

            if (result.Response)
            {
                _logger.LogInformation("例外已添加并同步到 Consul: ServiceId={ServiceId}, NodeId={NodeId}",
                    serviceId, nodeId);
                return true;
            }
            else
            {
                _logger.LogWarning("例外添加到 Consul 失败: ServiceId={ServiceId}, NodeId={NodeId}",
                    serviceId, nodeId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加例外到 Consul 时发生错误: ServiceId={ServiceId}, NodeId={NodeId}",
                serviceId, nodeId);
            return false;
        }
    }

    /// <summary>
    /// 移除例外并从 Consul 删除
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    public async Task<bool> RemoveExceptionAsync(string serviceId)
    {
        if (string.IsNullOrEmpty(serviceId))
            throw new ArgumentException("ServiceId cannot be null or empty", nameof(serviceId));

        try
        {
            // 从本地缓存移除
            _localExceptions.TryRemove(serviceId, out _);

            // 从 Consul 删除
            var key = $"{_basePath}/{serviceId}";
            var result = await _consulClient.KV.Delete(key);

            if (result.Response)
            {
                _logger.LogInformation("例外已移除并从 Consul 删除: ServiceId={ServiceId}", serviceId);
                return true;
            }
            else
            {
                _logger.LogWarning("从 Consul 删除例外失败: ServiceId={ServiceId}", serviceId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Consul 删除例外时发生错误: ServiceId={ServiceId}", serviceId);
            return false;
        }
    }

    /// <summary>
    /// 批量添加例外
    /// </summary>
    public async Task AddExceptionsBatchAsync(Dictionary<string, string> exceptions)
    {
        var tasks = exceptions.Select(kvp => AddExceptionAsync(kvp.Key, kvp.Value));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 批量移除例外
    /// </summary>
    public async Task RemoveExceptionsBatchAsync(IEnumerable<string> serviceIds)
    {
        var tasks = serviceIds.Select(RemoveExceptionAsync);
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 查找例外（优先本地缓存，然后全局缓存）
    /// </summary>
    /// <param name="serviceId">服务ID</param>
    /// <returns>节点ID，如果不存在返回 null</returns>
    public string? FindException(string serviceId)
    {
        if (string.IsNullOrEmpty(serviceId))
            return null;

        // 优先查找本地例外
        if (_localExceptions.TryGetValue(serviceId, out var localNodeId))
        {
            _logger.LogDebug("本地例外命中: ServiceId={ServiceId}, NodeId={NodeId}", serviceId, localNodeId);
            return localNodeId;
        }

        // 查找全局例外（从 Consul 同步的）
        if (_globalExceptions.TryGetValue(serviceId, out var globalNodeId))
        {
            _logger.LogDebug("全局例外命中: ServiceId={ServiceId}, NodeId={NodeId}", serviceId, globalNodeId);
            return globalNodeId;
        }

        return null;
    }

    /// <summary>
    /// 从 Consul 同步全局例外表
    /// </summary>
    public async Task SyncFromConsulAsync()
    {
        if (_isDisposed)
            return;

        await _syncLock.WaitAsync();
        try
        {
            var result = await _consulClient.KV.List(_basePath);

            if (result.Response == null || result.Response.Length == 0)
            {
                // Consul 中没有例外记录，清空全局缓存
                if (_globalExceptions.Count > 0)
                {
                    _globalExceptions.Clear();
                    _logger.LogDebug("全局例外表已清空（Consul 中无记录）");
                }
                return;
            }

            var newExceptions = new Dictionary<string, string>();
            foreach (var kvPair in result.Response)
            {
                // 跳过目录本身
                if (kvPair.Value == null || kvPair.Value.Length == 0)
                    continue;

                // 解析 Key: pulserpc/exceptions/{serviceId}
                var serviceId = kvPair.Key.Substring(_basePath.Length + 1);
                var nodeId = Encoding.UTF8.GetString(kvPair.Value);

                newExceptions[serviceId] = nodeId;
            }

            // 更新全局缓存
            _globalExceptions.Clear();
            foreach (var kvp in newExceptions)
            {
                _globalExceptions[kvp.Key] = kvp.Value;
            }

            _logger.LogDebug("从 Consul 同步全局例外表: {Count} 条记录", newExceptions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Consul 同步例外表时发生错误");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// 获取所有本地例外
    /// </summary>
    public Dictionary<string, string> GetLocalExceptions()
    {
        return new Dictionary<string, string>(_localExceptions);
    }

    /// <summary>
    /// 获取所有全局例外（从 Consul 同步的）
    /// </summary>
    public Dictionary<string, string> GetGlobalExceptions()
    {
        return new Dictionary<string, string>(_globalExceptions);
    }

    /// <summary>
    /// 获取统计信息
    /// </summary>
    public ServiceExceptionStats GetStats()
    {
        return new ServiceExceptionStats
        {
            LocalExceptionCount = _localExceptions.Count,
            GlobalExceptionCount = _globalExceptions.Count,
            NodeId = _currentNodeId
        };
    }

    /// <summary>
    /// 清空所有本地例外（不影响 Consul）
    /// </summary>
    public void ClearLocal()
    {
        _localExceptions.Clear();
        _logger.LogInformation("本地例外表已清空");
    }

    /// <summary>
    /// 清空所有本地例外并从 Consul 删除
    /// </summary>
    public async Task ClearAllAsync()
    {
        var serviceIds = _localExceptions.Keys.ToList();
        await RemoveExceptionsBatchAsync(serviceIds);
        _localExceptions.Clear();
        _logger.LogInformation("所有例外已清空并从 Consul 删除");
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _syncTimer?.Dispose();
        _syncLock.Dispose();

        _logger.LogInformation("ServiceExceptionManager 已释放");
        await ValueTask.CompletedTask;
    }
}

/// <summary>
/// Service 例外统计信息
/// </summary>
public class ServiceExceptionStats
{
    /// <summary>
    /// 本地例外数量
    /// </summary>
    public int LocalExceptionCount { get; init; }

    /// <summary>
    /// 全局例外数量（从 Consul 同步）
    /// </summary>
    public int GlobalExceptionCount { get; init; }

    /// <summary>
    /// 当前节点ID
    /// </summary>
    public string NodeId { get; init; } = string.Empty;

    public override string ToString() =>
        $"NodeId={NodeId}, Local={LocalExceptionCount}, Global={GlobalExceptionCount}";
}
