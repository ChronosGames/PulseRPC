using PulseServiceDiscovery.Abstractions.Enums;

namespace PulseServiceDiscovery.Client.LoadBalancing;

/// <summary>
/// 端点统计信息
/// </summary>
public class EndpointStatistics
{
    private long _requestCount;
    private long _successCount;
    private long _failureCount;
    private long _totalResponseTimeMs;
    private readonly object _lock = new();

    public long RequestCount => _requestCount;
    public long SuccessCount => _successCount;
    public long FailureCount => _failureCount;
    public double SuccessRate => _requestCount > 0 ? (double)_successCount / _requestCount : 0.0;
    public double AverageResponseTimeMs => _requestCount > 0 ? (double)_totalResponseTimeMs / _requestCount : 0.0;

    public EndpointStatistics UpdateWith(LoadBalancingResult result, TimeSpan responseTime)
    {
        lock (_lock)
        {
            _requestCount++;
            _totalResponseTimeMs += (long)responseTime.TotalMilliseconds;

            if (result == LoadBalancingResult.Success)
            {
                _successCount++;
            }
            else
            {
                _failureCount++;
            }
        }

        return this;
    }

    public Dictionary<string, object> ToDictionary()
    {
        lock (_lock)
        {
            return new Dictionary<string, object>
            {
                ["RequestCount"] = RequestCount,
                ["SuccessCount"] = SuccessCount,
                ["FailureCount"] = FailureCount,
                ["SuccessRate"] = SuccessRate,
                ["AverageResponseTimeMs"] = AverageResponseTimeMs
            };
        }
    }

    public override string ToString()
    {
        return $"Requests: {RequestCount}, Success: {SuccessCount}, Failures: {FailureCount}, " +
               $"SuccessRate: {SuccessRate:P2}, AvgResponseTime: {AverageResponseTimeMs:F2}ms";
    }
}
