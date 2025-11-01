namespace PulseRPC.Server;

// ========================
// 2. 增强的PID设计
// ========================

/// <summary>
/// 增强的进程标识符 - 包含ServiceId信息
/// </summary>
public readonly struct PID : IEquatable<PID>
{
    /// <summary>节点编号 (1-65535)</summary>
    public ushort NodeId { get; }

    /// <summary>流水号 (0-65535)</summary>
    public ushort SequenceId { get; }

    /// <summary>服务实例策略</summary>
    public ServiceInstanceStrategy Strategy { get; }

    /// <summary>服务标识符</summary>
    public ServiceId ServiceId { get; }

    private PID(ushort nodeId, ushort sequenceId, ServiceInstanceStrategy strategy, ServiceId serviceId)
    {
        if (nodeId == 0)
            throw new ArgumentOutOfRangeException(nameof(nodeId), "NodeId must be between 1 and 65535");

        NodeId = nodeId;
        SequenceId = sequenceId;
        Strategy = strategy;
        ServiceId = serviceId;
    }

    /// <summary>创建单例服务PID</summary>
    public static PID CreateSingleton<TService>(ushort nodeId, ushort sequenceId) where TService : BaseService
        => new(nodeId, sequenceId, ServiceInstanceStrategy.Singleton, ServiceId.CreateSingleton<TService>());

    /// <summary>创建多实例服务PID</summary>
    public static PID CreateTransient<TService>(ushort nodeId, ushort sequenceId, string instanceId) where TService : BaseService
        => new(nodeId, sequenceId, ServiceInstanceStrategy.Transient, ServiceId.CreateMultiInstance<TService>(instanceId));

    /// <summary>创建池化服务PID</summary>
    public static PID CreatePooled<TService>(ushort nodeId, ushort sequenceId, string instanceId) where TService : BaseService
        => new(nodeId, sequenceId, ServiceInstanceStrategy.Pooled, ServiceId.CreateMultiInstance<TService>(instanceId));

    /// <summary>创建全局唯一服务PID</summary>
    public static PID CreateGlobal<TService>(ushort nodeId, ushort sequenceId) where TService : BaseService
        => new(nodeId, sequenceId, ServiceInstanceStrategy.Global, ServiceId.CreateSingleton<TService>());

    public override string ToString()
        => $"PID[{NodeId}:{SequenceId}:{Strategy}:{ServiceId}]";

    public bool Equals(PID other)
        => NodeId == other.NodeId && SequenceId == other.SequenceId && ServiceId.Equals(other.ServiceId);

    public override bool Equals(object? obj)
        => obj is PID other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(NodeId, SequenceId, ServiceId);

    public static bool operator ==(PID left, PID right) => left.Equals(right);
    public static bool operator !=(PID left, PID right) => !left.Equals(right);
}
