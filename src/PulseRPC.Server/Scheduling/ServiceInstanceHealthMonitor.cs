using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Scheduling;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 服务实例健康监控器
/// </summary>
/// <remarks>
/// <para>
/// 负责跟踪所有服务实例的健康状态,记录请求结果并触发熔断器状态转换。
/// 使用 <see cref="CircuitBreakerPolicy"/> 实现自动隔离和恢复逻辑。
/// </para>
/// <para>
/// <strong>核心功能</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>RecordRequestResult: 记录请求成功/失败,更新健康状态</description></item>
/// <item><description>CanAcceptRequest: 判断服务实例是否可接受新请求</description></item>
/// <item><description>GetHealth: 获取服务实例的健康状态</description></item>
/// <item><description>ResetHealth: 手动重置服务实例为 Healthy 状态</description></item>
/// </list>
/// <para>
/// <strong>线程安全</strong>:
/// 使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 存储健康状态,支持高并发访问。
/// </para>
/// </remarks>
public sealed class ServiceInstanceHealthMonitor
{
    private readonly ConcurrentDictionary<ServiceSchedulingKey, ServiceInstanceHealth> _healthStates;
    private readonly CircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly ILogger<ServiceInstanceHealthMonitor> _logger;

    /// <summary>
    /// 创建服务实例健康监控器
    /// </summary>
    /// <param name="options">健康监控配置选项</param>
    /// <param name="logger">日志记录器</param>
    public ServiceInstanceHealthMonitor(
        IOptions<HealthMonitorOptions> options,
        ILogger<ServiceInstanceHealthMonitor> logger)
    {
        _circuitBreakerPolicy = new CircuitBreakerPolicy(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthStates = new ConcurrentDictionary<ServiceSchedulingKey, ServiceInstanceHealth>();

        _logger.LogInformation("ServiceInstanceHealthMonitor initialized");
    }

    /// <summary>
    /// 记录请求结果并更新健康状态
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <param name="success">请求是否成功</param>
    /// <param name="timestamp">请求时间戳 (UTC)</param>
    public void RecordRequestResult(ServiceSchedulingKey key, bool success, DateTime timestamp)
    {
        var health = _healthStates.GetOrAdd(key, CreateHealthRecord);

        // 更新统计信息
        health.TotalRequests++;
        if (success)
        {
            health.SuccessfulRequests++;
        }
        health.LastActivityUtc = timestamp;

        // 评估熔断器状态转换
        var stateChanged = _circuitBreakerPolicy.EvaluateTransition(health, success, timestamp);

        if (stateChanged)
        {
            _logger.LogInformation(
                "Service instance {ServiceName}:{ServiceId} health state changed to {NewState}",
                key.ServiceName,
                key.ServiceId,
                health.State);
        }
    }

    /// <summary>
    /// 判断服务实例是否可以接受新请求
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <returns>如果可以接受请求返回 true,否则返回 false</returns>
    public bool CanAcceptRequest(ServiceSchedulingKey key)
    {
        if (!_healthStates.TryGetValue(key, out var health))
        {
            // 首次请求,默认允许
            return true;
        }

        return health.CanAcceptRequests();
    }

    /// <summary>
    /// 获取服务实例的健康状态
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <returns>健康状态记录,如果不存在返回 null</returns>
    public ServiceInstanceHealth? GetHealth(ServiceSchedulingKey key)
    {
        _healthStates.TryGetValue(key, out var health);
        return health;
    }

    /// <summary>
    /// 手动重置服务实例的健康状态为 Healthy
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <returns>如果找到并重置成功返回 true,否则返回 false</returns>
    /// <remarks>
    /// 此方法用于运维人员通过诊断端点手动恢复隔离的服务实例
    /// </remarks>
    public bool ResetHealth(ServiceSchedulingKey key)
    {
        if (_healthStates.TryGetValue(key, out var health))
        {
            var previousState = health.State;
            health.Reset();

            _logger.LogWarning(
                "Service instance {ServiceName}:{ServiceId} health manually reset from {PreviousState} to Healthy",
                key.ServiceName,
                key.ServiceId,
                previousState);

            return true;
        }

        return false;
    }

    /// <summary>
    /// 获取所有服务实例的健康状态 (只读副本)
    /// </summary>
    /// <returns>健康状态字典</returns>
    public IReadOnlyDictionary<ServiceSchedulingKey, ServiceInstanceHealth> GetAllHealthStates()
    {
        return _healthStates;
    }

    /// <summary>
    /// 创建新的健康状态记录
    /// </summary>
    /// <param name="key">服务调度键</param>
    /// <returns>新的健康状态记录</returns>
    private ServiceInstanceHealth CreateHealthRecord(ServiceSchedulingKey key)
    {
        var health = new ServiceInstanceHealth
        {
            Key = key,
            State = HealthState.Healthy,
            LastActivityUtc = DateTime.UtcNow
        };

        _logger.LogDebug(
            "Created new health record for service instance {ServiceName}:{ServiceId}",
            key.ServiceName,
            key.ServiceId);

        return health;
    }

    /// <summary>
    /// 获取健康状态统计摘要
    /// </summary>
    /// <returns>统计摘要</returns>
    public HealthStatsSummary GetSummary()
    {
        var summary = new HealthStatsSummary();

        foreach (var health in _healthStates.Values)
        {
            summary.TotalInstances++;

            switch (health.State)
            {
                case HealthState.Healthy:
                    summary.HealthyInstances++;
                    break;
                case HealthState.Isolated:
                    summary.IsolatedInstances++;
                    break;
                case HealthState.CoolingDown:
                    summary.CoolingDownInstances++;
                    break;
                case HealthState.ProbeAllowed:
                    summary.ProbeAllowedInstances++;
                    break;
            }
        }

        return summary;
    }
}

/// <summary>
/// 健康状态统计摘要
/// </summary>
public sealed class HealthStatsSummary
{
    /// <summary>
    /// 总服务实例数
    /// </summary>
    public int TotalInstances { get; set; }

    /// <summary>
    /// 健康状态的实例数
    /// </summary>
    public int HealthyInstances { get; set; }

    /// <summary>
    /// 已隔离状态的实例数
    /// </summary>
    public int IsolatedInstances { get; set; }

    /// <summary>
    /// 冷却中状态的实例数
    /// </summary>
    public int CoolingDownInstances { get; set; }

    /// <summary>
    /// 探测允许状态的实例数
    /// </summary>
    public int ProbeAllowedInstances { get; set; }

    /// <summary>
    /// 整体健康状态
    /// </summary>
    public string OverallStatus
    {
        get
        {
            if (TotalInstances == 0) return "Unknown";
            if (IsolatedInstances == 0) return "Healthy";
            if (IsolatedInstances > TotalInstances / 2) return "Unhealthy";
            return "Degraded";
        }
    }
}
