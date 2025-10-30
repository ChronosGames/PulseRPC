// 健康检查配置已统一到 PulseRPC.Configuration.HealthCheckOptions
// 此文件仅保留 HealthCheckState 类

using System;
using PulseRPC.Client.Health;

namespace PulseRPC.HealthCheck;

/// <summary>
/// 健康检查状态
/// </summary>
public class HealthCheckState
{
    private HealthStatus _currentHealth;
    private HealthStatus _previousHealth = HealthStatus.Unknown;
    private int _consecutiveFailures;
    private int _consecutiveSuccesses;
    private DateTime _lastCheckTime = DateTime.UtcNow;
    private readonly object _lock = new object();

    /// <summary>
    /// 创建健康检查状态
    /// </summary>
    /// <param name="initialHealth">初始健康状态</param>
    public HealthCheckState(HealthStatus initialHealth)
    {
        _currentHealth = initialHealth;
        _consecutiveFailures = initialHealth == HealthStatus.Unhealthy ? 1 : 0;
        _consecutiveSuccesses = initialHealth == HealthStatus.Healthy ? 1 : 0;
    }

    public HealthStatus CurrentHealth
    {
        get
        {
            lock (_lock)
            {
                return _currentHealth;
            }
        }
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
