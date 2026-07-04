using System;
using System.Collections.Generic;

namespace PulseRPC.Clustering;

/// <summary>
/// 集群成员视图 —— 提供当前<strong>存活</strong>节点集合，并在成员变化时通知订阅者。
/// </summary>
/// <remarks>
/// <para>
/// 这是「服务发现 / 集群成员」的统一插拔点（设计文档路线图 P8）：
/// </para>
/// <list type="bullet">
/// <item><description>静态部署：<c>StaticClusterMembership</c> 从 <c>ClusterTopologyOptions</c> 读取固定成员，
/// 并结合节点健康（失败累计）在运行时把不可达节点移出存活集；</description></item>
/// <item><description>动态部署：Consul/Etcd/Kubernetes 等基础设施包实现本接口，从发现后端推送成员增删。</description></item>
/// </list>
/// <para>
/// 一致性哈希环（<c>NodeConsistentHashRing</c>）与集群路由（<c>ClusterPulseRouter</c>）以本视图的
/// <see cref="LiveNodeIds"/> 为准计算 Actor 属主；<see cref="Changed"/> 触发时应重建环，使故障节点拥有的
/// <c>(Hub, Key)</c> 重新映射到存活节点（P7 故障接管）。
/// </para>
/// <para>
/// <see cref="ReportNodeFailure"/>/<see cref="ReportNodeSuccess"/> 是路由层在节点间链路调用失败/成功时给出的
/// <strong>健康提示</strong>：静态实现据此做失败累计与恢复；动态（发现后端权威）实现可将其作为快速可疑判定或忽略。
/// </para>
/// </remarks>
public interface IClusterMembership
{
    /// <summary>
    /// 当前存活的节点标识集合（已排除被判定为不可达/已下线的节点）。始终至少包含本节点。
    /// </summary>
    IReadOnlyList<string> LiveNodeIds { get; }

    /// <summary>
    /// 存活成员集合发生变化时触发（节点被移出或重新加入存活集）。订阅方应据此重建一致性哈希环。
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// 健康提示：报告一次到 <paramref name="nodeId"/> 的节点间调用失败。静态实现累计连续失败，
    /// 达到阈值即把该节点移出存活集并触发 <see cref="Changed"/>。
    /// </summary>
    void ReportNodeFailure(string nodeId);

    /// <summary>
    /// 健康提示：报告一次到 <paramref name="nodeId"/> 的节点间调用成功。清零其失败累计；
    /// 若该节点此前被移出存活集，则重新纳入并触发 <see cref="Changed"/>。
    /// </summary>
    void ReportNodeSuccess(string nodeId);
}
