using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.ServiceDiscovery;
using PulseRPC.Client.LoadBalancing;
using System.Collections.Concurrent;

namespace PulseRPC.LoadBalancing.Strategies
{
    /// <summary>
    /// 故障转移配置选项
    /// </summary>
    public class FailoverOptions
    {
        /// <summary>
        /// 配置节名称
        /// </summary>
        public const string SectionName = "Failover";

        /// <summary>
        /// 故障检测阈值 (连续失败次数)
        /// </summary>
        public int FailureThreshold { get; set; } = 3;

        /// <summary>
        /// 恢复检测阈值 (连续成功次数)
        /// </summary>
        public int RecoveryThreshold { get; set; } = 2;

        /// <summary>
        /// 故障端点检查间隔
        /// </summary>
        public TimeSpan FailedEndpointCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 断路器开启时间
        /// </summary>
        public TimeSpan CircuitBreakerOpenTime { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 半开状态下的测试请求比例 (0.0-1.0)
        /// </summary>
        public double HalfOpenTestRatio { get; set; } = 0.1;

        /// <summary>
        /// 是否启用优雅降级
        /// </summary>
        public bool EnableGracefulDegradation { get; set; } = true;

        /// <summary>
        /// 最小健康端点数量
        /// </summary>
        public int MinHealthyEndpoints { get; set; } = 1;
    }

    /// <summary>
    /// 断路器状态
    /// </summary>
    public enum CircuitBreakerState
    {
        /// <summary>
        /// 关闭状态 (正常工作)
        /// </summary>
        Closed,

        /// <summary>
        /// 开启状态 (完全失败)
        /// </summary>
        Open,

        /// <summary>
        /// 半开状态 (恢复测试)
        /// </summary>
        HalfOpen
    }

    /// <summary>
    /// 端点健康状态跟踪
    /// </summary>
    public class EndpointHealthTracker
    {
        public ServiceEndpoint Endpoint { get; init; } = null!;
        public CircuitBreakerState CircuitState { get; set; } = CircuitBreakerState.Closed;
        public int ConsecutiveFailures { get; set; } = 0;
        public int ConsecutiveSuccesses { get; set; } = 0;
        public DateTime LastFailureTime { get; set; } = DateTime.MinValue;
        public DateTime LastSuccessTime { get; set; } = DateTime.MinValue;
        public DateTime LastCheckTime { get; set; } = DateTime.MinValue;
        public TimeSpan AverageResponseTime { get; set; } = TimeSpan.Zero;
        public long TotalRequests { get; set; } = 0;
        public long SuccessfulRequests { get; set; } = 0;
        public long FailedRequests { get; set; } = 0;
        public double CurrentSuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0.0;
        public bool IsHealthy => CircuitState == CircuitBreakerState.Closed && ConsecutiveFailures == 0;
    }

    /// <summary>
    /// 故障转移负载均衡器 - 实现自动故障检测和恢复逻辑
    /// </summary>
    public class FailoverLoadBalancer : ILoadBalancer, IDisposable
    {
        private readonly FailoverOptions _options;
        private readonly ILogger<FailoverLoadBalancer> _logger;
        
        // 端点健康状态追踪
        private readonly ConcurrentDictionary<string, EndpointHealthTracker> _endpointTrackers = new();
        
        // 故障端点检查定时器
        private readonly Timer _healthCheckTimer;
        
        // 随机数生成器 (用于半开状态测试)
        private readonly Random _random = new();
        
        private bool _disposed;

        public LoadBalancingStrategy Strategy => LoadBalancingStrategy.Failover;

        public FailoverLoadBalancer(
            IOptions<FailoverOptions> options,
            ILogger<FailoverLoadBalancer> logger)
        {
            _options = options.Value;
            _logger = logger;

            // 启动故障端点检查定时器
            _healthCheckTimer = new Timer(CheckFailedEndpoints, null, 
                _options.FailedEndpointCheckInterval, _options.FailedEndpointCheckInterval);

            _logger.LogInformation("FailoverLoadBalancer 已初始化，故障阈值: {FailureThreshold}, 恢复阈值: {RecoveryThreshold}",
                _options.FailureThreshold, _options.RecoveryThreshold);
        }

