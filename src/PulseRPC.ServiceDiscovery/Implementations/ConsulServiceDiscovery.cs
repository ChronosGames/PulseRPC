using Consul;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Server.ServiceDiscovery;
using PulseRPC.Client.ServiceDiscovery;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;

namespace PulseRPC.ServiceDiscovery.Implementations
{
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
        /// Consul 服务器地址
        /// </summary>
        public string Address { get; set; } = "http://localhost:8500";

        /// <summary>
        /// Consul 数据中心
        /// </summary>
        public string? Datacenter { get; set; }

        /// <summary>
        /// 认证令牌
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// 等待时间 (用于长轮询)
        /// </summary>
        public TimeSpan WaitTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 连接超时时间
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 请求超时时间
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 健康检查超时时间
        /// </summary>
        public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 健康检查间隔
        /// </summary>
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 健康检查失败后的注销延迟
        /// </summary>
        public TimeSpan DeregisterAfter { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 是否使用 HTTPS
        /// </summary>
        public bool UseHttps { get; set; } = false;

        /// <summary>
        /// 服务名称前缀
        /// </summary>
        public string ServiceNamePrefix { get; set; } = "pulserpc";

        /// <summary>
        /// 默认标签
        /// </summary>
        public List<string> DefaultTags { get; set; } = new() { "pulserpc", "rpc" };
    }

    /// <summary>
    /// 基于 Consul 的服务发现实现
    /// </summary>
    public class ConsulServiceDiscovery : IServiceDiscovery, IServiceRegistry, IDisposable
    {
        private readonly ConsulClient _consulClient;
        private readonly ConsulOptions _options;
        private readonly ILogger<ConsulServiceDiscovery> _logger;
        private readonly ConcurrentDictionary<string, ulong> _watchIndexes = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _watchCancellations = new();
        private readonly ConcurrentDictionary<string, ServiceEndpoint> _registeredServices = new();
        private bool _disposed;

        public ConsulServiceDiscovery(IOptions<ConsulOptions> options, ILogger<ConsulServiceDiscovery> logger)
        {
            _options = options.Value;
            _logger = logger;

            var config = new ConsulClientConfiguration
            {
                Address = new Uri(_options.Address),
                Datacenter = _options.Datacenter,
                Token = _options.Token,
                WaitTime = _options.WaitTime
            };

            _consulClient = new ConsulClient(config);
            _logger.LogInformation("Consul客户端已初始化，地址: {Address}", _options.Address);
        }

        #region IServiceDiscovery Implementation

        /// <summary>
        /// 发现指定名称的服务
        /// </summary>
        public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAsync(string serviceName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var fullServiceName = GetFullServiceName(serviceName);
                var response =
                    await _consulClient.Health.Service(fullServiceName, string.Empty, true, cancellationToken);

                var endpoints = new List<ServiceEndpoint>();
                foreach (var service in response.Response)
                {
                    var endpoint = ConvertToServiceEndpoint(service);
                    if (endpoint != null)
                    {
                        endpoints.Add(endpoint);
                    }
                }

                _logger.LogDebug("发现服务 {ServiceName}，共 {Count} 个实例", serviceName, endpoints.Count);
                return endpoints.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发现服务 {ServiceName} 失败", serviceName);
                return Array.Empty<ServiceEndpoint>();
            }
        }

