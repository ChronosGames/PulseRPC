// using Microsoft.Extensions.Logging;
using PulseRPC.ServiceDiscovery;
// using Microsoft.Extensions.Options;
// using System.Collections.Concurrent;
// using PulseRPC.Caching;
// using PulseRPC.LoadBalancing;
//
// namespace PulseRPC.ServiceDiscovery;
//
// /// <summary>
// /// 服务发现客户端实现
// /// </summary>
// public class ServiceDiscoveryClient(
//     IServiceDiscovery innerDiscovery,
//     ICacheService cache,
//     ILoadBalancer loadBalancer,
//     IOptions<ServiceDiscoveryOptions> options,
//     ILogger<ServiceDiscoveryClient> logger)
//     : IServiceDiscovery, IDisposable
// {
//     private readonly IServiceDiscovery _innerDiscovery = innerDiscovery ?? throw new ArgumentNullException(nameof(innerDiscovery));
//     private readonly ICacheService _cache = cache ?? throw new ArgumentNullException(nameof(cache));
//     private readonly ILoadBalancer _loadBalancer = loadBalancer ?? throw new ArgumentNullException(nameof(loadBalancer));
//     private readonly ILogger<ServiceDiscoveryClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
//     private readonly ServiceDiscoveryOptions _options = options.Value ?? throw new ArgumentNullException(nameof(options));
//     private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceEndpoint?>> _pendingRequests = new();
//     private readonly CancellationTokenSource _cancellationTokenSource = new();
//     private bool _disposed;
//
//     /// <inheritdoc/>
//     public async Task<ServiceEndpoint?> DiscoverAsync(string serviceName, CancellationToken cancellationToken = default)
//     {
//         if (string.IsNullOrWhiteSpace(serviceName))
//             throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
//
//         try
//         {
//             _logger.LogDebug("Discovering service: {ServiceName}", serviceName);
//
//             // 首先尝试从缓存获取
//             var cachedEndpoints = await _cache.GetAsync(serviceName, cancellationToken);
//             if (cachedEndpoints?.Count > 0)
//             {
//                 _logger.LogDebug("Found {Count} cached endpoints for service: {ServiceName}", cachedEndpoints.Count, serviceName);
//
//                 // 使用负载均衡器选择端点
//                 var selectedEndpoint = await _loadBalancer.SelectAsync(
//                     cachedEndpoints.Where(e => e.IsHealthy).ToList(),
//                     LoadBalancingContext.Default(),
//                     cancellationToken);
//
//                 if (selectedEndpoint != null)
//                 {
//                     _logger.LogDebug("Selected endpoint from cache: {Endpoint}", selectedEndpoint.Address);
//                     return selectedEndpoint;
//                 }
//             }
//
//             // 如果缓存中没有或没有健康的端点，从底层发现服务中获取
//             var endpoints = await _innerDiscovery.DiscoverAllAsync(serviceName, cancellationToken);
//             if (endpoints?.Count > 0)
//             {
//                 _logger.LogDebug("Discovered {Count} endpoints for service: {ServiceName}", endpoints.Count, serviceName);
//
//                 // 更新缓存
//                 await _cache.SetAsync(serviceName, endpoints, _options.CacheOptions.DefaultTtl, cancellationToken);
//
//                 // 使用负载均衡器选择端点
//                 var selectedEndpoint = await _loadBalancer.SelectAsync(
//                     endpoints.Where(e => e.IsHealthy).ToList(),
//                     LoadBalancingContext.Default(),
//                     cancellationToken);
//
//                 if (selectedEndpoint != null)
//                 {
//                     _logger.LogDebug("Selected endpoint from discovery: {Endpoint}", selectedEndpoint.Address);
//                     return selectedEndpoint;
//                 }
//             }
//
//             _logger.LogWarning("No healthy endpoints found for service: {ServiceName}", serviceName);
//             return null;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error discovering service: {ServiceName}", serviceName);
//             throw;
//         }
//     }
//
//     /// <inheritdoc/>
//     public async Task<IReadOnlyList<ServiceEndpoint>> DiscoverAllAsync(string serviceName, CancellationToken cancellationToken = default)
//     {
//         if (string.IsNullOrWhiteSpace(serviceName))
//             throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
//
//         try
//         {
//             _logger.LogDebug("Discovering all endpoints for service: {ServiceName}", serviceName);
//
//             // 首先尝试从缓存获取
//             var cachedEndpoints = await _cache.GetAsync(serviceName, cancellationToken);
//             if (cachedEndpoints?.Count > 0)
//             {
//                 _logger.LogDebug("Found {Count} cached endpoints for service: {ServiceName}", cachedEndpoints.Count, serviceName);
//                 return cachedEndpoints;
//             }
//
//             // 如果缓存中没有，从底层发现服务中获取
//             var endpoints = await _innerDiscovery.DiscoverAllAsync(serviceName, cancellationToken);
//             if (endpoints?.Count > 0)
//             {
//                 _logger.LogDebug("Discovered {Count} endpoints for service: {ServiceName}", endpoints.Count, serviceName);
//
//                 // 更新缓存
//                 await _cache.SetAsync(serviceName, endpoints, _options.CacheOptions.DefaultTtl, cancellationToken);
//                 return endpoints;
//             }
//
//             _logger.LogWarning("No endpoints found for service: {ServiceName}", serviceName);
//             return Array.Empty<ServiceEndpoint>();
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error discovering all endpoints for service: {ServiceName}", serviceName);
//             throw;
//         }
//     }
//
//     /// <inheritdoc/>
//     public async Task<bool> IsAvailableAsync(string serviceName, CancellationToken cancellationToken = default)
//     {
//         if (string.IsNullOrWhiteSpace(serviceName))
//             throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
//
//         try
//         {
//             var endpoint = await DiscoverAsync(serviceName, cancellationToken);
//             return endpoint != null;
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError(ex, "Error checking service availability: {ServiceName}", serviceName);
//             return false;
//         }
//     }
//
//     /// <inheritdoc/>
//     public async IAsyncEnumerable<ServiceEvent> WatchAsync(
//         string serviceName,
//         [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
//     {
//         if (string.IsNullOrWhiteSpace(serviceName))
//             throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
//
//         _logger.LogDebug("Starting to watch service: {ServiceName}", serviceName);
//
//         using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
//             cancellationToken, _cancellationTokenSource.Token);
//
//         await foreach (var serviceEvent in _innerDiscovery.WatchAsync(serviceName, combinedCts.Token))
//         {
//             // 当收到服务变化事件时，清除缓存
//             await _cache.RemoveAsync(serviceName, CancellationToken.None);
//
//             _logger.LogDebug("Received service event for {ServiceName}: {EventType}",
//                 serviceName, serviceEvent.GetType().Name);
//
//             yield return serviceEvent;
//         }
//     }
//
//     /// <summary>
//     /// 报告端点使用结果
//     /// </summary>
//     /// <param name="endpoint">端点</param>
//     /// <param name="success">是否成功</param>
//     /// <param name="responseTime">响应时间</param>
//     public void ReportEndpointResult(ServiceEndpoint endpoint, bool success, TimeSpan responseTime)
//     {
//         if (endpoint == null) return;
//
//         var result = success ?
//             Abstractions.Enums.LoadBalancingResult.Success :
//             Abstractions.Enums.LoadBalancingResult.Failed;
//
//         _loadBalancer.ReportResult(endpoint, result, responseTime);
//
//         _logger.LogDebug("Reported endpoint result: {Endpoint}, Success: {Success}, ResponseTime: {ResponseTime}ms",
//             endpoint.Address, success, responseTime.TotalMilliseconds);
//     }
//
//     /// <summary>
//     /// 清除指定服务的缓存
//     /// </summary>
//     /// <param name="serviceName">服务名称</param>
//     /// <param name="cancellationToken">取消令牌</param>
//     public async Task ClearCacheAsync(string serviceName, CancellationToken cancellationToken = default)
//     {
//         if (string.IsNullOrWhiteSpace(serviceName)) return;
//
//         await _cache.RemoveAsync(serviceName, cancellationToken);
//         _logger.LogDebug("Cleared cache for service: {ServiceName}", serviceName);
//     }
//
//     /// <summary>
//     /// 预热缓存
//     /// </summary>
//     /// <param name="serviceNames">服务名称列表</param>
//     /// <param name="cancellationToken">取消令牌</param>
//     public async Task WarmupCacheAsync(IEnumerable<string> serviceNames, CancellationToken cancellationToken = default)
//     {
//         var tasks = serviceNames.Select(async serviceName =>
//         {
//             try
//             {
//                 await DiscoverAllAsync(serviceName, cancellationToken);
//                 _logger.LogDebug("Warmed up cache for service: {ServiceName}", serviceName);
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogWarning(ex, "Failed to warm up cache for service: {ServiceName}", serviceName);
//             }
//         });
//
//         await Task.WhenAll(tasks);
//     }
//
//     public void Dispose()
//     {
//         if (_disposed) return;
//
//         _cancellationTokenSource.Cancel();
//         _cancellationTokenSource.Dispose();
//         _cache?.Dispose();
//
//         if (_innerDiscovery is IDisposable disposableDiscovery)
//         {
//             disposableDiscovery.Dispose();
//         }
//
//         _disposed = true;
//         _logger.LogDebug("ServiceDiscoveryClient disposed");
//     }
// }
