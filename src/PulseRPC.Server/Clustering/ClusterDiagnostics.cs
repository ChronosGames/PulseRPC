using System;
using PulseRPC.Routing;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// 集群诊断钩子：用于接入分布式 tracing、指标、dead letter 与 slow mailbox 观测。
/// </summary>
public interface IClusterDiagnostics
{
    /// <summary>记录一次 placement 决策。</summary>
    void RecordPlacementDecision(string hub, string key, string ownerNodeId, string strategyName);

    /// <summary>记录无法投递的消息。</summary>
    void RecordDeadLetter(PulseAddress address, ushort protocolId, string reason);

    /// <summary>记录慢邮箱/慢 Actor 调度。</summary>
    void RecordSlowMailbox(string hub, string key, TimeSpan elapsed);
}

/// <summary>
/// 默认 no-op 集群诊断实现；生产环境可替换为 OpenTelemetry/Prometheus/日志实现。
/// </summary>
public sealed class NoopClusterDiagnostics : IClusterDiagnostics
{
    /// <inheritdoc/>
    public void RecordPlacementDecision(string hub, string key, string ownerNodeId, string strategyName) { }

    /// <inheritdoc/>
    public void RecordDeadLetter(PulseAddress address, ushort protocolId, string reason) { }

    /// <inheritdoc/>
    public void RecordSlowMailbox(string hub, string key, TimeSpan elapsed) { }
}
