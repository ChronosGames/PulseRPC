using PulseRPC.Benchmark.Client.Engine;

namespace PulseRPC.Benchmark.Client.UI.Models;

/// <summary>
/// 统一的实时显示数据模型
/// </summary>
public class DisplayData
{
    /// <summary>
    /// 测试配置信息
    /// </summary>
    public TestConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// 当前测试状态
    /// </summary>
    public TestState CurrentState { get; set; } = TestState.Idle;

    /// <summary>
    /// 测试进度信息
    /// </summary>
    public TestProgress Progress { get; set; } = new();

    /// <summary>
    /// 测试开始时间
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 预计结束时间
    /// </summary>
    public DateTime EstimatedEndTime => StartTime.AddSeconds(Configuration.DurationSeconds);

    /// <summary>
    /// 测试完成百分比
    /// </summary>
    public double ProgressPercentage
    {
        get
        {
            if (Configuration.DurationSeconds <= 0) return 0;
            var elapsed = Progress.ElapsedTime.TotalSeconds;
            return Math.Min(100.0, (elapsed / Configuration.DurationSeconds) * 100.0);
        }
    }

    /// <summary>
    /// 系统资源使用情况
    /// </summary>
    public SystemResourceInfo SystemResources { get; set; } = new();

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 是否为详细模式
    /// </summary>
    public bool IsVerbose { get; set; }
}

/// <summary>
/// 系统资源信息
/// </summary>
public class SystemResourceInfo
{
    /// <summary>
    /// CPU使用率 (0-100)
    /// </summary>
    public double CpuUsagePercent { get; set; }

    /// <summary>
    /// 内存使用量 (MB)
    /// </summary>
    public long MemoryUsageMB { get; set; }

    /// <summary>
    /// 网络发送速率 (MB/s)
    /// </summary>
    public double NetworkSendMBps { get; set; }

    /// <summary>
    /// 网络接收速率 (MB/s)
    /// </summary>
    public double NetworkReceiveMBps { get; set; }

    /// <summary>
    /// GC回收次数
    /// </summary>
    public int GCCollectionCount { get; set; }

    /// <summary>
    /// 工作线程数
    /// </summary>
    public int WorkerThreads { get; set; }

    /// <summary>
    /// IO完成端口线程数
    /// </summary>
    public int CompletionPortThreads { get; set; }
}
