using Microsoft.Extensions.Logging;
using PulseRPC.Cluster.Registry;
using PulseRPC.LoadBalancing;

namespace PulseRPC.Cluster.Routing;

/// <summary>
/// 智能服务路由器实现
/// </summary>
public class ServiceRouter(
    IUnifiedServiceRegistry registry,
    IChannelLoadBalancer loadBalancer,
    ILogger<ServiceRouter> logger)
    : IServiceRouter
{
    private readonly IUnifiedServiceRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IChannelLoadBalancer _loadBalancer = loadBalancer ?? throw new ArgumentNullException(nameof(loadBalancer));
    private readonly ILogger<ServiceRouter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ServiceEndpoint?> RouteToServiceAsync(
        string serviceType,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceType))
        {
            throw new ArgumentException("Service type cannot be null or empty", nameof(serviceType));
        }

        _logger.LogDebug("Routing to service: {ServiceType} with context: {Context}",
            serviceType, context?.RequestId ?? "N/A");

        try
        {
            // 1. 发现服务实例
            var services = await DiscoverCandidateServices(serviceType, context, cancellationToken);

            if (!services.Any())
            {
                _logger.LogWarning("No candidate services found for type: {ServiceType}", serviceType);
                return null;
            }

            _logger.LogDebug("Found {Count} candidate services for type: {ServiceType}",
                services.Count, serviceType);

            // 2. 应用过滤规则
            var filteredServices = ApplyFilters(services, context);
            if (filteredServices.Count == 0)
            {
                _logger.LogWarning("No services remain after applying filters for type: {ServiceType}", serviceType);
                return null;
            }

            _logger.LogDebug("After filtering: {Count} services remain for type: {ServiceType}",
                filteredServices.Count, serviceType);

            // 3. 负载均衡选择
            var selectedService = await SelectBestService(filteredServices, context, cancellationToken);

            if (selectedService != null)
            {
                _logger.LogInformation("Routed to service: {ServiceId} on channel: {ChannelId} for type: {ServiceType}",
                    selectedService.ServiceId, selectedService.Channel.ChannelId, serviceType);
            }

            return selectedService;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route to service type: {ServiceType}", serviceType);
            throw;
        }
    }

    public async Task<ChannelEndpoint?> RouteToChannelAsync(
        string channelName,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            throw new ArgumentException("Channel name cannot be null or empty", nameof(channelName));
        }

        _logger.LogDebug("Routing to channel: {ChannelName} with context: {Context}",
            channelName, context?.RequestId ?? "N/A");

        try
        {
            // 1. 发现通道实例
            var channels = await _registry.DiscoverChannelsAsync(channelName, cancellationToken);

            if (!channels.Any())
            {
                _logger.LogWarning("No channels found for name: {ChannelName}", channelName);
                return null;
            }

            _logger.LogDebug("Found {Count} channels for name: {ChannelName}",
                channels.Count, channelName);

            // 2. 应用过滤规则
            var filteredChannels = ApplyChannelFilters(channels, context);

            if (!filteredChannels.Any())
            {
                _logger.LogWarning("No channels remain after applying filters for name: {ChannelName}", channelName);
                return null;
            }

            // 3. 负载均衡选择
            var loadBalancingContext = CreateLoadBalancingContext(context);
            var selectedChannel = await _loadBalancer.SelectChannelAsync(filteredChannels, loadBalancingContext, cancellationToken);

            if (selectedChannel != null)
            {
                _logger.LogInformation("Routed to channel: {ChannelId} for name: {ChannelName}",
                    selectedChannel.ChannelId, channelName);
            }

            return selectedChannel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route to channel: {ChannelName}", channelName);
            throw;
        }
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> RouteToMultipleServicesAsync(
        string serviceType,
        int count,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return Array.Empty<ServiceEndpoint>();
        }

        if (count == 1)
        {
            var single = await RouteToServiceAsync(serviceType, context, cancellationToken);
            return single != null ? new[] { single } : Array.Empty<ServiceEndpoint>();
        }

        _logger.LogDebug("Routing to {Count} services of type: {ServiceType}", count, serviceType);

        try
        {
            // 发现候选服务
            var services = await DiscoverCandidateServices(serviceType, context, cancellationToken);

            if (!services.Any())
            {
                return Array.Empty<ServiceEndpoint>();
            }

            // 应用过滤规则
            var filteredServices = ApplyFilters(services, context);

            if (!filteredServices.Any())
            {
                return Array.Empty<ServiceEndpoint>();
            }

            // 选择多个服务（确保不重复）
            var results = new List<ServiceEndpoint>();
            var availableServices = filteredServices.ToList();
            var loadBalancingContext = CreateLoadBalancingContext(context);

            for (int i = 0; i < count && availableServices.Any(); i++)
            {
                var selected = await _loadBalancer.SelectServiceAsync(
                    availableServices, loadBalancingContext, cancellationToken);

                if (selected != null)
                {
                    results.Add(selected);
                    availableServices.Remove(selected);
                }
            }

            _logger.LogInformation("Routed to {ActualCount}/{RequestedCount} services of type: {ServiceType}",
                results.Count, count, serviceType);

            return results.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route to multiple services of type: {ServiceType}", serviceType);
            throw;
        }
    }

    #region Private Methods

    private async Task<IReadOnlyList<ServiceEndpoint>> DiscoverCandidateServices(
        string serviceType,
        RoutingContext? context,
        CancellationToken cancellationToken)
    {
        // 优先选择健康的服务
        var healthyServices = await _registry.DiscoverHealthyServicesAsync(serviceType, cancellationToken);

        if (healthyServices.Any())
        {
            return healthyServices;
        }

        // 如果没有健康的服务，且不强制要求健康，则获取所有服务
        if (context?.Preferences?.HealthyOnly == false)
        {
            _logger.LogWarning("No healthy services found for {ServiceType}, falling back to all services", serviceType);
            return await _registry.DiscoverServicesAsync(serviceType, cancellationToken);
        }

        return Array.Empty<ServiceEndpoint>();
    }

    private List<ServiceEndpoint> ApplyFilters(
        IReadOnlyList<ServiceEndpoint> services,
        RoutingContext? context)
    {
        var filtered = services.AsEnumerable();

        // 应用标签过滤
        if (context?.ServiceTags?.Any() == true)
        {
            filtered = filtered.Where(s => MatchesTags(s.Metadata.Tags, context.ServiceTags));
        }

        // 应用通道过滤
        if (!string.IsNullOrEmpty(context?.PreferredChannel))
        {
            filtered = filtered.Where(s => s.Channel.ChannelName == context.PreferredChannel);
        }

        // 排除指定的服务
        if (context?.ExcludedServices?.Any() == true)
        {
            filtered = filtered.Where(s => !context.ExcludedServices.Contains(s.ServiceId));
        }

        // 排除指定的通道
        if (context?.ExcludedChannels?.Any() == true)
        {
            filtered = filtered.Where(s => !context.ExcludedChannels.Contains(s.Channel.ChannelId));
        }

        // 应用偏好设置过滤
        if (context?.Preferences != null)
        {
            filtered = ApplyPreferenceFilters(filtered, context.Preferences);
        }

        return filtered.ToList();
    }

    private List<ChannelEndpoint> ApplyChannelFilters(
        IReadOnlyList<ChannelEndpoint> channels,
        RoutingContext? context)
    {
        var filtered = channels.AsEnumerable();

        // 排除指定的通道
        if (context?.ExcludedChannels?.Any() == true)
        {
            filtered = filtered.Where(c => !context.ExcludedChannels.Contains(c.ChannelId));
        }

        // 应用偏好设置过滤
        if (context?.Preferences != null)
        {
            filtered = ApplyChannelPreferenceFilters(filtered, context.Preferences);
        }

        return filtered.ToList();
    }

    private IEnumerable<ServiceEndpoint> ApplyPreferenceFilters(
        IEnumerable<ServiceEndpoint> services,
        RoutingPreferences preferences)
    {
        // 协议偏好
        if (preferences.PreferredProtocol.HasValue)
        {
            services = services.Where(s => s.Channel.Protocol == preferences.PreferredProtocol.Value);
        }

        // TLS要求
        if (preferences.RequireTls)
        {
            services = services.Where(s => s.Channel.Address.UseTls);
        }

        // 最小权重要求
        if (preferences.MinWeight.HasValue)
        {
            services = services.Where(s => s.Channel.Weight >= preferences.MinWeight.Value);
        }

        // 版本偏好
        if (!string.IsNullOrEmpty(preferences.PreferredVersion))
        {
            services = services.Where(s => s.Metadata.Version == preferences.PreferredVersion);
        }

        // 地理位置偏好
        if (!string.IsNullOrEmpty(preferences.PreferredRegion))
        {
            services = services.Where(s =>
                s.Metadata.Tags.TryGetValue("region", out var region) &&
                region == preferences.PreferredRegion);
        }

        return services;
    }

    private IEnumerable<ChannelEndpoint> ApplyChannelPreferenceFilters(
        IEnumerable<ChannelEndpoint> channels,
        RoutingPreferences preferences)
    {
        // 协议偏好
        if (preferences.PreferredProtocol.HasValue)
        {
            channels = channels.Where(c => c.Protocol == preferences.PreferredProtocol.Value);
        }

        // TLS要求
        if (preferences.RequireTls)
        {
            channels = channels.Where(c => c.Address.UseTls);
        }

        // 最小权重要求
        if (preferences.MinWeight.HasValue)
        {
            channels = channels.Where(c => c.Weight >= preferences.MinWeight.Value);
        }

        return channels;
    }

    private async Task<ServiceEndpoint?> SelectBestService(
        List<ServiceEndpoint> services,
        RoutingContext? context,
        CancellationToken cancellationToken)
    {
        if (services.Count == 0)
        {
            return null;
        }

        var loadBalancingContext = CreateLoadBalancingContext(context);
        return await _loadBalancer.SelectServiceAsync(services, loadBalancingContext, cancellationToken);
    }

    private LoadBalancingContext CreateLoadBalancingContext(RoutingContext? context)
    {
        return new LoadBalancingContext
        {
            RequestId = context?.RequestId ?? Guid.NewGuid().ToString(),
            Data = context?.Properties ?? new Dictionary<string, object>()
        };
    }

    private static bool MatchesTags(
        Dictionary<string, string> serviceTags,
        Dictionary<string, string> requiredTags)
    {
        return requiredTags.All(required =>
            serviceTags.TryGetValue(required.Key, out var serviceValue) &&
            serviceValue == required.Value);
    }

    #endregion
}