        /// <summary>
        /// 监听指定服务的变化
        /// </summary>
        public async IAsyncEnumerable<ServiceEndpoint[]> WatchAsync(string serviceName,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var fullServiceName = GetFullServiceName(serviceName);
            var watchKey = $"watch_{fullServiceName}";

            // 取消之前的监听
            if (_watchCancellations.TryGetValue(watchKey, out var oldCts))
            {
                oldCts.Cancel();
                _watchCancellations.TryRemove(watchKey, out _);
            }

            var newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _watchCancellations[watchKey] = newCts;

            var currentIndex = _watchIndexes.GetOrAdd(watchKey, 0);

            try
            {
                while (!newCts.Token.IsCancellationRequested)
                {
                    var endpoints = new List<ServiceEndpoint>();

                    try
                    {
                        var queryOptions = new QueryOptions { WaitIndex = currentIndex, WaitTime = _options.WaitTime };

                        var response = await _consulClient.Health.Service(fullServiceName, string.Empty, true,
                            queryOptions, newCts.Token);

                        if (response.LastIndex > currentIndex)
                        {
                            currentIndex = response.LastIndex;
                            _watchIndexes[watchKey] = currentIndex;

                            foreach (var service in response.Response)
                            {
                                var endpoint = ConvertToServiceEndpoint(service);
                                if (endpoint != null)
                                {
                                    endpoints.Add(endpoint);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "监听服务 {ServiceName} 时发生错误", serviceName);
                        await Task.Delay(TimeSpan.FromSeconds(5), newCts.Token);
                    }

                    yield return endpoints.ToArray();
                }
            }
            finally
            {
                _watchCancellations.TryRemove(watchKey, out _);
                newCts.Dispose();
            }
        }

        /// <summary>
        /// 获取所有可用的服务名称
        /// </summary>
        public async Task<IReadOnlyList<string>> GetServiceNamesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _consulClient.Catalog.Services(cancellationToken);
                var serviceNames = response.Response.Keys
                    .Where(name => name.StartsWith(_options.ServiceNamePrefix))
                    .Select(name => name.Substring(_options.ServiceNamePrefix.Length + 1))
                    .ToList();

                return serviceNames.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取服务名称列表失败");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 根据标签过滤服务
        /// </summary>
        public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverByTagsAsync(string serviceName,
            Dictionary<string, string> tags, CancellationToken cancellationToken = default)
        {
            var allEndpoints = await DiscoverAsync(serviceName, cancellationToken);

            var filteredEndpoints = allEndpoints.Where(endpoint =>
                tags.All(tag => endpoint.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value)
            ).ToList();

            return filteredEndpoints.AsReadOnly();
        }

        #endregion

        #region IServiceRegistry Implementation

        /// <summary>
        /// 注册服务
        /// </summary>
        public async Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            try
            {
                var registration = CreateServiceRegistration(endpoint);
                await _consulClient.Agent.ServiceRegister(registration, cancellationToken);

                _registeredServices[endpoint.ServiceId] = endpoint;
                _logger.LogInformation("已注册服务: {ServiceName}({ServiceId}) @ {EndPoint}",
                    endpoint.ServiceName, endpoint.ServiceId, endpoint.EndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册服务 {ServiceName}({ServiceId}) 失败",
                    endpoint.ServiceName, endpoint.ServiceId);
                throw;
            }
        }

        /// <summary>
        /// 注销服务
        /// </summary>
        public async Task UnregisterAsync(string serviceId, CancellationToken cancellationToken = default)
        {
            try
            {
                await _consulClient.Agent.ServiceDeregister(serviceId, cancellationToken);
                _registeredServices.TryRemove(serviceId, out _);
                _logger.LogInformation("已注销服务: {ServiceId}", serviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注销服务 {ServiceId} 失败", serviceId);
                throw;
            }
        }

        /// <summary>
        /// 更新服务健康状态
        /// </summary>
        public async Task UpdateHealthAsync(string serviceId, HealthStatus status,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var checkId = $"service:{serviceId}";
                var consulStatus = ConvertToConsulStatus(status);
                var note = $"Health status updated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";

                await _consulClient.Agent.UpdateTTL(checkId, note, consulStatus, cancellationToken);

                if (_registeredServices.TryGetValue(serviceId, out var endpoint))
                {
                    endpoint.HealthStatus = status;
                    endpoint.LastHealthCheck = DateTime.UtcNow;
                }

                _logger.LogDebug("已更新服务 {ServiceId} 健康状态: {Status}", serviceId, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新服务 {ServiceId} 健康状态失败", serviceId);
                throw;
            }
        }

        /// <summary>
        /// 获取已注册的服务列表
        /// </summary>
        public async Task<IReadOnlyList<ServiceEndpoint>> GetRegisteredServicesAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _consulClient.Agent.Services(cancellationToken);
                var endpoints = new List<ServiceEndpoint>();

                foreach (var service in response.Response.Values)
                {
                    if (service.Service.StartsWith(_options.ServiceNamePrefix))
                    {
                        var endpoint = ConvertAgentServiceToEndpoint(service);
                        if (endpoint != null)
                        {
                            endpoints.Add(endpoint);
                        }
                    }
                }

                return endpoints.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已注册服务列表失败");
                return Array.Empty<ServiceEndpoint>();
            }
        }

        #endregion

        #region Private Methods

        private string GetFullServiceName(string serviceName)
        {
            return $"{_options.ServiceNamePrefix}-{serviceName}";
        }

