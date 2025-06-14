namespace PulseRPC.ServiceDiscovery;

/// <summary>
/// 清理配置
/// </summary>
public class CleanupOptions
{
    /// <summary>
    /// 是否启用自动清理
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 清理间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 服务过期时间
    /// </summary>
    public TimeSpan ServiceExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 健康检查超时后自动移除
    /// </summary>
    public bool RemoveUnhealthyServices { get; set; } = true;
}
