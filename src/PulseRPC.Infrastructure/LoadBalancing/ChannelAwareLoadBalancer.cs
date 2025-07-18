using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using PulseRPC.Infrastructure;

namespace PulseRPC.LoadBalancing;

/// <summary>
/// 通道感知的负载均衡器
/// </summary>
public class ChannelAwareLoadBalancer : IChannelLoadBalancer
{
    private readonly ILogger<ChannelAwareLoadBalancer> _logger;
    private readonly LoadBalancingStrategy _strategy;
    private readonly ConcurrentDictionary<string, ChannelStatistics> _channelStats = new();
    private readonly ConcurrentDictionary<string, ServiceStatistics> _serviceStats = new();
    private readonly Random _random = new();
    private int _roundRobinIndex = 0;

    public LoadBalancingStrategy Strategy => _strategy;

    public ChannelAwareLoadBalancer(
        LoadBalancingStrategy strategy = LoadBalancingStrategy.RoundRobin,
        ILogger<ChannelAwareLoadBalancer>? logger = null)
    {
        _strategy = strategy;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChannelAwareLoadBalancer>.Instance;
    }

    public async Task<ServiceEndpoint?> SelectAsync(
        IReadOnlyList<ServiceEndpoint> endpoints,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default)
    {
        if (endpoints == null || endpoints.Count == 0)
        {
            _logger.LogWarning("No service endpoints available for load balancing");
            return null;
        }

        if (endpoints.Count == 1)
        {
            return endpoints[0];
        }

        return _strategy switch
        {
            LoadBalancingStrategy.RoundRobin => SelectRoundRobin(endpoints),
            LoadBalancingStrategy.WeightedRoundRobin => SelectWeightedRoundRobin(endpoints),
            LoadBalancingStrategy.Random => SelectRandom(endpoints),
            LoadBalancingStrategy.LeastConnections => await SelectLeastConnections(endpoints),
            _ => SelectRoundRobin(endpoints)
        };
    }

    public async Task<ChannelEndpoint?> SelectChannelAsync(
        IReadOnlyList<ChannelEndpoint> channels,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default)
    {
        if (channels == null || channels.Count == 0)
        {
            _logger.LogWarning("No channel endpoints available for load balancing");
            return null;
        }

        if (channels.Count == 1)
        {
            return channels[0];
        }

        return _strategy switch
        {
            LoadBalancingStrategy.RoundRobin => SelectChannelRoundRobin(channels),
            LoadBalancingStrategy.WeightedRoundRobin => SelectChannelWeightedRoundRobin(channels),
            LoadBalancingStrategy.Random => SelectChannelRandom(channels),
            LoadBalancingStrategy.LeastConnections => await SelectChannelLeastConnections(channels),
            _ => SelectChannelRoundRobin(channels)
        };
    }

    public async Task<ServiceEndpoint?> SelectServiceAsync(
        IReadOnlyList<ServiceEndpoint> services,
        LoadBalancingContext context,
        CancellationToken cancellationToken = default)
    {
        return await SelectAsync(services, context, cancellationToken);
    }

    public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (endpoint == null) return;

