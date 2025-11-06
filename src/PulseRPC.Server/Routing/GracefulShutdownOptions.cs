namespace PulseRPC.Server.Routing;

/// <summary>
/// 优雅关闭配置选项
/// </summary>
public class GracefulShutdownOptions
{
    /// <summary>
    /// 优雅关闭超时时间（默认30秒）
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 排空请求的最大等待时间（默认10秒）
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 保存Service状态的超时时间（默认15秒）
    /// </summary>
    public TimeSpan SaveStateTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 是否在关闭前通知客户端迁移（默认true）
    /// </summary>
    public bool NotifyClientsBeforeShutdown { get; set; } = true;

    /// <summary>
    /// 客户端迁移通知的提前时间（默认5秒）
    /// </summary>
    public TimeSpan ClientNotificationLeadTime { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 是否自动保存Service状态（默认true）
    /// </summary>
    public bool AutoSaveServiceState { get; set; } = true;

    /// <summary>
    /// 是否清理固定映射（默认true）
    /// </summary>
    public bool CleanupFixedMappings { get; set; } = true;

    /// <summary>
    /// 健康检查不健康状态延迟时间（默认2秒）
    /// 在此时间后，健康检查将返回不健康状态
    /// </summary>
    public TimeSpan HealthCheckUnhealthyDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// 优雅关闭状态
/// </summary>
public enum ShutdownState
{
    /// <summary>正常运行</summary>
    Running,

    /// <summary>准备关闭（通知阶段）</summary>
    PreparingShutdown,

    /// <summary>拒绝新连接</summary>
    RejectingNewConnections,

    /// <summary>排空现有请求</summary>
    DrainingRequests,

    /// <summary>保存状态</summary>
    SavingState,

    /// <summary>清理资源</summary>
    CleaningUp,

    /// <summary>已关闭</summary>
    Shutdown
}

/// <summary>
/// 优雅关闭进度信息
/// </summary>
public class ShutdownProgress
{
    /// <summary>当前状态</summary>
    public ShutdownState State { get; set; }

    /// <summary>开始时间</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>预计完成时间</summary>
    public DateTime EstimatedCompletionTime { get; set; }

    /// <summary>活跃连接数</summary>
    public int ActiveConnections { get; set; }

    /// <summary>待完成请求数</summary>
    public int PendingRequests { get; set; }

    /// <summary>待保存Service数</summary>
    public int PendingServiceSaves { get; set; }

    /// <summary>已完成百分比（0-100）</summary>
    public int CompletionPercentage { get; set; }

    /// <summary>当前步骤描述</summary>
    public string CurrentStep { get; set; } = string.Empty;

    /// <summary>错误信息（如果有）</summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// 客户端迁移信息
/// </summary>
public class ClientMigrationInfo
{
    /// <summary>推荐的目标节点列表</summary>
    public List<ushort> RecommendedNodes { get; set; } = new();

    /// <summary>迁移原因</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>剩余时间（秒）</summary>
    public int RemainingSeconds { get; set; }

    /// <summary>是否强制迁移</summary>
    public bool IsForcedMigration { get; set; }
}
