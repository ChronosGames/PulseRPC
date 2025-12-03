namespace PulseRPC.Server;

// ========================
// 2. 增强的PID设计 [已废弃]
// ========================

/// <summary>
/// [已废弃] 增强的进程标识符 - 包含ServiceId信息
/// </summary>
/// <remarks>
/// <para>
/// <strong>⚠️ 此结构已废弃</strong>，新代码请使用 <see cref="PulseRPC.Server.Abstractions.IUnifiedPulseService.ServiceAddress"/> 替代。
/// </para>
/// <para>
/// <strong>迁移说明</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>PID → ServiceAddress (string)：{ServiceType}:{ServiceId}</description></item>
/// <item><description>新体系使用简单字符串作为服务标识，无需复杂的 PID 结构</description></item>
/// </list>
/// </remarks>
[Obsolete("使用 IUnifiedPulseService.ServiceAddress 替代。此结构将在未来版本中移除。")]
public readonly struct PID : IEquatable<PID>
{
    /// <summary>节点编号 (1-65535)</summary>
    public ushort NodeId { get; }

    /// <summary>流水号 (0-65535)</summary>
    public ushort SequenceId { get; }

    /// <summary>服务实例策略</summary>
    public ServiceInstanceStrategy Strategy { get; }

    /// <summary>服务标识符</summary>
    public string ServiceId { get; }

    private PID(ushort nodeId, ushort sequenceId, ServiceInstanceStrategy strategy, string serviceId)
    {
        if (nodeId == 0)
            throw new ArgumentOutOfRangeException(nameof(nodeId), "NodeId must be between 1 and 65535");

        NodeId = nodeId;
        SequenceId = sequenceId;
        Strategy = strategy;
        ServiceId = serviceId;
    }

    /// <summary>创建单例服务PID</summary>
    // public static PID CreateSingleton<TService>(ushort nodeId, ushort sequenceId) where TService : BaseService
    //     => new(nodeId, sequenceId, ServiceInstanceStrategy.Singleton, ServiceId.CreateSingleton<TService>());

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
