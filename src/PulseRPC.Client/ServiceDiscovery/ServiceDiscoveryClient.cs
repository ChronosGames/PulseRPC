using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceDiscovery;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PulseRPC.LoadBalancing;

namespace PulseRPC.Client.ServiceDiscovery
{
    /// <summary>
    /// 服务发现客户端 - 整合服务发现和负载均衡功能
    /// </summary>
    public class ServiceDiscoveryClient : IDisposable
    {
        private readonly IServiceDiscovery _serviceDiscovery;
        private readonly ILoadBalancer _loadBalancer;
        private readonly IHealthChecker? _healthChecker;
        private readonly ClientOptions _options;
        private readonly ILogger<ServiceDiscoveryClient> _logger;

        // 服务端点缓存
        private readonly ConcurrentDictionary<string, CachedServiceInfo> _serviceCache = new();

        // 监听取消令牌
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _watchCancellations = new();

        // 健康检查定时器
        private readonly Timer? _healthCheckTimer;

        // 缓存清理定时器
        private readonly Timer? _cacheCleanupTimer;

        private bool _disposed;

        public ServiceDiscoveryClient(
            IServiceDiscovery serviceDiscovery,
            ILoadBalancer loadBalancer,
            IOptions<ClientOptions> options,
            ILogger<ServiceDiscoveryClient> logger,
            IHealthChecker? healthChecker = null)
        {
            _serviceDiscovery = serviceDiscovery;
            _loadBalancer = loadBalancer;
            _healthChecker = healthChecker;
            _options = options.Value;
            _logger = logger;

            // 初始化健康检查定时器
            if (_options.LoadBalancingOptions.EnableHealthCheck && _healthChecker != null)
            {
                _healthCheckTimer = new Timer(PerformHealthCheck, null,
                    _options.LoadBalancingOptions.HealthCheckInterval,
                    _options.LoadBalancingOptions.HealthCheckInterval);
            }

            // 初始化缓存清理定时器
            if (_options.ServiceDiscoveryOptions.EnableCaching)
            {
                _cacheCleanupTimer = new Timer(CleanupExpiredCache, null,
                    _options.ServiceDiscoveryOptions.CacheTimeout,
                    _options.ServiceDiscoveryOptions.CacheTimeout);
            }

            _logger.LogInformation("ServiceDiscoveryClient 已初始化，服务发现: {ServiceDiscoveryEnabled}, 负载均衡: {LoadBalancingEnabled}, 健康检查: {HealthCheckEnabled}",
                _options.ServiceDiscoveryOptions.Enabled,
                _options.LoadBalancingOptions.Enabled,
                _options.LoadBalancingOptions.EnableHealthCheck);
        }

        /// <summary>
        /// 获取服务端点 (带负载均衡)
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="context">负载均衡上下文</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>选中的服务端点</returns>
        public async Task<ServiceEndpoint?> GetServiceEndpointAsync(
            string serviceName,
            LoadBalancingContext? context = null,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceDiscoveryClient));

            context ??= new LoadBalancingContext();

