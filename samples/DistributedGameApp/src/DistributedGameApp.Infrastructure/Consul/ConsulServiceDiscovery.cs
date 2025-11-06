using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedGameApp.Infrastructure.Consul;

/// <summary>
/// Consul 服务发现
/// </summary>
public class ConsulServiceDiscovery
{
    private readonly IConsulClient _client;
    private readonly ConsulOptions _options;
    private readonly ILogger<ConsulServiceDiscovery> _logger;

    public ConsulServiceDiscovery(
        IOptions<ConsulOptions> options,
        ILogger<ConsulServiceDiscovery> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new ConsulClient(config =>
        {
            config.Address = new Uri(_options.Address);
        });
    }

    /// <summary>
    /// 获取指定类型的所有服务
    /// </summary>
    public async Task<List<ServiceRegistration>> GetServicesAsync(
        string serviceType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceName = $"{_options.ServiceBasePath}-{serviceType.ToLower()}";
            var queryResult = await _client.Health.Service(serviceName, null, true, cancellationToken);

            var services = new List<ServiceRegistration>();

            foreach (var serviceEntry in queryResult.Response)
            {
                try
                {
                    var service = ParseServiceEntry(serviceEntry);
                    if (service != null)
                    {
                        services.Add(service);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse service entry: {ServiceId}", serviceEntry.Service.ID);
                }
            }

            return services;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get services of type: {ServiceType}", serviceType);
            return new List<ServiceRegistration>();
        }
    }

    /// <summary>
    /// 获取单个服务
    /// </summary>
    public async Task<ServiceRegistration?> GetServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queryResult = await _client.Agent.Services(cancellationToken);

