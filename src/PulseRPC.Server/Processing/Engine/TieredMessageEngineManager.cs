using PulseRPC.Messaging;
using PulseRPC.Server.Services.Scheduling;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Processing.Engine;

public interface ITieredMessageEngine
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();

    void RegisterConnection(string connectionId);

    void RegisterConnection(IServerChannel channel);

    void UnregisterConnection(string connectionId);

    void UnregisterConnection(IServerChannel channel);

    bool TryEnqueueMessage(string connectionId, MessagePacketHolder message, MessagePriority priority);

    bool TryEnqueueMessage(IServerChannel sourceChannel, MessagePacketHolder message, MessagePriority priority);

    EngineStatistics GetStatistics();
}

/// <summary>
/// 引擎管理器配置选项
/// </summary>
[Obsolete("This options model is not used. Configure PulseServerOptions instead.", false)]
public class TieredEngineManagerOptions
{
    public int MaxConnections { get; set; } = 10000;

    // 默认处理器选项
    public int DefaultL1BufferSize { get; set; } = 4096;
    public int DefaultL2QueueCapacity { get; set; } = 256;
    public int DefaultL3QueueCapacity { get; set; } = 128;
    public int DefaultMaxBatchSize { get; set; } = 64;
    public int DefaultBatchIntervalMs { get; set; } = 5;
    public bool EnableDetailedLogging { get; set; } = false;
    public double DefaultNormalMessageDropRate { get; set; } = 0.8;
    public int DefaultCriticalMessageTimeoutUs { get; set; } = 100;
    public int DefaultL2BackpressureWaitMs { get; set; } = 1;
    public int DefaultPerformanceCheckFrequency { get; set; } = 10;
    public int DefaultBatchSoftTimeoutMs { get; set; } = 50;
}

/// <summary>
/// 管理器统计信息
/// </summary>
[Obsolete("Tiered engine manager statistics are no longer produced. Use EngineStatistics.", false)]
public class ManagerStatistics
{
    public int TotalConnections { get; set; }
    public int TotalEngineInstances { get; set; }
    public bool IsDisposed { get; set; }
    public List<AdapterStatistics> AdapterStatistics { get; set; } = new();
}
