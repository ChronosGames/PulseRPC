using PulseServiceDiscovery.Abstractions.Models;

namespace PulseServiceDiscovery.Server.HealthCheck;

/// <summary>
/// 健康检查状态
/// </summary>
public class HealthCheckState
{
    private HealthStatus _currentHealth;
    private HealthStatus _previousHealth;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private DateTime _lastCheckTime;
    private readonly Lock _lock = new();

    public HealthStatus CurrentHealth
    {
        get { lock (_lock) return _currentHealth; }
    }

    public HealthStatus PreviousHealth
    {
        get { lock (_lock) return _previousHealth; }
    }

    public int ConsecutiveFailures
    {
        get { lock (_lock) return _consecutiveFailures; }
    }

    public int ConsecutiveSuccesses
    {
        get { lock (_lock) return _consecutiveSuccesses; }
    }

    public DateTime LastCheckTime
    {
        get { lock (_lock) return _lastCheckTime; }
    }

    public HealthCheckState(HealthStatus initialHealth)
    {
        _currentHealth = initialHealth;
        _previousHealth = HealthStatus.Unknown;
        _lastCheckTime = DateTime.UtcNow;
        _consecutiveFailures = initialHealth == HealthStatus.Unhealthy ? 1 : 0;
        _consecutiveSuccesses = initialHealth == HealthStatus.Healthy ? 1 : 0;
    }

    public HealthCheckState UpdateHealth(HealthStatus newHealth)
    {
        lock (_lock)
        {
            _previousHealth = _currentHealth;
            _currentHealth = newHealth;
            _lastCheckTime = DateTime.UtcNow;

            if (newHealth == HealthStatus.Healthy)
            {
                _consecutiveSuccesses++;
                _consecutiveFailures = 0;
            }
            else if (newHealth == HealthStatus.Unhealthy)
            {
                _consecutiveFailures++;
                _consecutiveSuccesses = 0;
            }
            else
            {
                // 对于Unknown或Degraded状态，重置计数器
                _consecutiveSuccesses = 0;
                _consecutiveFailures = 0;
            }
        }

        return this;
    }
}

/// <summary>
/// 健康检查结果
/// </summary>
public record HealthCheckResult
{
    /// <summary>
    /// 服务ID
    /// </summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// 当前健康状态
    /// </summary>
    public HealthStatus CurrentHealth { get; init; }

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// 连续失败次数
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// 连续成功次数
    /// </summary>
    public int ConsecutiveSuccesses { get; init; }

    /// <summary>
    /// 错误信息（如果有）
    /// </summary>
    public string? Error { get; init; }
}
