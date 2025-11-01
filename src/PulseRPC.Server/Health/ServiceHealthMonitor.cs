using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PulseRPC.Scheduling;

namespace PulseRPC.Server.Health;

/// <summary>
/// 服务健康状态
/// </summary>
public enum ServiceHealthState
{
    /// <summary>健康</summary>
    Healthy,

    /// <summary>降级</summary>
    Degraded,

    /// <summary>熔断</summary>
    CircuitBroken
}

/// <summary>
/// 服务实例健康信息
/// </summary>
public sealed class ServiceHealthInfo
{
    private readonly ServiceSchedulingKey _key;
    private long _totalRequests;
    private long _failedRequests;
    private long _consecutiveFailures;
    private long _consecutiveSuccesses;
    private DateTime _lastActivity;
    private DateTime? _circuitBrokenTime;
    private ServiceHealthState _state;
    private readonly object _stateLock = new();

    public ServiceSchedulingKey Key => _key;
    public long TotalRequests => Interlocked.Read(ref _totalRequests);
    public long FailedRequests => Interlocked.Read(ref _failedRequests);
    public long ConsecutiveFailures => Interlocked.Read(ref _consecutiveFailures);
    public long ConsecutiveSuccesses => Interlocked.Read(ref _consecutiveSuccesses);
    public DateTime LastActivity => _lastActivity;
    public ServiceHealthState State
    {
        get { lock (_stateLock) { return _state; } }
        private set { lock (_stateLock) { _state = value; } }
    }
    public DateTime? CircuitBrokenTime => _circuitBrokenTime;

    /// <summary>
    /// 成功率（0-100）
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var total = TotalRequests;
            if (total == 0) return 100.0;
            var failed = FailedRequests;
            return ((total - failed) * 100.0) / total;
        }
    }

    public ServiceHealthInfo(ServiceSchedulingKey key)
    {
        _key = key;
        _state = ServiceHealthState.Healthy;
        _lastActivity = DateTime.UtcNow;
    }

    public void RecordSuccess()
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _consecutiveSuccesses);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        _lastActivity = DateTime.UtcNow;
    }

    public void RecordFailure()
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);
        Interlocked.Increment(ref _consecutiveFailures);
        Interlocked.Exchange(ref _consecutiveSuccesses, 0);
        _lastActivity = DateTime.UtcNow;
    }

    public void TripCircuitBreaker()
    {
        State = ServiceHealthState.CircuitBroken;
        _circuitBrokenTime = DateTime.UtcNow;
    }

    public void Recover()
    {
        State = ServiceHealthState.Healthy;
        _circuitBrokenTime = null;
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    public void EnterDegraded()
    {
        State = ServiceHealthState.Degraded;
    }
}

/// <summary>
/// 健康监控选项
/// </summary>
public sealed class HealthMonitorOptions
{
    /// <summary>连续失败阈值（触发熔断）</summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>降级失败阈值</summary>
    public int DegradedThreshold { get; set; } = 3;

    /// <summary>恢复成功阈值</summary>
    public int RecoveryThreshold { get; set; } = 3;

    /// <summary>熔断后的冷却期</summary>
    public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>空闲超时（清理不活跃实例）</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>健康检查间隔</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 服务实例健康监控器 - 提供基础的健康检查和熔断功能
/// </summary>
public sealed class ServiceHealthMonitor : IAsyncDisposable
{
    private readonly ConcurrentDictionary<ServiceSchedulingKey, ServiceHealthInfo> _healthStates = new();
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<ServiceHealthMonitor> _logger;
    private readonly Timer _checkTimer;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    public ServiceHealthMonitor(
        HealthMonitorOptions? options = null,
        ILogger<ServiceHealthMonitor>? logger = null)
    {
        _options = options ?? new HealthMonitorOptions();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceHealthMonitor>.Instance;

        // 定期健康检查
        _checkTimer = new Timer(
            _ => _ = PerformHealthCheckAsync(),
            null,
            _options.CheckInterval,
            _options.CheckInterval);

        _logger.LogInformation(
            "ServiceHealthMonitor initialized - FailureThreshold: {FailureThreshold}, CooldownPeriod: {CooldownPeriod}",
            _options.FailureThreshold, _options.CooldownPeriod);
    }

    /// <summary>
    /// 记录成功调用
    /// </summary>
    public void RecordSuccess(ServiceSchedulingKey key)
    {
        var health = _healthStates.GetOrAdd(key, k => new ServiceHealthInfo(k));
        health.RecordSuccess();

        // 如果之前是熔断或降级状态，检查是否可以恢复
        if (health.State != ServiceHealthState.Healthy)
        {
            if (health.ConsecutiveSuccesses >= _options.RecoveryThreshold)
            {
                health.Recover();
                _logger.LogInformation(
                    "Service instance recovered - Key: {ServiceName}:{ServiceId}, ConsecutiveSuccesses: {Count}",
                    key.ServiceName, key.ServiceId, health.ConsecutiveSuccesses);
            }
        }
    }