        ReportServiceResult(endpoint, result, responseTime);
        ReportChannelResult(endpoint.Channel, result, responseTime);
    }

    public void ReportChannelResult(ChannelEndpoint channel, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (channel == null) return;

        var stats = _channelStats.AddOrUpdate(channel.ChannelId,
            new ChannelStatistics { ChannelId = channel.ChannelId },
            (_, existing) =>
            {
                existing.UpdateStats(result, responseTime);
                return existing;
            });

        _logger.LogDebug("Updated channel statistics for {ChannelId}: {Stats}",
            channel.ChannelId, stats);
    }

    public void ReportServiceResult(ServiceEndpoint service, LoadBalancingResult result, TimeSpan responseTime)
    {
        if (service == null) return;

        var stats = _serviceStats.AddOrUpdate(service.ServiceId,
            new ServiceStatistics { ServiceId = service.ServiceId },
            (_, existing) =>
            {
                existing.UpdateStats(result, responseTime);
                return existing;
            });

        _logger.LogDebug("Updated service statistics for {ServiceId}: {Stats}",
            service.ServiceId, stats);
    }

    public void Reset()
    {
        _channelStats.Clear();
        _serviceStats.Clear();
        _roundRobinIndex = 0;
        _logger.LogDebug("Reset channel-aware load balancer state");
    }

    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["Strategy"] = Strategy.ToString(),
            ["ServiceCount"] = _serviceStats.Count,
            ["ChannelCount"] = _channelStats.Count,
            ["ServiceStatistics"] = _serviceStats.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary()),
            ["ChannelStatistics"] = _channelStats.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToDictionary())
        };
    }

    #region Private Methods - Service Selection

    private ServiceEndpoint SelectRoundRobin(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        var index = Interlocked.Increment(ref _roundRobinIndex) % endpoints.Count;
        return endpoints[index];
    }

    private ServiceEndpoint SelectWeightedRoundRobin(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        var totalWeight = endpoints.Sum(e => e.Channel.Weight);
        if (totalWeight <= 0)
        {
            return SelectRoundRobin(endpoints);
        }

        var randomWeight = _random.Next(totalWeight);
        var currentWeight = 0;

        foreach (var endpoint in endpoints)
        {
            currentWeight += endpoint.Channel.Weight;
            if (randomWeight < currentWeight)
            {
                return endpoint;
            }
        }

        return endpoints[^1];
    }

    private ServiceEndpoint SelectRandom(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        var index = _random.Next(endpoints.Count);
        return endpoints[index];
    }

    private async Task<ServiceEndpoint> SelectLeastConnections(IReadOnlyList<ServiceEndpoint> endpoints)
    {
        ServiceEndpoint? bestEndpoint = null;
        var minConnections = int.MaxValue;
        var bestScore = double.MinValue;

        foreach (var endpoint in endpoints)
        {
            var serviceStats = _serviceStats.GetValueOrDefault(endpoint.ServiceId);
            var channelStats = _channelStats.GetValueOrDefault(endpoint.Channel.ChannelId);

            var connections = (serviceStats?.ActiveConnections ?? 0) +
                             (channelStats?.ActiveConnections ?? 0);

            var score = CalculateConnectionScore(connections, endpoint.Channel.Weight);

            if (score > bestScore || (score == bestScore && connections < minConnections))
            {
                bestScore = score;
                minConnections = connections;
                bestEndpoint = endpoint;
            }
        }

        return bestEndpoint ?? endpoints[0];
    }

    #endregion

    #region Private Methods - Channel Selection

    private ChannelEndpoint SelectChannelRoundRobin(IReadOnlyList<ChannelEndpoint> channels)
    {
        var index = Interlocked.Increment(ref _roundRobinIndex) % channels.Count;
        return channels[index];
    }

    private ChannelEndpoint SelectChannelWeightedRoundRobin(IReadOnlyList<ChannelEndpoint> channels)
    {
        var totalWeight = channels.Sum(c => c.Weight);
        if (totalWeight <= 0)
        {
            return SelectChannelRoundRobin(channels);
        }

        var randomWeight = _random.Next(totalWeight);
        var currentWeight = 0;

        foreach (var channel in channels)
        {
            currentWeight += channel.Weight;
            if (randomWeight < currentWeight)
            {
                return channel;
            }
        }

        return channels[^1];
    }

    private ChannelEndpoint SelectChannelRandom(IReadOnlyList<ChannelEndpoint> channels)
    {
        var index = _random.Next(channels.Count);
        return channels[index];
    }

    private async Task<ChannelEndpoint> SelectChannelLeastConnections(IReadOnlyList<ChannelEndpoint> channels)
    {
        ChannelEndpoint? bestChannel = null;
        var minConnections = int.MaxValue;
        var bestScore = double.MinValue;

        foreach (var channel in channels)
        {
            var stats = _channelStats.GetValueOrDefault(channel.ChannelId);
            var connections = stats?.ActiveConnections ?? 0;
            var score = CalculateConnectionScore(connections, channel.Weight);

            if (score > bestScore || (score == bestScore && connections < minConnections))
            {
                bestScore = score;
                minConnections = connections;
                bestChannel = channel;
            }
        }

        return bestChannel ?? channels[0];
    }

    #endregion

    private static double CalculateConnectionScore(int connections, int weight)
    {
        var connectionPenalty = connections == 0 ? 0 : 1.0 / connections;
        return weight * 1000 + connectionPenalty * 100;
    }
}

/// <summary>
/// 通道统计信息
/// </summary>
internal class ChannelStatistics
{
    public string ChannelId { get; init; } = string.Empty;
    public int ActiveConnections { get; private set; }
    public long TotalRequests { get; private set; }
    public long SuccessfulRequests { get; private set; }
    public long FailedRequests { get; private set; }
    public TimeSpan AverageResponseTime { get; private set; }
    private readonly object _lock = new object();

    public void UpdateStats(LoadBalancingResult result, TimeSpan responseTime)
    {
        lock (_lock)
        {
            TotalRequests++;

            if (result == LoadBalancingResult.Success)
            {
                SuccessfulRequests++;
            }
            else
            {
                FailedRequests++;
            }

            // 简单的移动平均
            var totalResponseTime = AverageResponseTime.TotalMilliseconds * (TotalRequests - 1) + responseTime.TotalMilliseconds;
            AverageResponseTime = TimeSpan.FromMilliseconds(totalResponseTime / TotalRequests);
        }
    }

    public Dictionary<string, object> ToDictionary()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>
            {
                ["ChannelId"] = ChannelId,
                ["ActiveConnections"] = ActiveConnections,
                ["TotalRequests"] = TotalRequests,
                ["SuccessfulRequests"] = SuccessfulRequests,
                ["FailedRequests"] = FailedRequests,
                ["SuccessRate"] = TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0.0,
                ["AverageResponseTimeMs"] = AverageResponseTime.TotalMilliseconds
            };
        }
    }
}

/// <summary>
/// 服务统计信息
/// </summary>
internal class ServiceStatistics
{
    public string ServiceId { get; init; } = string.Empty;
    public int ActiveConnections { get; private set; }
    public long TotalRequests { get; private set; }
    public long SuccessfulRequests { get; private set; }
    public long FailedRequests { get; private set; }
    public TimeSpan AverageResponseTime { get; private set; }
    private readonly object _lock = new object();

    public void UpdateStats(LoadBalancingResult result, TimeSpan responseTime)
    {
        lock (_lock)
        {
            TotalRequests++;

            if (result == LoadBalancingResult.Success)
            {
                SuccessfulRequests++;
            }
            else
            {
                FailedRequests++;
            }

            // 简单的移动平均
            var totalResponseTime = AverageResponseTime.TotalMilliseconds * (TotalRequests - 1) + responseTime.TotalMilliseconds;
            AverageResponseTime = TimeSpan.FromMilliseconds(totalResponseTime / TotalRequests);
        }
    }

    public Dictionary<string, object> ToDictionary()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>
            {
                ["ServiceId"] = ServiceId,
                ["ActiveConnections"] = ActiveConnections,
                ["TotalRequests"] = TotalRequests,
                ["SuccessfulRequests"] = SuccessfulRequests,
                ["FailedRequests"] = FailedRequests,
                ["SuccessRate"] = TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0.0,
                ["AverageResponseTimeMs"] = AverageResponseTime.TotalMilliseconds
            };
        }
    }
}
