namespace PulseRPC.Server.Processing;

/// <summary>
/// 高吞吐量处理器管理接口
/// </summary>
public interface IHighThroughputProcessorManager : IDisposable
{
    /// <summary>
    /// 为连接创建高吞吐量处理器
    /// </summary>
    Task<ServerHighThroughputMessageProcessor> CreateProcessorAsync(string connectionId, PulseRPC.Server.Transport.IServerChannel serverChannel);

    /// <summary>
    /// 移除连接的处理器
    /// </summary>
    Task<bool> RemoveProcessorAsync(string connectionId);

    /// <summary>
    /// 获取连接的处理器
    /// </summary>
    ServerHighThroughputMessageProcessor? GetProcessor(string connectionId);

    /// <summary>
    /// 获取所有处理器统计信息
    /// </summary>
    Dictionary<string, ProcessorStats> GetAllStats();

    /// <summary>
    /// 获取管理器状态
    /// </summary>
    ManagerStats GetManagerStats();
}

/// <summary>
/// 管理器统计信息
/// </summary>
public class ManagerStats
{
    public int ActiveProcessors { get; set; }
    public long TotalProcessorsCreated { get; set; }
    public long TotalProcessorsRemoved { get; set; }
    public long TotalMessagesProcessed { get; set; }
    public long TotalMessagesDropped { get; set; }
    public DateTime StartTime { get; set; }
}