        private ServiceEndpoint? ConvertToServiceEndpoint(ServiceEntry serviceEntry)
        {
            try
            {
                var service = serviceEntry.Service;
                var endPoint = new IPEndPoint(IPAddress.Parse(service.Address), service.Port);

                var tags = new Dictionary<string, string>();
                foreach (var tag in service.Tags ?? Array.Empty<string>())
                {
                    var parts = tag.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        tags[parts[0]] = parts[1];
                    }
                    else
                    {
                        tags[tag] = string.Empty;
                    }
                }

                var healthStatus = serviceEntry.Checks?.All(c => c.Status == Consul.HealthStatus.Passing) == true
                    ? HealthStatus.Healthy
                    : HealthStatus.Unhealthy;

                return new ServiceEndpoint
                {
                    ServiceId = service.ID,
                    ServiceName = service.Service.Substring(_options.ServiceNamePrefix.Length + 1),
                    EndPoint = endPoint,
                    Tags = tags,
                    HealthStatus = healthStatus,
                    Weight = tags.TryGetValue("weight", out var weightStr) &&
                             int.TryParse(weightStr, out var weight)
                        ? weight
                        : 1
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "转换服务条目失败: {ServiceId}", serviceEntry.Service?.ID);
                return null;
            }
        }

        private ServiceEndpoint? ConvertAgentServiceToEndpoint(AgentService service)
        {
            try
            {
                var endPoint = new IPEndPoint(IPAddress.Parse(service.Address), service.Port);

                var tags = new Dictionary<string, string>();
                foreach (var tag in service.Tags ?? Array.Empty<string>())
                {
                    var parts = tag.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        tags[parts[0]] = parts[1];
                    }
                    else
                    {
                        tags[tag] = string.Empty;
                    }
                }

                return new ServiceEndpoint
                {
                    ServiceId = service.ID,
                    ServiceName = service.Service.Substring(_options.ServiceNamePrefix.Length + 1),
                    EndPoint = endPoint,
                    Tags = tags,
                    Weight = tags.TryGetValue("weight", out var weightStr) &&
                             int.TryParse(weightStr, out var weight)
                        ? weight
                        : 1
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "转换代理服务失败: {ServiceId}", service?.ID);
                return null;
            }
        }

        private AgentServiceRegistration CreateServiceRegistration(ServiceEndpoint endpoint)
        {
            var registration = new AgentServiceRegistration
            {
                ID = endpoint.ServiceId,
                Name = GetFullServiceName(endpoint.ServiceName),
                Address = endpoint.EndPoint.Address.ToString(),
                Port = endpoint.EndPoint.Port,
                Tags = CreateTags(endpoint)
            };

            // 添加健康检查
            registration.Check = new AgentServiceCheck
            {
                TCP = endpoint.EndPoint.ToString(),
                Timeout = _options.HealthCheckTimeout,
                Interval = _options.HealthCheckInterval,
                DeregisterCriticalServiceAfter = _options.DeregisterAfter
            };

            return registration;
        }

        private string[] CreateTags(ServiceEndpoint endpoint)
        {
            var tags = new List<string>(_options.DefaultTags);

            // 添加权重标签
            if (endpoint.Weight > 1)
            {
                tags.Add($"weight={endpoint.Weight}");
            }

            // 添加版本标签
            if (!string.IsNullOrEmpty(endpoint.Version))
            {
                tags.Add($"version={endpoint.Version}");
            }

            // 添加自定义标签
            foreach (var tag in endpoint.Tags)
            {
                if (!string.IsNullOrEmpty(tag.Value))
                {
                    tags.Add($"{tag.Key}={tag.Value}");
                }
                else
                {
                    tags.Add(tag.Key);
                }
            }

            return tags.ToArray();
        }

        private TTLStatus ConvertToConsulStatus(HealthStatus status)
        {
            return status switch
            {
                HealthStatus.Healthy => TTLStatus.Pass,
                HealthStatus.Unhealthy => TTLStatus.Critical,
                HealthStatus.Maintenance => TTLStatus.Warn,
                _ => TTLStatus.Critical
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // 取消所有监听
            foreach (var cts in _watchCancellations.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _watchCancellations.Clear();

            // 注销所有已注册的服务
            foreach (var serviceId in _registeredServices.Keys.ToArray())
            {
                try
                {
                    _consulClient.Agent.ServiceDeregister(serviceId).Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "注销服务 {ServiceId} 时发生错误", serviceId);
                }
            }

            _consulClient?.Dispose();
            _logger.LogInformation("ConsulServiceDiscovery 已释放");
        }

        #endregion
    }
}