        /// <summary>
        /// 选择服务端点 - 故障转移策略
        /// </summary>
        public async Task<ServiceEndpoint?> SelectAsync(IReadOnlyList<ServiceEndpoint> endpoints, LoadBalancingContext context)
        {
            if (endpoints.Count == 0)
            {
                _logger.LogWarning("没有可用的服务端点");
                return null;
            }

            // 更新端点追踪器
            UpdateEndpointTrackers(endpoints);

            // 获取健康的端点
            var healthyEndpoints = GetHealthyEndpoints(endpoints);
            
            if (healthyEndpoints.Count == 0)
            {
                _logger.LogWarning("没有健康的端点可用");
                
                // 尝试优雅降级
                if (_options.EnableGracefulDegradation)
                {
                    return TryGracefulDegradation(endpoints);
                }
                
                return null;
            }

            // 选择最佳端点
            var selectedEndpoint = SelectBestEndpoint(healthyEndpoints, context);
            
            if (selectedEndpoint != null)
            {
                _logger.LogDebug("故障转移策略选择端点: {ServiceId} @ {EndPoint}", 
                    selectedEndpoint.ServiceId, selectedEndpoint.EndPoint);
            }

            return selectedEndpoint;
        }

        /// <summary>
        /// 报告请求结果
        /// </summary>
        public void ReportResult(ServiceEndpoint endpoint, LoadBalancingResult result, TimeSpan responseTime)
        {
            var tracker = _endpointTrackers.GetOrAdd(endpoint.ServiceId, _ => new EndpointHealthTracker
            {
                Endpoint = endpoint
            });

            lock (tracker)
            {
                tracker.TotalRequests++;
                tracker.LastCheckTime = DateTime.UtcNow;

                // 更新平均响应时间
                var totalTime = tracker.AverageResponseTime.TotalMilliseconds * (tracker.TotalRequests - 1) + responseTime.TotalMilliseconds;
                tracker.AverageResponseTime = TimeSpan.FromMilliseconds(totalTime / tracker.TotalRequests);

                if (result == LoadBalancingResult.Success)
                {
                    HandleSuccessfulRequest(tracker);
                }
                else
                {
                    HandleFailedRequest(tracker, result);
                }

                // 更新断路器状态
                UpdateCircuitBreakerState(tracker);
            }

            _logger.LogDebug("端点 {ServiceId} 请求结果: {Result}, 断路器状态: {CircuitState}, 连续失败: {ConsecutiveFailures}",
                endpoint.ServiceId, result, tracker.CircuitState, tracker.ConsecutiveFailures);
        }

        /// <summary>
        /// 重置负载均衡器状态
        /// </summary>
        public void Reset()
        {
            _endpointTrackers.Clear();
            _logger.LogInformation("故障转移负载均衡器状态已重置");
        }

        /// <summary>
        /// 获取当前负载均衡统计信息
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            var healthyCount = _endpointTrackers.Values.Count(t => t.IsHealthy);
            var openCircuitCount = _endpointTrackers.Values.Count(t => t.CircuitState == CircuitBreakerState.Open);
            var halfOpenCircuitCount = _endpointTrackers.Values.Count(t => t.CircuitState == CircuitBreakerState.HalfOpen);

            var stats = new Dictionary<string, object>
            {
                ["Strategy"] = Strategy.ToString(),
                ["TotalEndpoints"] = _endpointTrackers.Count,
                ["HealthyEndpoints"] = healthyCount,
                ["UnhealthyEndpoints"] = _endpointTrackers.Count - healthyCount,
                ["OpenCircuits"] = openCircuitCount,
                ["HalfOpenCircuits"] = halfOpenCircuitCount,
                ["ClosedCircuits"] = _endpointTrackers.Count - openCircuitCount - halfOpenCircuitCount,
                ["TotalRequests"] = _endpointTrackers.Values.Sum(t => t.TotalRequests),
                ["TotalSuccessfulRequests"] = _endpointTrackers.Values.Sum(t => t.SuccessfulRequests),
                ["TotalFailedRequests"] = _endpointTrackers.Values.Sum(t => t.FailedRequests),
                ["OverallSuccessRate"] = _endpointTrackers.Values.Sum(t => t.TotalRequests) > 0 
                    ? (double)_endpointTrackers.Values.Sum(t => t.SuccessfulRequests) / _endpointTrackers.Values.Sum(t => t.TotalRequests) * 100 
                    : 0
            };