    /// <summary>
    /// 记录失败调用
    /// </summary>
    public void RecordFailure(ServiceSchedulingKey key, Exception? exception = null)
    {
        var health = _healthStates.GetOrAdd(key, k => new ServiceHealthInfo(k));
        health.RecordFailure();

        var consecutiveFailures = health.ConsecutiveFailures;

        // 检查是否需要降级
        if (consecutiveFailures >= _options.DegradedThreshold && health.State == ServiceHealthState.Healthy)
        {
            health.EnterDegraded();
            _logger.LogWarning(
                "Service instance degraded - Key: {ServiceName}:{ServiceId}, ConsecutiveFailures: {Failures}",
                key.ServiceName, key.ServiceId, consecutiveFailures);
        }

        // 检查是否需要熔断
        if (consecutiveFailures >= _options.FailureThreshold)
        {
            if (health.State != ServiceHealthState.CircuitBroken)
            {
                health.TripCircuitBreaker();

                _logger.LogError(exception,
                    "Circuit breaker tripped - Key: {ServiceName}:{ServiceId}, ConsecutiveFailures: {Failures}",
                    key.ServiceName, key.ServiceId, consecutiveFailures);

                // 启动自动恢复定时器
                _ = Task.Run(async () =>
                {
                    await Task.Delay(_options.CooldownPeriod, _cts.Token);
                    await AttemptRecoveryAsync(key);
                }, _cts.Token);
            }
        }
    }

    /// <summary>
    /// 检查服务实例健康状态
    /// </summary>
    public ServiceHealthState GetHealthState(ServiceSchedulingKey key)
    {
        return _healthStates.TryGetValue(key, out var health)
            ? health.State
            : ServiceHealthState.Healthy;
    }

    /// <summary>
    /// 获取服务实例健康信息
    /// </summary>
    public ServiceHealthInfo? GetHealthInfo(ServiceSchedulingKey key)
    {
        return _healthStates.TryGetValue(key, out var health) ? health : null;
    }

    /// <summary>
    /// 获取所有健康信息
    /// </summary>
    public IReadOnlyDictionary<ServiceSchedulingKey, ServiceHealthInfo> GetAllHealthInfo()
    {
        return _healthStates;
    }

    /// <summary>
    /// 手动重置熔断器
    /// </summary>
    public bool ResetCircuitBreaker(ServiceSchedulingKey key)
    {
        if (_healthStates.TryGetValue(key, out var health))
        {
            health.Recover();
            _logger.LogInformation(
                "Circuit breaker manually reset - Key: {ServiceName}:{ServiceId}",
                key.ServiceName, key.ServiceId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 定期健康检查
    /// </summary>
    private async Task PerformHealthCheckAsync()
    {
        if (_isDisposed)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var keysToRemove = new List<ServiceSchedulingKey>();

            foreach (var kvp in _healthStates)
            {
                var key = kvp.Key;
                var health = kvp.Value;

                // 清理空闲实例
                if (now - health.LastActivity > _options.IdleTimeout)
                {
                    keysToRemove.Add(key);
                }
            }

            // 移除空闲实例
            foreach (var key in keysToRemove)
            {
                if (_healthStates.TryRemove(key, out _))
                {
                    _logger.LogDebug(
                        "Removed idle service instance - Key: {ServiceName}:{ServiceId}",
                        key.ServiceName, key.ServiceId);
                }
            }

            // 记录整体健康状况
            var totalInstances = _healthStates.Count;
            var healthyCount = _healthStates.Count(x => x.Value.State == ServiceHealthState.Healthy);
            var degradedCount = _healthStates.Count(x => x.Value.State == ServiceHealthState.Degraded);
            var brokenCount = _healthStates.Count(x => x.Value.State == ServiceHealthState.CircuitBroken);

            if (totalInstances > 0)
            {
                _logger.LogInformation(
                    "Health check completed - Total: {Total}, Healthy: {Healthy}, Degraded: {Degraded}, Broken: {Broken}",
                    totalInstances, healthyCount, degradedCount, brokenCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
        }
    }

    /// <summary>
    /// 尝试恢复熔断实例
    /// </summary>
    private async Task AttemptRecoveryAsync(ServiceSchedulingKey key)
    {
        if (_isDisposed)
            return;

        try
        {
            if (!_healthStates.TryGetValue(key, out var health))
                return;

            if (health.State != ServiceHealthState.CircuitBroken)
                return;

            // 进入降级状态，允许少量请求测试恢复
            health.EnterDegraded();

            _logger.LogInformation(
                "Service instance attempting recovery - Key: {ServiceName}:{ServiceId}",
                key.ServiceName, key.ServiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during recovery attempt for key {Key}", key);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _cts.Cancel();
        await _checkTimer.DisposeAsync();
        _cts.Dispose();

        _logger.LogInformation("ServiceHealthMonitor disposed");
    }
}
