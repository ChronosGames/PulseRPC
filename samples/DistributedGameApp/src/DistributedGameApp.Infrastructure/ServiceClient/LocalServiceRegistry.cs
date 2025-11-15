using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 本地服务注册表 - 管理 PID 与 ServiceId 的本地映射关系
/// </summary>
public class LocalServiceRegistry
{
    private readonly ConcurrentDictionary<long, string> _pidToServiceId = new();
    private readonly ConcurrentDictionary<string, long> _serviceIdToPid = new();
    private readonly ConcurrentDictionary<string, ServiceMetadata> _serviceMetadata = new();
    private readonly ILogger<LocalServiceRegistry> _logger;

    public LocalServiceRegistry(ILogger<LocalServiceRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册服务（PID 与 ServiceId 的映射）
    /// </summary>
    /// <param name="pid">进程ID或服务实例ID</param>
    /// <param name="serviceId">服务ID</param>
    /// <param name="metadata">服务元数据（可选）</param>
    public void Register(long pid, string serviceId, ServiceMetadata? metadata = null)
    {
        if (string.IsNullOrEmpty(serviceId))
            throw new ArgumentException("ServiceId cannot be null or empty", nameof(serviceId));

        _pidToServiceId[pid] = serviceId;
        _serviceIdToPid[serviceId] = pid;

        if (metadata != null)
        {
            _serviceMetadata[serviceId] = metadata;
        }

        _logger.LogInformation("本地服务注册: PID={Pid}, ServiceId={ServiceId}, Metadata={Metadata}",
            pid, serviceId, metadata?.ToString() ?? "None");
    }

    /// <summary>
    /// 注销服务
    /// </summary>
    public void Unregister(string serviceId)
    {
        if (_serviceIdToPid.TryRemove(serviceId, out var pid))
        {
            _pidToServiceId.TryRemove(pid, out _);
            _serviceMetadata.TryRemove(serviceId, out _);

            _logger.LogInformation("本地服务注销: ServiceId={ServiceId}, PID={Pid}", serviceId, pid);
        }
    }

    /// <summary>
    /// 通过 PID 获取 ServiceId
    /// </summary>
    public string? GetServiceId(long pid)
    {
        return _pidToServiceId.TryGetValue(pid, out var serviceId) ? serviceId : null;
    }

    /// <summary>
    /// 通过 ServiceId 获取 PID
    /// </summary>
    public long? GetPid(string serviceId)
    {
        return _serviceIdToPid.TryGetValue(serviceId, out var pid) ? pid : null;
    }

    /// <summary>
    /// 获取服务元数据
    /// </summary>
    public ServiceMetadata? GetMetadata(string serviceId)
    {
        return _serviceMetadata.TryGetValue(serviceId, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// 检查服务是否已注册
    /// </summary>
    public bool IsRegistered(string serviceId)
    {
        return _serviceIdToPid.ContainsKey(serviceId);
    }

    /// <summary>
    /// 获取所有已注册的 ServiceId
    /// </summary>
    public IEnumerable<string> GetAllServiceIds()
    {
        return _serviceIdToPid.Keys;
    }

    /// <summary>
    /// 获取注册数量
    /// </summary>
    public int Count => _serviceIdToPid.Count;

    /// <summary>
    /// 清空所有注册
    /// </summary>
    public void Clear()
    {
        _pidToServiceId.Clear();
        _serviceIdToPid.Clear();
        _serviceMetadata.Clear();

        _logger.LogInformation("本地服务注册表已清空");
    }
}

/// <summary>
/// 服务元数据
/// </summary>
public class ServiceMetadata
{
    /// <summary>
    /// 服务类型
    /// </summary>
    public string? ServiceType { get; set; }

    /// <summary>
    /// 节点ID
    /// </summary>
    public int? NodeId { get; set; }

    /// <summary>
    /// 是否启用一致性哈希
    /// </summary>
    public bool EnableConsistentHash { get; set; } = true;

    /// <summary>
    /// 是否同步到 Consul
    /// </summary>
    public bool SyncToConsul { get; set; } = true;

    /// <summary>
    /// 额外标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public override string ToString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(ServiceType)) parts.Add($"Type={ServiceType}");
        if (NodeId.HasValue) parts.Add($"NodeId={NodeId}");
        parts.Add($"ConsistentHash={EnableConsistentHash}");
        parts.Add($"SyncToConsul={SyncToConsul}");
        return string.Join(", ", parts);
    }
}
