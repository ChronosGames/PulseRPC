using Microsoft.Extensions.Logging;
using ServiceDiscoveryEndpoint = PulseServiceDiscovery.Abstractions.Models.ServiceEndpoint;
using ServiceDiscoveryLoadBalancer = PulseServiceDiscovery.Abstractions.ILoadBalancer;
using ServiceDiscoveryLoadBalancingContext = PulseServiceDiscovery.Abstractions.Models.LoadBalancingContext;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Models;

namespace PulseRPC.ServiceDiscovery.Adapter.Services;

/// <summary>
/// PulseRPC服务发现适配器
/// </summary>
public class PulseRpcServiceDiscoveryAdapter
{
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly ServiceDiscoveryLoadBalancer? _loadBalancer;
    private readonly ILogger<PulseRpcServiceDiscoveryAdapter> _logger;

    /// <summary>
    /// 初始化PulseRPC服务发现适配器
    /// </summary>
    /// <param name="serviceDiscovery">服务发现实例</param>
    /// <param name="loadBalancer">负载均衡器（可选）</param>
    /// <param name="logger">日志记录器</param>
    public PulseRpcServiceDiscoveryAdapter(
        IServiceDiscovery serviceDiscovery,
        ServiceDiscoveryLoadBalancer? loadBalancer,
        ILogger<PulseRpcServiceDiscoveryAdapter> logger)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
        _loadBalancer = loadBalancer;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 发现服务端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端点列表</returns>
    public async Task<IReadOnlyList<PulseRpcEndpoint>> DiscoverEndpointsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 修正：使用正确的方法调用 - PulseServiceDiscovery使用DiscoverAllAsync
            var endpoints = await _serviceDiscovery.DiscoverAllAsync(serviceName, cancellationToken);

            var pulseRpcEndpoints = endpoints.Select(ConvertToPulseRpcEndpoint).ToList();

            _logger.LogDebug("Discovered {Count} PulseRPC endpoints for service: {ServiceName}",
                pulseRpcEndpoints.Count, serviceName);

            return pulseRpcEndpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover PulseRPC endpoints for service: {ServiceName}", serviceName);

            // 快速失败策略：立即抛出异常
            throw new ServiceDiscoveryException($"Failed to discover endpoints for service '{serviceName}'", ex);
        }
    }

    /// <summary>
    /// 获取最佳端点（集成负载均衡器）
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>最佳端点</returns>
    public async Task<PulseRpcEndpoint?> GetBestEndpointAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var endpoints = await DiscoverEndpointsAsync(serviceName, cancellationToken);

        if (!endpoints.Any())
        {
            _logger.LogWarning("No endpoints found for service: {ServiceName}", serviceName);
            return null;
        }

        // 如果有负载均衡器，使用负载均衡器选择
        if (_loadBalancer != null)
        {
            try
            {
                var serviceEndpoints = endpoints.Select(ConvertToServiceEndpoint).ToList();
                var selectedServiceEndpoint = await _loadBalancer.SelectAsync(
                    serviceEndpoints,
                    ServiceDiscoveryLoadBalancingContext.Default(),
                    cancellationToken);

                if (selectedServiceEndpoint != null)
                {
                    var selectedRpcEndpoint = ConvertToPulseRpcEndpoint(selectedServiceEndpoint);
                    _logger.LogDebug("Load balancer selected endpoint {Host}:{Port} for service: {ServiceName}",
                        selectedRpcEndpoint.Host, selectedRpcEndpoint.Port, serviceName);
                    return selectedRpcEndpoint;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Load balancer failed, falling back to simple selection for service: {ServiceName}", serviceName);
            }
        }

        // 简单轮询选择作为fallback
        var selectedEndpoint = endpoints.First();
        _logger.LogDebug("Selected endpoint {Host}:{Port} for service: {ServiceName}",
            selectedEndpoint.Host, selectedEndpoint.Port, serviceName);

        return selectedEndpoint;
    }

    /// <summary>
    /// 将ServiceEndpoint转换为PulseRpcEndpoint
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <returns>PulseRPC端点</returns>
    private static PulseRpcEndpoint ConvertToPulseRpcEndpoint(ServiceDiscoveryEndpoint endpoint)
    {
        var metadata = new Dictionary<string, string>();
        if (endpoint.Metadata != null)
        {
            foreach (var key in endpoint.Metadata.Keys)
            {
                var value = endpoint.Metadata.GetValue(key);
                if (value != null)
                {
                    metadata[key] = value;
                }
            }
        }

        return new PulseRpcEndpoint
        {
            Id = endpoint.Id,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Weight = endpoint.Weight,
            IsHealthy = endpoint.IsHealthy,
            Metadata = metadata
        };
    }

    /// <summary>
    /// 将PulseRpcEndpoint转换为ServiceEndpoint（用于负载均衡器）
    /// </summary>
    private static ServiceDiscoveryEndpoint ConvertToServiceEndpoint(PulseRpcEndpoint endpoint)
    {
        var metadata = new ServiceMetadata();
        foreach (var kvp in endpoint.Metadata)
        {
            metadata.SetValue(kvp.Key, kvp.Value);
        }

        // 修正：正确的HealthStatus类型转换
        var healthStatus = endpoint.IsHealthy
            ? PulseServiceDiscovery.Abstractions.Models.HealthStatus.Healthy
            : PulseServiceDiscovery.Abstractions.Models.HealthStatus.Unhealthy;

        return new ServiceDiscoveryEndpoint(
            endpoint.Id,
            "unknown", // 服务名称在这个上下文中不可用
            endpoint.Host,
            endpoint.Port,
            "tcp",
            metadata,
            healthStatus,
            endpoint.Weight);
    }
}

