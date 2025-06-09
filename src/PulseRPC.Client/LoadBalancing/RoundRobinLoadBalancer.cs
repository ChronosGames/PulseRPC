using Microsoft.Extensions.Logging;
using PulseRPC.LoadBalancing;
using PulseRPC.ServiceDiscovery;
using System.Collections.Concurrent;

namespace PulseRPC.Client.LoadBalancing;

/// <summary>
/// 轮询负载均衡器实现
/// </summary>
public class RoundRobinLoadBalancer : ILoadBalancer
{
    private readonly ILogger<RoundRobinLoadBalancer> _logger;
    private readonly ConcurrentDictionary<string, EndpointStatistics> _endpointStats = new();
    private long _currentIndex = -1;

    public LoadBalancingStrategy Strategy => LoadBalancingStrategy.RoundRobin;

    public RoundRobinLoadBalancer(ILogger<RoundRobinLoadBalancer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 选择服务端点 - 使用轮询算法
    /// </summary>
    public async Task<ServiceEndpoint?> SelectAsync(IReadOnlyList<ServiceEndpoint> endpoints,
        LoadBalancingContext context)
    {
        if (endpoints.Count == 0)
        {
            _logger.LogWarning("没有可用的服务端点");
            return null;
        }

        // 过滤健康的端点
        var healthyEndpoints = endpoints.Where(e => e.HealthStatus == HealthStatus.Healthy).ToList();

        if (healthyEndpoints.Count == 0)
        {
            _logger.LogWarning("没有健康的服务端点，使用所有端点");
            healthyEndpoints = endpoints.ToList();
        }

        // 轮询选择
        var index = Interlocked.Increment(ref _currentIndex) % healthyEndpoints.Count;
        var selectedEndpoint = healthyEndpoints[(int)index];

        // 更新统计信息
        var stats = _endpointStats.GetOrAdd(selectedEndpoint.ServiceId, _ => new EndpointStatistics());
        stats.SelectCount++;
        stats.LastSelected = DateTime.UtcNow;

        _logger.LogDebug("轮询选择端点: {ServiceId} @ {EndPoint} (索引: {Index})",
            selectedEndpoint.ServiceId, selectedEndpoint.EndPoint, index);

        return await Task.FromResult(selectedEndpoint);
    }

    /// <summary>
    /// 报告请求结果
    /// </summary>
    public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
    {
        var stats = _endpointStats.GetOrAdd(endpoint.ServiceId, _ => new EndpointStatistics());

        stats.TotalRequests++;
        stats.TotalResponseTime += responseTime;
        stats.LastResponseTime = responseTime;
        stats.LastResult = result;
        stats.LastUpdated = DateTime.UtcNow;

        switch (result)
        {
            case LoadBalancingResult.Success:
                stats.SuccessCount++;
                break;
            case LoadBalancingResult.ConnectionFailed:
                stats.ConnectionFailureCount++;
                break;
            case LoadBalancingResult.Timeout:
                stats.TimeoutCount++;
                break;
            case LoadBalancingResult.Failure:
            case LoadBalancingResult.ServiceUnavailable:
                stats.ErrorCount++;
                break;
        }

        _logger.LogDebug("端点 {ServiceId} 请求结果: {Result}, 响应时间: {ResponseTime}ms",
            endpoint.ServiceId, result, responseTime.TotalMilliseconds);
    }

    /// <summary>
    /// 重置负载均衡器状态
    /// </summary>
    public void Reset()
    {
        _currentIndex = -1;
        _endpointStats.Clear();
        _logger.LogInformation("轮询负载均衡器状态已重置");
    }

    /// <summary>
    /// 获取当前负载均衡统计信息
    /// </summary>
    public Dictionary<string, object> GetStatistics()
    {
        var stats = new Dictionary<string, object>
        {
            ["Strategy"] = Strategy.ToString(),
            ["CurrentIndex"] = _currentIndex,
            ["EndpointCount"] = _endpointStats.Count,
            ["TotalSelections"] = _endpointStats.Values.Sum(s => s.SelectCount)
        };

        var endpointStats = new Dictionary<string, object>();
        foreach (var kvp in _endpointStats)
        {
            var endpointStat = kvp.Value;
            endpointStats[kvp.Key] = new Dictionary<string, object>
            {
                ["SelectCount"] = endpointStat.SelectCount,
                ["TotalRequests"] = endpointStat.TotalRequests,
                ["SuccessCount"] = endpointStat.SuccessCount,
                ["ErrorCount"] = endpointStat.ErrorCount,
                ["ConnectionFailureCount"] = endpointStat.ConnectionFailureCount,
                ["TimeoutCount"] = endpointStat.TimeoutCount,
                ["AverageResponseTime"] = endpointStat.TotalRequests > 0
                    ? endpointStat.TotalResponseTime.TotalMilliseconds / endpointStat.TotalRequests
                    : 0,
                ["LastResponseTime"] = endpointStat.LastResponseTime.TotalMilliseconds,
                ["LastResult"] = endpointStat.LastResult.ToString(),
                ["LastSelected"] = endpointStat.LastSelected,
                ["LastUpdated"] = endpointStat.LastUpdated,
                ["SuccessRate"] = endpointStat.TotalRequests > 0
                    ? (double)endpointStat.SuccessCount / endpointStat.TotalRequests * 100
                    : 0
            };
        }

        stats["EndpointStatistics"] = endpointStats;

        return stats;
    }

    /// <summary>
    /// 端点统计信息
    /// </summary>
    private class EndpointStatistics
    {
        public long SelectCount { get; set; }
        public long TotalRequests { get; set; }
        public long SuccessCount { get; set; }
        public long ErrorCount { get; set; }
        public long ConnectionFailureCount { get; set; }
        public long TimeoutCount { get; set; }
        public TimeSpan TotalResponseTime { get; set; }
        public TimeSpan LastResponseTime { get; set; }
        public LoadBalancingResult LastResult { get; set; }
        public DateTime LastSelected { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