            if (queryResult.Response.TryGetValue(serviceId, out var service))
            {
                return ParseAgentService(service, serviceId);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service: {ServiceId}", serviceId);
            return null;
        }
    }

    /// <summary>
    /// 发现最优服务（负载最低）
    /// </summary>
    public async Task<ServiceRegistration?> DiscoverBestServiceAsync(
        string serviceType,
        CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(serviceType, cancellationToken);

        if (services.Count == 0)
        {
            _logger.LogWarning("No services available for type: {ServiceType}", serviceType);
            return null;
        }

        // 过滤在线服务
        var onlineServices = services.Where(s => s.Status == "Online").ToList();

        if (onlineServices.Count == 0)
        {
            _logger.LogWarning("No online services available for type: {ServiceType}", serviceType);
            return null;
        }

        // 选择负载率最低的服务
        var bestService = onlineServices
            .OrderBy(s => (double)s.CurrentLoad / s.MaxCapacity)
            .First();

        _logger.LogInformation(
            "Discovered service: {ServiceType}/{ServiceId} (Load: {Load}/{Max})",
            bestService.ServiceType,
            bestService.ServiceId,
            bestService.CurrentLoad,
            bestService.MaxCapacity);

        return bestService;
    }

    /// <summary>
    /// 随机选择一个服务
    /// </summary>
    public async Task<ServiceRegistration?> DiscoverRandomServiceAsync(
        string serviceType,
        CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(serviceType, cancellationToken);

        var onlineServices = services.Where(s => s.Status == "Online").ToList();

        if (onlineServices.Count == 0)
        {
            return null;
        }

        var random = new Random();
        return onlineServices[random.Next(onlineServices.Count)];
    }

    /// <summary>
    /// 监听服务变更
    /// </summary>
    /// <remarks>
    /// 使用 Consul 的阻塞查询来实现服务监听
    /// </remarks>
    public async Task WatchServicesAsync(
        string serviceType,
        Action<ServiceChangeType, ServiceRegistration?> callback,
        CancellationToken cancellationToken = default)
    {
        var serviceName = $"{_options.ServiceBasePath}-{serviceType.ToLower()}";
        var knownServices = new Dictionary<string, ServiceRegistration>();
        ulong waitIndex = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 使用阻塞查询
                    var queryOptions = new QueryOptions
                    {
                        WaitIndex = waitIndex,
                        WaitTime = TimeSpan.FromSeconds(30)
                    };

                    var queryResult = await _client.Health.Service(
                        serviceName,
                        null,
                        true,
                        queryOptions,
                        cancellationToken);

                    // 更新索引
                    waitIndex = queryResult.LastIndex;

                    var currentServices = queryResult.Response
                        .Select(ParseServiceEntry)
                        .Where(s => s != null)
                        .ToDictionary(s => s!.ServiceId, s => s!);

                    // 检查新增的服务
                    foreach (var service in currentServices.Values)
                    {
                        if (!knownServices.ContainsKey(service.ServiceId))
                        {
                            callback(ServiceChangeType.Added, service);
                        }
                        else if (!AreServicesEqual(knownServices[service.ServiceId], service))
                        {
                            callback(ServiceChangeType.Modified, service);
                        }
                    }

                    // 检查删除的服务
                    foreach (var serviceId in knownServices.Keys)
                    {
                        if (!currentServices.ContainsKey(serviceId))
                        {
                            callback(ServiceChangeType.Removed, knownServices[serviceId]);
                        }
                    }

                    knownServices = currentServices;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error watching services of type: {ServiceType}", serviceType);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Watch cancelled for service type: {ServiceType}", serviceType);
        }
    }

    /// <summary>
    /// 解析服务条目
    /// </summary>
    private ServiceRegistration? ParseServiceEntry(ServiceEntry serviceEntry)
    {
        var service = serviceEntry.Service;
        var meta = service.Meta ?? new Dictionary<string, string>();

        // 解析负载信息
        int currentLoad = 0;
        int maxCapacity = 100;

        if (meta.TryGetValue("CurrentLoad", out var loadStr) && int.TryParse(loadStr, out var load))
        {
            currentLoad = load;
        }

        if (meta.TryGetValue("MaxCapacity", out var capacityStr) && int.TryParse(capacityStr, out var capacity))
        {
            maxCapacity = capacity;
        }

        // 解析节点ID
        int nodeId = 0;
        if (meta.TryGetValue("NodeId", out var nodeIdStr) && int.TryParse(nodeIdStr, out var nid))
        {
            nodeId = nid;
        }

        // 解析 KCP 端口
        int? kcpPort = null;
        if (meta.TryGetValue("KcpPort", out var kcpPortStr) && int.TryParse(kcpPortStr, out var kcp))
        {
            kcpPort = kcp;
        }

        // 解析时间
        DateTime registeredAt = DateTime.UtcNow;
        if (meta.TryGetValue("RegisteredAt", out var regAtStr) && DateTime.TryParse(regAtStr, out var regAt))
        {
            registeredAt = regAt;
        }

        DateTime lastHeartbeat = DateTime.UtcNow;
        if (meta.TryGetValue("LastHeartbeat", out var hbStr) && DateTime.TryParse(hbStr, out var hb))
        {
            lastHeartbeat = hb;
        }

        return new ServiceRegistration
        {
            ServiceId = service.ID,
            ServiceType = meta.TryGetValue("ServiceType", out var svcType) ? svcType : "Unknown",
            NodeId = nodeId,
            NodeName = meta.TryGetValue("NodeName", out var nodeName) ? nodeName : "",
            Host = service.Address,
            TcpPort = service.Port,
            KcpPort = kcpPort,
            CurrentLoad = currentLoad,
            MaxCapacity = maxCapacity,
            Status = meta.TryGetValue("Status", out var status) ? status : "Online",
            RegisteredAt = registeredAt,
            LastHeartbeat = lastHeartbeat,
            Metadata = meta
                .Where(kvp => kvp.Key.StartsWith("Custom_"))
                .ToDictionary(kvp => kvp.Key.Substring(7), kvp => kvp.Value)
        };
    }

    /// <summary>
    /// 解析 Agent 服务
    /// </summary>
    private ServiceRegistration? ParseAgentService(AgentService service, string serviceId)
    {
        var meta = service.Meta ?? new Dictionary<string, string>();

        int currentLoad = 0;
        int maxCapacity = 100;

        if (meta.TryGetValue("CurrentLoad", out var loadStr) && int.TryParse(loadStr, out var load))
        {
            currentLoad = load;
        }

        if (meta.TryGetValue("MaxCapacity", out var capacityStr) && int.TryParse(capacityStr, out var capacity))
        {
            maxCapacity = capacity;
        }

        int nodeId = 0;
        if (meta.TryGetValue("NodeId", out var nodeIdStr) && int.TryParse(nodeIdStr, out var nid))
        {
            nodeId = nid;
        }

        int? kcpPort = null;
        if (meta.TryGetValue("KcpPort", out var kcpPortStr) && int.TryParse(kcpPortStr, out var kcp))
        {
            kcpPort = kcp;
        }

        DateTime registeredAt = DateTime.UtcNow;
        if (meta.TryGetValue("RegisteredAt", out var regAtStr) && DateTime.TryParse(regAtStr, out var regAt))
        {
            registeredAt = regAt;
        }

        DateTime lastHeartbeat = DateTime.UtcNow;
        if (meta.TryGetValue("LastHeartbeat", out var hbStr) && DateTime.TryParse(hbStr, out var hb))
        {
            lastHeartbeat = hb;
        }

        return new ServiceRegistration
        {
            ServiceId = serviceId,
            ServiceType = meta.TryGetValue("ServiceType", out var svcType) ? svcType : "Unknown",
            NodeId = nodeId,
            NodeName = meta.TryGetValue("NodeName", out var nodeName) ? nodeName : "",
            Host = service.Address,
            TcpPort = service.Port,
            KcpPort = kcpPort,
            CurrentLoad = currentLoad,
            MaxCapacity = maxCapacity,
            Status = meta.TryGetValue("Status", out var status) ? status : "Online",
            RegisteredAt = registeredAt,
            LastHeartbeat = lastHeartbeat,
            Metadata = meta
                .Where(kvp => kvp.Key.StartsWith("Custom_"))
                .ToDictionary(kvp => kvp.Key.Substring(7), kvp => kvp.Value)
        };
    }

    /// <summary>
    /// 比较服务是否相等
    /// </summary>
    private static bool AreServicesEqual(ServiceRegistration s1, ServiceRegistration s2)
    {
        return s1.Status == s2.Status &&
               s1.CurrentLoad == s2.CurrentLoad &&
               s1.Host == s2.Host &&
               s1.TcpPort == s2.TcpPort;
    }

    /// <summary>
    /// 获取服务数量
    /// </summary>
    public async Task<int> GetServiceCountAsync(
        string serviceType,
        CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(serviceType, cancellationToken);
        return services.Count;
    }

    /// <summary>
    /// 检查服务是否存在
    /// </summary>
    public async Task<bool> ServiceExistsAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(serviceId, cancellationToken);
        return service != null;
    }
}

/// <summary>
/// 服务变更类型
/// </summary>
public enum ServiceChangeType
{
    Added,
    Modified,
    Removed
}
