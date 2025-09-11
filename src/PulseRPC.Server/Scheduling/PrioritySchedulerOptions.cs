using System;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 优先级调度器配置选项
/// </summary>
public class PrioritySchedulerOptions
{
    /// <summary>
    /// 关键消息权重 (默认50%)
    /// </summary>
    public int CriticalWeight { get; set; } = 50;
    
    /// <summary>
    /// 普通消息权重 (默认35%)
    /// </summary>
    public int NormalWeight { get; set; } = 35;
    
    /// <summary>
    /// 批量消息权重 (默认15%)
    /// </summary>
    public int BulkWeight { get; set; } = 15;
    
    /// <summary>
    /// 关键消息队列大小
    /// </summary>
    public int CriticalQueueSize { get; set; } = 256;
    
    /// <summary>
    /// 普通消息队列大小
    /// </summary>
    public int NormalQueueSize { get; set; } = 1024;
    
    /// <summary>
    /// 批量消息队列大小
    /// </summary>
    public int BulkQueueSize { get; set; } = 4096;
    
    /// <summary>
    /// 关键消息最大延迟 (毫秒)
    /// </summary>
    public double CriticalMaxLatencyMs { get; set; } = 2.0;
    
    /// <summary>
    /// 普通消息最大延迟 (毫秒)
    /// </summary>
    public double NormalMaxLatencyMs { get; set; } = 10.0;
    
    /// <summary>
    /// 批量消息最大延迟 (毫秒)
    /// </summary>
    public double BulkMaxLatencyMs { get; set; } = 100.0;
    
    /// <summary>
    /// 普通消息满时丢弃
    /// </summary>
    public bool NormalDropOnFull { get; set; } = true;
    
    /// <summary>
    /// 批量消息支持背压
    /// </summary>
    public bool BulkBackpressure { get; set; } = true;
    
    /// <summary>
    /// 最大并发任务数
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = Environment.ProcessorCount * 2;
    
    /// <summary>
    /// 工作线程数量
    /// </summary>
    public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;
    
    /// <summary>
    /// 最大任务数每秒 (速率限制)
    /// </summary>
    public int MaxTasksPerSecond { get; set; } = 100_000;
    
    /// <summary>
    /// 突发大小 (速率限制)
    /// </summary>
    public int BurstSize { get; set; } = 1000;
    
    /// <summary>
    /// 启用详细日志
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
    
    /// <summary>
    /// 验证配置的有效性
    /// </summary>
    public void Validate()
    {
        if (CriticalWeight + NormalWeight + BulkWeight != 100)
        {
            throw new ArgumentException("权重总和必须等于100%");
        }
        
        if (CriticalWeight < 0 || NormalWeight < 0 || BulkWeight < 0)
        {
            throw new ArgumentException("权重不能为负数");
        }
        
        if (CriticalQueueSize <= 0 || NormalQueueSize <= 0 || BulkQueueSize <= 0)
        {
            throw new ArgumentException("队列大小必须大于0");
        }
        
        if (CriticalMaxLatencyMs <= 0 || NormalMaxLatencyMs <= 0 || BulkMaxLatencyMs <= 0)
        {
            throw new ArgumentException("最大延迟必须大于0");
        }
        
        if (MaxConcurrentTasks <= 0)
        {
            throw new ArgumentException("最大并发任务数必须大于0");
        }
        
        if (WorkerThreadCount <= 0)
        {
            throw new ArgumentException("工作线程数必须大于0");
        }
        
        if (MaxTasksPerSecond <= 0)
        {
            throw new ArgumentException("最大任务数每秒必须大于0");
        }
        
        if (BurstSize <= 0)
        {
            throw new ArgumentException("突发大小必须大于0");
        }
    }
}