            var endpointStats = new Dictionary<string, object>();
            foreach (var kvp in _endpointTrackers)
            {
                var tracker = kvp.Value;
                endpointStats[kvp.Key] = new Dictionary<string, object>
                {
                    ["CircuitState"] = tracker.CircuitState.ToString(),
                    ["IsHealthy"] = tracker.IsHealthy,
                    ["ConsecutiveFailures"] = tracker.ConsecutiveFailures,
                    ["ConsecutiveSuccesses"] = tracker.ConsecutiveSuccesses,
                    ["TotalRequests"] = tracker.TotalRequests,
                    ["SuccessfulRequests"] = tracker.SuccessfulRequests,
                    ["FailedRequests"] = tracker.FailedRequests,
                    ["SuccessRate"] = tracker.CurrentSuccessRate * 100,
                    ["AverageResponseTime"] = tracker.AverageResponseTime.TotalMilliseconds,
                    ["LastSuccessTime"] = tracker.LastSuccessTime,
                    ["LastFailureTime"] = tracker.LastFailureTime,
                    ["LastCheckTime"] = tracker.LastCheckTime
                };
            }
            stats["EndpointStatistics"] = endpointStats;

            return stats;
        }

        #region Private Methods

        /// <summary>
        /// 更新端点追踪器
        /// </summary>
        private void UpdateEndpointTrackers(IReadOnlyList<ServiceEndpoint> endpoints)
        {
            var currentEndpointIds = endpoints.Select(e => e.ServiceId).ToHashSet();
            
            // 移除不存在的端点
            var trackersToRemove = _endpointTrackers.Keys.Where(k => !currentEndpointIds.Contains(k)).ToList();
            foreach (var key in trackersToRemove)
            {
                _endpointTrackers.TryRemove(key, out _);
            }

            // 添加新端点
            foreach (var endpoint in endpoints)
            {
                _endpointTrackers.GetOrAdd(endpoint.ServiceId, _ => new EndpointHealthTracker
                {
                    Endpoint = endpoint
                });
            }
        }

        /// <summary>
        /// 获取健康的端点
        /// </summary>
        private List<ServiceEndpoint> GetHealthyEndpoints(IReadOnlyList<ServiceEndpoint> endpoints)
        {
            var healthyEndpoints = new List<ServiceEndpoint>();

            foreach (var endpoint in endpoints)
            {
                if (_endpointTrackers.TryGetValue(endpoint.ServiceId, out var tracker))
                {
                    // 检查断路器状态
                    if (tracker.CircuitState == CircuitBreakerState.Closed ||
                        (tracker.CircuitState == CircuitBreakerState.HalfOpen && ShouldAllowHalfOpenRequest()))
                    {
                        healthyEndpoints.Add(endpoint);
                    }
                }
                else
                {
                    // 新端点默认视为健康
                    healthyEndpoints.Add(endpoint);
                }
            }

            return healthyEndpoints;
        }

        /// <summary>
        /// 选择最佳端点
        /// </summary>
        private ServiceEndpoint? SelectBestEndpoint(List<ServiceEndpoint> healthyEndpoints, LoadBalancingContext context)
        {
            if (healthyEndpoints.Count == 1)
            {
                return healthyEndpoints[0];
            }

            // 根据健康状态和性能指标排序
            return healthyEndpoints
                .OrderByDescending(e => GetEndpointScore(e))
                .ThenBy(e => GetAverageResponseTime(e))
                .FirstOrDefault();
        }

        /// <summary>
        /// 获取端点评分
        /// </summary>
        private double GetEndpointScore(ServiceEndpoint endpoint)
        {
            if (!_endpointTrackers.TryGetValue(endpoint.ServiceId, out var tracker))
            {
                return 1.0; // 新端点默认高分
            }

            var successRate = tracker.CurrentSuccessRate;
            var healthBonus = tracker.IsHealthy ? 0.2 : 0.0;
            var responseTimeBonus = tracker.AverageResponseTime.TotalMilliseconds < 100 ? 0.1 : 0.0;

            return successRate + healthBonus + responseTimeBonus;
        }

        /// <summary>
        /// 获取端点平均响应时间
        /// </summary>
        private TimeSpan GetAverageResponseTime(ServiceEndpoint endpoint)
        {
            return _endpointTrackers.TryGetValue(endpoint.ServiceId, out var tracker) 
                ? tracker.AverageResponseTime 
                : TimeSpan.Zero;
        }

