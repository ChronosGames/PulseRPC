using Microsoft.Extensions.Logging;
using PulseServiceDiscovery.Abstractions;
using PulseServiceDiscovery.Abstractions.Models;

namespace PulseRPC.ServiceDiscovery.Adapter.Services;

/// <summary>
/// PulseRPC服务发现适配器
/// </summary>
public class PulseRpcServiceDiscoveryAdapter
{
    private readonly IServiceDiscovery _serviceDiscovery;
    private readonly ILogger<PulseRpcServiceDiscoveryAdapter> _logger;

    public PulseRpcServiceDiscoveryAdapter(
        IServiceDiscovery serviceDiscovery,
        ILogger<PulseRpcServiceDiscoveryAdapter> logger)
    {
        _serviceDiscovery = serviceDiscovery ?? throw new ArgumentNullException(nameof(serviceDiscovery));
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
            var endpoints = await _serviceDiscovery.DiscoverServicesAsync(serviceName, cancellationToken);

            var pulseRpcEndpoints = endpoints.Select(ConvertToPulseRpcEndpoint).ToList();

            _logger.LogDebug("Discovered {Count} PulseRPC endpoints for service: {ServiceName}",
                pulseRpcEndpoints.Count, serviceName);

            return pulseRpcEndpoints.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover PulseRPC endpoints for service: {ServiceName}", serviceName);
            throw;
        }
    }

    /// <summary>
    /// 获取最佳端点（基于负载均衡）
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

        // 简单的轮询选择，实际应该集成PulseServiceDiscovery的负载均衡器
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
    private static PulseRpcEndpoint ConvertToPulseRpcEndpoint(ServiceEndpoint endpoint)
    {
        return new PulseRpcEndpoint
        {
            Id = endpoint.Id,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Weight = endpoint.Weight,
            IsHealthy = true, // 假设从服务发现获取的都是健康的
            Metadata = endpoint.Metadata.Properties?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? string.Empty)
                      ?? new Dictionary<string, string>()
        };
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
