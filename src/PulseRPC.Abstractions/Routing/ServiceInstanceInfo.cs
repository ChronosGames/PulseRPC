using System;
using System.Collections.Generic;
using PulseRPC.Transport;

namespace PulseRPC.Routing;

/// <summary>
/// 服务实例信息
/// </summary>
public class ServiceInstanceInfo
{
    /// <summary>
    /// 实例ID
    /// </summary>
    public string InstanceId { get; set; } = "";

    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// 服务端点
    /// </summary>
    public ServiceEndpoint Endpoint { get; set; } = new();

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// 权重（用于负载均衡）
    /// </summary>
    public int Weight { get; set; } = 100;

    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; set; } = true;

    /// <summary>
    /// 最后健康检查时间
    /// </summary>
    public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; } = 0;

    /// <summary>
    /// 地区
    /// </summary>
    public string Region { get; set; } = "";

    /// <summary>
    /// 可用区
    /// </summary>
    public string Zone { get; set; } = "";

    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// 计算实例优先级（用于负载均衡）
    /// </summary>
    public double CalculatePriority(IRoutingContext context)
    {
        if (!IsHealthy) return 0;

        var score = (double)Weight;

        // 亲和性加分
        if (!string.IsNullOrEmpty(context.AffinityId) &&
            Metadata.TryGetValue("affinity", out var affinity) &&
            affinity.Contains(context.AffinityId))
        {
            score += 1000;
        }

        // 连接数惩罚
        score -= ActiveConnections * 10;

        // 健康检查时间惩罚（越久未检查，分数越低）
        var timeSinceHealthCheck = DateTime.UtcNow - LastHealthCheck;
        if (timeSinceHealthCheck.TotalMinutes > 5)
        {
            score -= timeSinceHealthCheck.TotalMinutes * 2;
        }

        return Math.Max(0, score);
    }

    /// <summary>
    /// 检查是否匹配路由上下文
    /// </summary>
    public bool MatchesContext(IRoutingContext context)
    {
        // 检查地区匹配
        if (context.Parameters.TryGetValue("region", out var requiredRegion) &&
            requiredRegion.ToString() != Region)
        {
            return false;
        }

        // 检查可用区匹配
        if (context.Parameters.TryGetValue("zone", out var requiredZone) &&
            requiredZone.ToString() != Zone)
        {
            return false;
        }

        // 检查版本匹配
        if (context.Parameters.TryGetValue("version", out var requiredVersion) &&
            requiredVersion.ToString() != Version)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// 服务端点定义
/// </summary>
public class ServiceEndpoint
{
    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// 端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// 是否使用TLS/SSL
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// 获取连接字符串
    /// </summary>
    public string GetConnectionString()
    {
        var protocol = UseSsl ? "tls" : Transport.ToString().ToLower();
        return $"{protocol}://{Host}:{Port}";
    }
}