        /// <summary>
        /// 处理成功请求
        /// </summary>
        private void HandleSuccessfulRequest(EndpointHealthTracker tracker)
        {
            tracker.SuccessfulRequests++;
            tracker.ConsecutiveSuccesses++;
            tracker.ConsecutiveFailures = 0;
            tracker.LastSuccessTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 处理失败请求
        /// </summary>
        private void HandleFailedRequest(EndpointHealthTracker tracker, LoadBalancingResult result)
        {
            tracker.FailedRequests++;
            tracker.ConsecutiveFailures++;
            tracker.ConsecutiveSuccesses = 0;
            tracker.LastFailureTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 更新断路器状态
        /// </summary>
        private void UpdateCircuitBreakerState(EndpointHealthTracker tracker)
        {
            switch (tracker.CircuitState)
            {
                case CircuitBreakerState.Closed:
                    if (tracker.ConsecutiveFailures >= _options.FailureThreshold)
                    {
                        tracker.CircuitState = CircuitBreakerState.Open;
                        _logger.LogWarning("端点 {ServiceId} 断路器开启，连续失败: {ConsecutiveFailures}", 
                            tracker.Endpoint.ServiceId, tracker.ConsecutiveFailures);
                    }
                    break;

                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow - tracker.LastFailureTime >= _options.CircuitBreakerOpenTime)
                    {
                        tracker.CircuitState = CircuitBreakerState.HalfOpen;
                        _logger.LogInformation("端点 {ServiceId} 断路器进入半开状态", tracker.Endpoint.ServiceId);
                    }
                    break;

                case CircuitBreakerState.HalfOpen:
                    if (tracker.ConsecutiveSuccesses >= _options.RecoveryThreshold)
                    {
                        tracker.CircuitState = CircuitBreakerState.Closed;
                        tracker.ConsecutiveFailures = 0;
                        _logger.LogInformation("端点 {ServiceId} 断路器关闭，已恢复正常", tracker.Endpoint.ServiceId);
                    }
                    else if (tracker.ConsecutiveFailures > 0)
                    {
                        tracker.CircuitState = CircuitBreakerState.Open;
                        _logger.LogWarning("端点 {ServiceId} 断路器重新开启", tracker.Endpoint.ServiceId);
                    }
                    break;
            }
        }

        /// <summary>
        /// 半开状态下是否允许请求
        /// </summary>
        private bool ShouldAllowHalfOpenRequest()
        {
            return _random.NextDouble() < _options.HalfOpenTestRatio;
        }

        /// <summary>
        /// 尝试优雅降级
        /// </summary>
        private ServiceEndpoint? TryGracefulDegradation(IReadOnlyList<ServiceEndpoint> endpoints)
        {
            if (endpoints.Count == 0) return null;

            // 选择最近失败时间最久的端点
            var candidateEndpoint = endpoints
                .Select(e => new { 
                    Endpoint = e, 
                    Tracker = _endpointTrackers.GetValueOrDefault(e.ServiceId) 
                })
                .OrderBy(x => x.Tracker?.LastFailureTime ?? DateTime.MinValue)
                .FirstOrDefault();

            if (candidateEndpoint != null)
            {
                _logger.LogWarning("启用优雅降级，选择端点: {ServiceId} @ {EndPoint}", 
                    candidateEndpoint.Endpoint.ServiceId, candidateEndpoint.Endpoint.EndPoint);
                return candidateEndpoint.Endpoint;
            }

            return null;
        }

        /// <summary>
        /// 检查故障端点 (定时器回调)
        /// </summary>
        private void CheckFailedEndpoints(object? state)
        {
            if (_disposed) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var failedTrackers = _endpointTrackers.Values
                        .Where(t => t.CircuitState == CircuitBreakerState.Open)
                        .ToList();

                    foreach (var tracker in failedTrackers)
                    {
                        // 检查是否可以尝试恢复
                        if (DateTime.UtcNow - tracker.LastFailureTime >= _options.CircuitBreakerOpenTime)
                        {
                            tracker.CircuitState = CircuitBreakerState.HalfOpen;
                            _logger.LogDebug("端点 {ServiceId} 从开启状态转换为半开状态", tracker.Endpoint.ServiceId);
                        }
                    }

                    _logger.LogDebug("已检查 {Count} 个故障端点", failedTrackers.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "检查故障端点时发生错误");
                }
            });
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _healthCheckTimer?.Dispose();

            _logger.LogInformation("FailoverLoadBalancer 已释放");
        }

        #endregion
    }
} 