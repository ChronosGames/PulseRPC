using System;
using System.Collections.Generic;

namespace PulseRPC.Server;

/// <summary>
/// 服务心跳信息
/// </summary>
public class ServiceHeartbeat
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
    /// 服务器ID
    /// </summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 心跳时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 服务指标数据
    /// </summary>
    public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
}