/// <summary>
/// PulseRPC端点信息
/// </summary>
public record PulseRpcEndpoint
{
    /// <summary>
    /// 端点ID
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// 端口号
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; init; } = 1;

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; init; } = true;

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>
    /// 获取连接地址
    /// </summary>
    /// <returns>连接地址</returns>
    public string GetConnectionString()
    {
        return $"{Host}:{Port}";
    }
}

/// <summary>
/// PulseRPC端点提供器
/// </summary>
public class PulseRpcEndpointProvider
{
    private readonly PulseRpcServiceDiscoveryAdapter _adapter;
    private readonly ILogger<PulseRpcEndpointProvider> _logger;

    public PulseRpcEndpointProvider(
        PulseRpcServiceDiscoveryAdapter adapter,
        ILogger<PulseRpcEndpointProvider> logger)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 为PulseRPC客户端提供端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接字符串</returns>
    public async Task<string?> GetEndpointAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = await _adapter.GetBestEndpointAsync(serviceName, cancellationToken);

            if (endpoint == null)
            {
                _logger.LogWarning("No available endpoint for service: {ServiceName}", serviceName);
                return null;
            }

            var connectionString = endpoint.GetConnectionString();
            _logger.LogDebug("Providing endpoint {ConnectionString} for service: {ServiceName}",
                connectionString, serviceName);

            return connectionString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get endpoint for service: {ServiceName}", serviceName);
            return null;
        }
    }

    /// <summary>
    /// 获取所有可用端点
    /// </summary>
    /// <param name="serviceName">服务名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接字符串列表</returns>
    public async Task<IReadOnlyList<string>> GetAllEndpointsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoints = await _adapter.DiscoverEndpointsAsync(serviceName, cancellationToken);

            var connectionStrings = endpoints.Select(e => e.GetConnectionString()).ToList();

            _logger.LogDebug("Found {Count} endpoints for service: {ServiceName}",
                connectionStrings.Count, serviceName);

            return connectionStrings.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all endpoints for service: {ServiceName}", serviceName);
            return Array.Empty<string>();
        }
    }
}

/// <summary>
/// 服务发现异常
/// </summary>
public class ServiceDiscoveryException : Exception
{
    public ServiceDiscoveryException(string message) : base(message) { }
    public ServiceDiscoveryException(string message, Exception innerException) : base(message, innerException) { }
}
