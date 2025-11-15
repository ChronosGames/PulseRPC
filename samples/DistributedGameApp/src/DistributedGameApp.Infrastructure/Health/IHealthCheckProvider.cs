namespace DistributedGameApp.Infrastructure.Health;

/// <summary>
/// 健康检查提供者接口
/// </summary>
public interface IHealthCheckProvider
{
    /// <summary>
    /// 执行健康检查
    /// </summary>
    /// <returns>健康检查结果</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 健康检查结果
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 健康状态描述
    /// </summary>
    public string Status { get; set; } = "Healthy";

    /// <summary>
    /// 详细信息
    /// </summary>
    public Dictionary<string, object> Details { get; set; } = new();

    /// <summary>
    /// 创建健康结果
    /// </summary>
    public static HealthCheckResult Healthy(string status = "Healthy", Dictionary<string, object>? details = null)
    {
        return new HealthCheckResult
        {
            IsHealthy = true,
            Status = status,
            Details = details ?? new()
        };
    }

    /// <summary>
    /// 创建不健康结果
    /// </summary>
    public static HealthCheckResult Unhealthy(string status, Dictionary<string, object>? details = null)
    {
        return new HealthCheckResult
        {
            IsHealthy = false,
            Status = status,
            Details = details ?? new()
        };
    }

    /// <summary>
    /// 添加详细信息
    /// </summary>
    public HealthCheckResult AddDetail(string key, object value)
    {
        Details[key] = value;
        return this;
    }
}
