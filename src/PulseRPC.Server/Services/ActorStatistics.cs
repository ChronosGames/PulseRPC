namespace PulseRPC.Server;

/// <summary>
/// Actor统计（增加认证相关指标）
/// </summary>
public class ActorStatistics
{
    public int QueuedCount { get; set; }
    public int ProcessedCount { get; set; }
    public int FailedCount { get; set; }
    public int MethodInvocations { get; set; }
    public int TimerInvocations { get; set; }
    public int DeadlocksPrevented { get; set; }
    public int AuthenticationFailures { get; set; }
    public int AuthorizationFailures { get; set; }
    public double AverageWaitTimeMs { get; set; }
    public double AverageProcessingTimeMs { get; set; }
    public DateTime? LastProcessedTime { get; set; }
    public bool IsProcessing { get; set; }
    public int ActiveTimerCount { get; set; }
}
