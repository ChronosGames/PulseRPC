namespace PulseRPC.LoadBalancing;

/// <summary>
/// 负载均衡配置选项
/// </summary>
public class LoadBalancingOptions
{
    /// <summary>
    /// 是否启用负载均衡
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public LoadBalancingStrategy Strategy { get; set; } = LoadBalancingStrategy.RoundRobin;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 健康检查超时时间
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 连续失败次数阈值（超过则标记为不健康）
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 恢复检查间隔（不健康端点的检查频率）
    /// </summary>
    public TimeSpan RecoveryCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 权重配置（用于加权负载均衡）
    /// </summary>
    public Dictionary<string, int> Weights { get; set; } = new();

    /// <summary>
    /// 是否启用慢启动（新端点逐渐增加流量）
    /// </summary>
    public bool EnableSlowStart { get; set; } = false;

    /// <summary>
    /// 慢启动持续时间
    /// </summary>
    public TimeSpan SlowStartDuration { get; set; } = TimeSpan.FromMinutes(5);
}
