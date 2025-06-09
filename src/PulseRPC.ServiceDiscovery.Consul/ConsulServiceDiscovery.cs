using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceDiscovery;
using System.Runtime.CompilerServices;

namespace PulseRPC.ServiceDiscovery.Consul;

/// <summary>
/// Consul 服务发现实现
/// </summary>
public class ConsulServiceDiscovery : IServiceDiscovery, IServiceRegistry, IDisposable
{
    private readonly ConsulClient _consulClient;
    private readonly ConsulOptions _options;
    private readonly ILogger<ConsulServiceDiscovery> _logger;
    private readonly Dictionary<string, CancellationTokenSource> _watchTokens = new();
    private volatile bool _disposed;

    public ConsulServiceDiscovery(
        IOptions<ConsulOptions> options,
        ILogger<ConsulServiceDiscovery> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new ConsulClientConfiguration
        {
            Address = new Uri(_options.Address),
            Datacenter = _options.Datacenter,
            Token = _options.Token
        };

        _consulClient = new ConsulClient(config);

        _logger.LogInformation("ConsulServiceDiscovery 已初始化，地址: {Address}, 数据中心: {Datacenter}",
            _options.Address, _options.Datacenter);
    }

    #region IServiceDiscovery Implementation

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _consulClient.Health.Service(serviceName, cancellationToken: cancellationToken);
            return ParseServiceEndpoints(result.Response).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Consul 发现服务失败: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverByTagsAsync(
        string serviceName,
        Dictionary<string, string> tags,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tagStrings = tags.Select(kvp => $"{kvp.Key}={kvp.Value}").ToArray();
            var result = await _consulClient.Health.Service(serviceName, string.Empty, false,
                QueryOptions.Default with { Filter = BuildTagFilter(tags) }, cancellationToken);

            return ParseServiceEndpoints(result.Response).AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "根据标签从 Consul 发现服务失败: {ServiceName}", serviceName);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _consulClient.Catalog.Services(cancellationToken: cancellationToken);
            return result.Response.Keys.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Consul 获取服务名称列表失败");
            throw;
        }
    }

    public async IAsyncEnumerable<ServiceEndpoint[]> WatchAsync(
        string serviceName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var watchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _watchTokens[serviceName] = watchCts;

        try
        {
            ulong lastIndex = 0;
            _logger.LogInformation("开始监听 Consul 服务变化: {ServiceName}", serviceName);

            while (!watchCts.Token.IsCancellationRequested)
            {
                try
                {
                    var query = new QueryOptions
                    {
                        WaitIndex = lastIndex,
                        WaitTime = _options.WatchTimeout
                    };

                    var result = await _consulClient.Health.Service(serviceName,
                        cancellationToken: watchCts.Token, q: query);

                    if (result.LastIndex > lastIndex)
                    {
                        lastIndex = result.LastIndex;
                        var endpoints = ParseServiceEndpoints(result.Response);

                        _logger.LogDebug("检测到 Consul 服务变化: {ServiceName}, 端点数量: {Count}",
                            serviceName, endpoints.Count);

                        yield return endpoints.ToArray();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Consul 监听过程中发生异常: {ServiceName}", serviceName);
                    await Task.Delay(TimeSpan.FromSeconds(5), watchCts.Token);
                }
            }
        }
        finally
        {
            _watchTokens.Remove(serviceName);
            watchCts.Dispose();
        }
    }

    #endregion

    #region IServiceRegistry Implementation

    public async Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            var registration = new AgentServiceRegistration
            {
                ID = endpoint.ServiceId,
                Name = endpoint.ServiceName,
                Address = endpoint.EndPoint.Address.ToString(),
                Port = endpoint.EndPoint.Port,
                Tags = endpoint.Tags.Select(kvp => $"{kvp.Key}={kvp.Value}").ToArray(),
                Check = CreateHealthCheck(endpoint)
            };

            await _consulClient.Agent.ServiceRegister(registration, cancellationToken);

            _logger.LogInformation("服务已注册到 Consul: {ServiceId} @ {EndPoint}",
                endpoint.ServiceId, endpoint.EndPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "向 Consul 注册服务失败: {ServiceId}", endpoint.ServiceId);
            throw;
        }
    }

    public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _consulClient.Agent.ServiceDeregister(serviceId, cancellationToken);
            _logger.LogInformation("服务已从 Consul 注销: {ServiceId}", serviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Consul 注销服务失败: {ServiceId}", serviceId);
            throw;
        }
    }

    public async Task UpdateHealthAsync(string serviceId, HealthStatus status, CancellationToken cancellationToken = default)
    {
        try
        {
            var checkId = $"service:{serviceId}";

            switch (status)
            {
                case HealthStatus.Healthy:
                    await _consulClient.Agent.PassTTL(checkId, "Service is healthy", cancellationToken);
                    break;
                case HealthStatus.Unhealthy:
                    await _consulClient.Agent.FailTTL(checkId, "Service is unhealthy", cancellationToken);
                    break;
                case HealthStatus.Unknown:
                    await _consulClient.Agent.WarnTTL(checkId, "Service health unknown", cancellationToken);
                    break;
            }

            _logger.LogDebug("已更新服务健康状态: {ServiceId} -> {Status}", serviceId, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新 Consul 服务健康状态失败: {ServiceId}", serviceId);
        }
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _consulClient.Agent.Services(cancellationToken);
            var endpoints = new List<ServiceEndpoint>();

            foreach (var service in result.Response.Values)
            {
                var endpoint = new ServiceEndpoint
                {
                    ServiceId = service.ID,
                    ServiceName = service.Service,
                    EndPoint = new System.Net.IPEndPoint(
                        System.Net.IPAddress.Parse(service.Address),
                        service.Port),
                    HealthStatus = HealthStatus.Unknown,
                    Tags = ParseTags(service.Tags)
                };

                endpoints.Add(endpoint);
            }

            return endpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从 Consul 获取已注册服务失败");
            throw;
        }
    }

    #endregion

    #region Private Methods

    private List<ServiceEndpoint> ParseServiceEndpoints(ServiceEntry[] entries)
    {
        var endpoints = new List<ServiceEndpoint>();

        foreach (var entry in entries)
        {
            try
            {
                var endpoint = new ServiceEndpoint
                {
                    ServiceId = entry.Service.ID,
                    ServiceName = entry.Service.Service,
                    EndPoint = new System.Net.IPEndPoint(
                        System.Net.IPAddress.Parse(entry.Service.Address),
                        entry.Service.Port),
                    HealthStatus = MapHealthStatus(entry.Checks),
                    Tags = ParseTags(entry.Service.Tags)
                };

                endpoints.Add(endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 Consul 服务条目失败: {ServiceId}", entry.Service?.ID);
            }
        }

        return endpoints;
    }

    private HealthStatus MapHealthStatus(HealthCheck[] checks)
    {
        if (checks == null || checks.Length == 0)
            return HealthStatus.Unknown;

        if (checks.All(c => c.Status.Equals(global::Consul.HealthStatus.Passing)))
            return HealthStatus.Healthy;

        if (checks.Any(c => c.Status.Equals(global::Consul.HealthStatus.Critical)))
            return HealthStatus.Unhealthy;

        return HealthStatus.Unknown;
    }

    private Dictionary<string, string> ParseTags(string[] tags)
    {
        var result = new Dictionary<string, string>();

        if (tags == null) return result;

        foreach (var tag in tags)
        {
            var parts = tag.Split('=', 2);
            if (parts.Length == 2)
            {
                result[parts[0]] = parts[1];
            }
            else
            {
                result[tag] = string.Empty;
            }
        }

        return result;
    }

    private string BuildTagFilter(Dictionary<string, string> tags)
    {
        var filters = tags.Select(kvp => $"Tags contains \"{kvp.Key}={kvp.Value}\"");
        return string.Join(" and ", filters);
    }

    private AgentServiceCheck? CreateHealthCheck(ServiceEndpoint endpoint)
    {
        if (!_options.EnableHealthCheck)
            return null;

        return new AgentServiceCheck
        {
            TTL = _options.HealthCheckTTL,
            Status = global::Consul.HealthStatus.Passing,
            DeregisterCriticalServiceAfter = _options.DeregisterCriticalServiceAfter
        };
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        // 停止所有监听
        foreach (var cts in _watchTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _watchTokens.Clear();

        _consulClient?.Dispose();

        _logger.LogInformation("ConsulServiceDiscovery 已释放资源");
    }
}

/// <summary>
/// Consul 配置选项
/// </summary>
public class ConsulOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "Consul";

    /// <summary>
    /// Consul 地址
    /// </summary>
    public string Address { get; set; } = "http://localhost:8500";

    /// <summary>
    /// 数据中心名称
    /// </summary>
    public string? Datacenter { get; set; }

    /// <summary>
    /// 访问令牌
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// 监听超时时间
    /// </summary>
    public TimeSpan WatchTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查 TTL
    /// </summary>
    public TimeSpan HealthCheckTTL { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 关键服务自动注销时间
    /// </summary>
    public TimeSpan DeregisterCriticalServiceAfter { get; set; } = TimeSpan.FromMinutes(1);
}
