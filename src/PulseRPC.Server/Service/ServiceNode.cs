using System;
using System.Collections.Generic;

namespace PulseRPC.Server;

/// <summary>
/// 服务节点信息
/// </summary>
public class ServiceNode
{
    /// <summary>
    /// 服务类型
    /// </summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 区ID
    /// </summary>
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>
    /// 服务器ID（用于GameServer等）
    /// </summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>
    /// 实例ID（用于BattleServer等动态实例）
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 服务主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// UDP端口（可选）
    /// </summary>
    public int UdpPort { get; set; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime LastHeartbeat { get; set; }

    /// <summary>
    /// 服务健康状态
    /// </summary>
    public ServiceHealth Health { get; set; } = ServiceHealth.Unknown;

    /// <summary>
    /// 从ServiceRegistration创建ServiceNode
    /// </summary>
    public static ServiceNode FromRegistration(ServiceRegistration registration)
    {
        if (registration == null)
            throw new ArgumentNullException(nameof(registration));

        return new ServiceNode
        {
            ServiceType = registration.ServiceType,
            ZoneId = registration.ZoneId,
            ServerId = registration.ServerId,
            InstanceId = registration.InstanceId,
            Host = registration.Host,
            Port = registration.Port,
            UdpPort = registration.UdpPort,
            Metadata = new Dictionary<string, string>(registration.Metadata),
            LastHeartbeat = DateTime.UtcNow,
            Health = ServiceHealth.Healthy
        };
    }
}

/// <summary>
/// 服务健康状态
/// </summary>
public enum ServiceHealth
{
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown,

    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 已下线
    /// </summary>
    Offline
}