            try
            {
                // 获取服务端点列表
                var endpoints = await GetServiceEndpointsAsync(serviceName, cancellationToken);

                if (endpoints.Count == 0)
                {
                    _logger.LogWarning("服务 {ServiceName} 没有可用的端点", serviceName);
                    return null;
                }

                // 过滤健康的端点
                var healthyEndpoints = _options.LoadBalancingOptions.EnableHealthCheck
                    ? endpoints.Where(e => e.HealthStatus == HealthStatus.Healthy || e.HealthStatus == HealthStatus.Unknown).ToList()
                    : endpoints.ToList();

                if (healthyEndpoints.Count == 0)
                {
                    _logger.LogWarning("服务 {ServiceName} 没有健康的端点，使用所有端点", serviceName);
                    healthyEndpoints = endpoints.ToList();
                }

                // 使用负载均衡器选择端点
                if (_options.LoadBalancingOptions.Enabled)
                {
                    var selectedEndpoint = await _loadBalancer.SelectAsync(healthyEndpoints, context);
                    if (selectedEndpoint != null)
                    {
                        _logger.LogDebug("负载均衡选择端点: {ServiceName} -> {EndPoint}", serviceName, selectedEndpoint.EndPoint);
                    }
                    return selectedEndpoint;
                }

                // 如果负载均衡未启用，返回第一个健康的端点
                var firstEndpoint = healthyEndpoints.FirstOrDefault();
                if (firstEndpoint != null)
                {
                    _logger.LogDebug("选择第一个端点: {ServiceName} -> {EndPoint}", serviceName, firstEndpoint.EndPoint);
                }
                return firstEndpoint;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取服务 {ServiceName} 端点失败", serviceName);
                return null;
            }
        }

        /// <summary>
        /// 获取服务端点列表
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>服务端点列表</returns>
        public async Task<IReadOnlyList<ServiceEndpoint>> GetServiceEndpointsAsync(
            string serviceName,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceDiscoveryClient));

            if (!_options.ServiceDiscoveryOptions.Enabled)
            {
                return GetStaticEndpoints(serviceName);
            }

            // 检查缓存
            if (_options.ServiceDiscoveryOptions.EnableCaching &&
                _serviceCache.TryGetValue(serviceName, out var cachedInfo))
            {
                if (DateTime.UtcNow - cachedInfo.LastUpdated < _options.ServiceDiscoveryOptions.CacheTimeout)
                {
                    _logger.LogDebug("从缓存返回服务 {ServiceName} 端点，共 {Count} 个", serviceName, cachedInfo.Endpoints.Count);
                    return cachedInfo.Endpoints;
                }
            }

            try
            {
                // 从服务发现获取最新端点
                var endpoints = await _serviceDiscovery.DiscoverAsync(serviceName, cancellationToken);

                // 应用标签过滤
                if (_options.ServiceDiscoveryOptions.TagFilters.Count > 0)
                {
                    endpoints = await _serviceDiscovery.DiscoverByTagsAsync(serviceName, _options.ServiceDiscoveryOptions.TagFilters, cancellationToken);
                }

                // 更新缓存
                if (_options.ServiceDiscoveryOptions.EnableCaching)
                {
                    var newCachedInfo = new CachedServiceInfo
                    {
                        Endpoints = endpoints,
                        LastUpdated = DateTime.UtcNow
                    };
                    _serviceCache.AddOrUpdate(serviceName, newCachedInfo, (_, _) => newCachedInfo);
                }

                // 启动服务监听 (如果启用且未监听)
                if (_options.ServiceDiscoveryOptions.EnableWatch && !_watchCancellations.ContainsKey(serviceName))
                {
                    StartWatching(serviceName);
                }

                _logger.LogDebug("从服务发现获取服务 {ServiceName} 端点，共 {Count} 个", serviceName, endpoints.Count);
                return endpoints;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从服务发现获取服务 {ServiceName} 端点失败", serviceName);

                // 返回缓存的端点 (如果有)
                if (_serviceCache.TryGetValue(serviceName, out cachedInfo))
                {
                    _logger.LogWarning("使用过期缓存返回服务 {ServiceName} 端点", serviceName);
                    return cachedInfo.Endpoints;
                }

                // 降级到静态配置
                return GetStaticEndpoints(serviceName);
            }
        }

        /// <summary>
        /// 根据标签获取服务端点
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="tags">标签过滤条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>符合条件的服务端点列表</returns>
        public async Task<IReadOnlyList<ServiceEndpoint>> GetServiceEndpointsByTagsAsync(
            string serviceName,
            Dictionary<string, string> tags,
            CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceDiscoveryClient));

            if (!_options.ServiceDiscoveryOptions.Enabled)
            {
                // 静态端点不支持标签过滤
                return GetStaticEndpoints(serviceName);
            }

            try
            {
                var endpoints = await _serviceDiscovery.DiscoverByTagsAsync(serviceName, tags, cancellationToken);
                _logger.LogDebug("根据标签过滤服务 {ServiceName} 端点，共 {Count} 个", serviceName, endpoints.Count);
                return endpoints;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据标签获取服务 {ServiceName} 端点失败", serviceName);
                return Array.Empty<ServiceEndpoint>();
            }
        }

        /// <summary>
        /// 报告请求结果
        /// </summary>
        /// <param name="endpoint">使用的端点</param>
        /// <param name="result">请求结果</param>
        /// <param name="responseTime">响应时间</param>
        public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            if (_disposed) return;

            try
            {
                if (_options.LoadBalancingOptions.Enabled)
                {
                    _loadBalancer.ReportResult(endpoint, result, responseTime);
                }

                // 更新端点健康状态
                UpdateEndpointHealth(endpoint, result);

                _logger.LogDebug("报告请求结果: {EndPoint} -> {Result}, 响应时间: {ResponseTime}ms",
                    endpoint.EndPoint, result, responseTime.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "报告请求结果失败: {EndPoint}", endpoint.EndPoint);
            }
        }

        /// <summary>
        /// 监听服务变化
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>服务端点变化流</returns>
        public async IAsyncEnumerable<ServiceEndpoint[]> WatchServiceAsync(
            string serviceName,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_disposed) yield break;

            if (!_options.ServiceDiscoveryOptions.Enabled || !_options.ServiceDiscoveryOptions.EnableWatch)
            {
                _logger.LogWarning("服务监听功能未启用: {ServiceName}", serviceName);
                yield break;
            }

            await foreach (var endpoints in _serviceDiscovery.WatchAsync(serviceName, cancellationToken))
            {
                var endpointArray = Array.Empty<ServiceEndpoint>();
                try
                {
                    // 应用标签过滤
                    var filteredEndpoints = endpoints.AsEnumerable();
                    if (_options.ServiceDiscoveryOptions.TagFilters.Count > 0)
                    {
                        filteredEndpoints = endpoints.Where(e =>
                            _options.ServiceDiscoveryOptions.TagFilters.All(filter =>
                                e.Tags.TryGetValue(filter.Key, out var value) && value == filter.Value));
                    }

                    endpointArray = filteredEndpoints.ToArray();

                    // 更新缓存
                    if (_options.ServiceDiscoveryOptions.EnableCaching)
                    {
                        var cachedInfo = new CachedServiceInfo
                        {
                            Endpoints = endpointArray,
                            LastUpdated = DateTime.UtcNow
                        };
                        _serviceCache.AddOrUpdate(serviceName, cachedInfo, (_, _) => cachedInfo);
                    }

                    _logger.LogDebug("服务 {ServiceName} 端点发生变化，当前 {Count} 个端点", serviceName, endpointArray.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理服务变化事件失败: {ServiceName}", serviceName);
                }

                if (endpointArray.Length > 0)
                {
                    yield return endpointArray;
                }
            }
        }

        /// <summary>
        /// 获取服务统计信息
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <returns>服务统计信息</returns>
        public ServiceStatistics GetServiceStatistics(string serviceName)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceDiscoveryClient));

            var stats = new ServiceStatistics
            {
                ServiceName = serviceName,
                LastUpdated = DateTime.UtcNow,
                TotalEndpoints = 0,
                HealthyEndpoints = 0,
                UnhealthyEndpoints = 0
            };

            if (_serviceCache.TryGetValue(serviceName, out var cachedInfo))
            {
                stats.TotalEndpoints = cachedInfo.Endpoints.Count;
                stats.HealthyEndpoints = cachedInfo.Endpoints.Count(e => e.HealthStatus == HealthStatus.Healthy);
                stats.UnhealthyEndpoints = cachedInfo.Endpoints.Count(e => e.HealthStatus == HealthStatus.Unhealthy);
                stats.LastUpdated = cachedInfo.LastUpdated;
            }

            // 获取负载均衡器统计信息
            if (_options.LoadBalancingOptions.Enabled)
            {
                try
                {
                    var lbStats = _loadBalancer.GetStatistics();
                    stats.LoadBalancingStatistics = lbStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString() ?? "");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "获取负载均衡统计信息失败: {ServiceName}", serviceName);
                }
            }

            return stats;
        }

        /// <summary>
        /// 清除服务缓存
        /// </summary>
        /// <param name="serviceName">服务名称，为空则清除所有缓存</param>
        public void ClearCache(string? serviceName = null)
        {
            if (_disposed) return;

            try
            {
                if (string.IsNullOrEmpty(serviceName))
                {
                    _serviceCache.Clear();
                    _logger.LogInformation("已清除所有服务缓存");
                }
                else
                {
                    _serviceCache.TryRemove(serviceName, out _);
                    _logger.LogInformation("已清除服务缓存: {ServiceName}", serviceName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清除服务缓存失败: {ServiceName}", serviceName);
            }
        }

        /// <summary>
        /// 手动刷新服务端点
        /// </summary>
        /// <param name="serviceName">服务名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>刷新后的端点列表</returns>
        public async Task<IReadOnlyList<ServiceEndpoint>> RefreshServiceAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceDiscoveryClient));

            // 先清除缓存
            _serviceCache.TryRemove(serviceName, out _);

            // 重新获取端点
            return await GetServiceEndpointsAsync(serviceName, cancellationToken);
        }

        #region Private Methods

        /// <summary>
        /// 启动服务监听
        /// </summary>
        private void StartWatching(string serviceName)
        {
            if (_watchCancellations.ContainsKey(serviceName))
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _watchCancellations.TryAdd(serviceName, cts);

            Task.Run(async () =>
            {
                try
                {
                    await foreach (var endpoints in WatchServiceAsync(serviceName, cts.Token))
                    {
                        // 监听逻辑在 WatchServiceAsync 中已处理
                    }
                }
                catch (OperationCanceledException)
                {
                    // 预期的取消操作
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "监听服务失败: {ServiceName}", serviceName);
                }
                finally
                {
                    _watchCancellations.TryRemove(serviceName, out _);
                }
            }, cts.Token);

            _logger.LogDebug("已启动服务监听: {ServiceName}", serviceName);
        }

        /// <summary>
        /// 获取静态配置的端点
        /// </summary>
        private IReadOnlyList<ServiceEndpoint> GetStaticEndpoints(string serviceName)
        {
            if (!_options.ServiceDiscoveryOptions.StaticEndpoints.TryGetValue(serviceName, out var endpointStrings))
            {
                _logger.LogWarning("服务 {ServiceName} 未在静态配置中找到", serviceName);
                return Array.Empty<ServiceEndpoint>();
            }

            var endpoints = new List<ServiceEndpoint>();
            foreach (var endpointStr in endpointStrings)
            {
                if (TryParseEndpoint(endpointStr, serviceName, out var endpoint))
                {
                    endpoints.Add(endpoint);
                }
            }

            _logger.LogDebug("从静态配置获取服务 {ServiceName} 端点，共 {Count} 个", serviceName, endpoints.Count);
            return endpoints.AsReadOnly();
        }

        /// <summary>
        /// 尝试解析端点字符串
        /// </summary>
        private bool TryParseEndpoint(string endpointStr, string serviceName, out ServiceEndpoint endpoint)
        {
            endpoint = null!;

            try
            {
                if (!endpointStr.Contains(':'))
                {
                    return false;
                }

                var parts = endpointStr.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
                {
                    return false;
                }

                endpoint = new ServiceEndpoint
                {
                    ServiceId = $"{serviceName}-{endpointStr}",
                    ServiceName = serviceName,
                    EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(parts[0]), port),
                    HealthStatus = HealthStatus.Unknown,
                    Tags = new Dictionary<string, string>
                    {
                        ["source"] = "static"
                    }
                };

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析端点字符串失败: {EndpointStr}", endpointStr);
                return false;
            }
        }

        /// <summary>
        /// 更新端点健康状态
        /// </summary>
        private void UpdateEndpointHealth(ServiceEndpoint endpoint, LoadBalancingResult result)
        {
            try
            {
                var newStatus = result switch
                {
                    LoadBalancingResult.Success => HealthStatus.Healthy,
                    LoadBalancingResult.ConnectionFailed => HealthStatus.Unhealthy,
                    LoadBalancingResult.ServerError => HealthStatus.Unhealthy,
                    LoadBalancingResult.ClientError => HealthStatus.Unhealthy,
                    LoadBalancingResult.Timeout => HealthStatus.Unhealthy,
                    _ => HealthStatus.Unknown
                };

                // 更新缓存中的端点健康状态
                foreach (var cachedInfo in _serviceCache.Values)
                {
                    var cachedEndpoint = cachedInfo.Endpoints.FirstOrDefault(e => e.ServiceId == endpoint.ServiceId);
                    if (cachedEndpoint == null)
                    {
                        continue;
                    }

                    cachedEndpoint.HealthStatus = newStatus;
                    cachedEndpoint.LastUpdatedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "更新端点健康状态失败: {EndPoint}", endpoint.EndPoint);
            }
        }

        /// <summary>
        /// 执行健康检查
        /// </summary>
        private async void PerformHealthCheck(object? state)
        {
            if (_disposed || _healthChecker == null) return;

            try
            {
                var allEndpoints = _serviceCache.Values
                    .SelectMany(cache => cache.Endpoints)
                    .Distinct()
                    .ToList();

                if (allEndpoints.Count == 0)
                {
                    return;
                }

                _logger.LogDebug("开始执行健康检查，端点数量: {Count}", allEndpoints.Count);

                var healthResults = await _healthChecker.CheckHealthBatchAsync(allEndpoints);

                foreach (var result in healthResults)
                {
                    // 更新缓存中的健康状态
                    foreach (var cachedInfo in _serviceCache.Values)
                    {
                        var endpoint = cachedInfo.Endpoints.FirstOrDefault(e => e.ServiceId == result.Value.ServiceId);
                        if (endpoint != null)
                        {
                            endpoint.HealthStatus = result.Value.Status;
                            endpoint.LastUpdatedAt = DateTime.UtcNow;
                        }
                    }
                }

                var healthyCount = healthResults.Count(r => r.Value.Status == HealthStatus.Healthy);
                _logger.LogDebug("健康检查完成，健康端点: {Healthy}/{Total}", healthyCount, healthResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行健康检查失败");
            }
        }

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        private void CleanupExpiredCache(object? state)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = new List<string>();

                foreach (var kvp in _serviceCache)
                {
                    if (now - kvp.Value.LastUpdated > _options.ServiceDiscoveryOptions.CacheTimeout)
                    {
                        expiredKeys.Add(kvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    _serviceCache.TryRemove(key, out _);
                }

                if (expiredKeys.Count > 0)
                {
                    _logger.LogDebug("清理过期缓存，清理数量: {Count}", expiredKeys.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理过期缓存失败");
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // 停止所有监听
            foreach (var cts in _watchCancellations.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _watchCancellations.Clear();

            // 释放定时器
            _healthCheckTimer?.Dispose();
            _cacheCleanupTimer?.Dispose();

            // 清除缓存
            _serviceCache.Clear();

            _logger.LogInformation("ServiceDiscoveryClient 已释放资源");
        }

        #endregion
    }

    /// <summary>
    /// 缓存的服务信息
    /// </summary>
    internal class CachedServiceInfo
    {
        public IReadOnlyList<ServiceEndpoint> Endpoints { get; set; } = Array.Empty<ServiceEndpoint>();
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// 服务统计信息
    /// </summary>
    public class ServiceStatistics
    {
        /// <summary>
        /// 服务名称
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// 总端点数
        /// </summary>
        public int TotalEndpoints { get; set; }

        /// <summary>
        /// 健康端点数
        /// </summary>
        public int HealthyEndpoints { get; set; }

        /// <summary>
        /// 不健康端点数
        /// </summary>
        public int UnhealthyEndpoints { get; set; }

        /// <summary>
        /// 负载均衡统计信息
        /// </summary>
        public Dictionary<string, string> LoadBalancingStatistics { get; set; } = new();
    }
}
