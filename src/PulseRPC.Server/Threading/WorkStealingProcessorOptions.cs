using System;

namespace PulseRPC.Server.Threading;

/// <summary>
/// 工作窃取处理器配置选项
/// </summary>
public class WorkStealingProcessorOptions
{
    /// <summary>
    /// 工作线程数量（默认为CPU核心数）
    /// </summary>
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;
    
    /// <summary>
    /// 窃取阈值 - 连续空出队多少次后开始尝试窃取
    /// </summary>
    public int StealThreshold { get; set; } = 10;
    
    /// <summary>
    /// 最小队列大小 - 只有当队列大小超过此值时才允许被窃取
    /// </summary>
    public int MinQueueSizeForStealing { get; set; } = 2;
    
    /// <summary>
    /// 等待策略
    /// </summary>
    public WaitStrategy WaitStrategy { get; set; } = WaitStrategy.Adaptive;
    
    /// <summary>
    /// 启用会话亲和性
    /// </summary>
    public bool EnableSessionAffinity { get; set; } = true;
    
    /// <summary>
    /// 使用轮询选择工作线程（否则使用负载最小选择）
    /// </summary>
    public bool UseRoundRobinSelection { get; set; } = false;
    
    /// <summary>
    /// 关闭超时（毫秒）
    /// </summary>
    public int ShutdownTimeoutMs { get; set; } = 5000;
    
    /// <summary>
    /// 启用详细日志
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
    
    /// <summary>
    /// 验证配置的有效性
    /// </summary>
    public void Validate()
    {
        if (WorkerThreadCount <= 0)
        {
            throw new ArgumentException("工作线程数必须大于0", nameof(WorkerThreadCount));
        }
        
        if (WorkerThreadCount > Environment.ProcessorCount * 4)
        {
            throw new ArgumentException($"工作线程数不应超过CPU核心数的4倍（当前CPU核心数：{Environment.ProcessorCount}）", 
                nameof(WorkerThreadCount));
        }
        
        if (StealThreshold <= 0)
        {
            throw new ArgumentException("窃取阈值必须大于0", nameof(StealThreshold));
        }
        
        if (MinQueueSizeForStealing <= 0)
        {
            throw new ArgumentException("最小队列大小必须大于0", nameof(MinQueueSizeForStealing));
        }
        
        if (ShutdownTimeoutMs <= 0)
        {
            throw new ArgumentException("关闭超时必须大于0", nameof(ShutdownTimeoutMs));
        }
        
        if (!Enum.IsDefined(typeof(WaitStrategy), WaitStrategy))
        {
            throw new ArgumentException("无效的等待策略", nameof(WaitStrategy));
        }
    }
}

/// <summary>
/// 等待策略
/// </summary>
public enum WaitStrategy
{
    /// <summary>
    /// 自旋等待 - 最低延迟，高CPU使用
    /// </summary>
    SpinWait,
    
    /// <summary>
    /// 让出CPU - 中等延迟，中等CPU使用
    /// </summary>
    YieldWait,
    
    /// <summary>
    /// 睡眠等待 - 高延迟，低CPU使用
    /// </summary>
    SleepWait,
    
    /// <summary>
    /// 自适应等待 - 根据负载动态调整
    /// </summary>
    Adaptive
}