using System;

namespace PulseRPC.Configuration;

/// <summary>
/// 统一健康检查配置选项
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

    /// <summary>
    /// HTTP健康检查路径（可选）
    /// </summary>
    public string? HttpPath { get; set; } = "/health";

    /// <summary>
    /// 健康检查类型（tcp、http、ping等）
    /// </summary>
    public string CheckType { get; set; } = "tcp";
}