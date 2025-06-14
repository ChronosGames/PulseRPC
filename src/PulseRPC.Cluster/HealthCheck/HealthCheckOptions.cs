namespace PulseRPC.HealthCheck;

/// <summary>
/// 服务端健康检查配置
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 是否启用并发健康检查
    /// </summary>
    public bool EnableConcurrentChecks { get; set; } = true;

    /// <summary>
    /// 最大并发检查数
    /// </summary>
    public int MaxConcurrentChecks { get; set; } = 50;

    /// <summary>
    /// 连续失败多少次后标记为不健康
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 连续成功多少次后标记为健康
    /// </summary>
    public int SuccessThreshold { get; set; } = 1;

    /// <summary>
    /// 是否自动移除不健康的服务
    /// </summary>
    public bool RemoveUnhealthyServices { get; set; } = false;

    /// <summary>
    /// 健康检查重试次数
    /// </summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>
    /// 健康检查重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}

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
