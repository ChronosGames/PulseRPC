using System;
using System.Collections.Generic;
using PulseRPC.Transport;

namespace PulseRPC;

/// <summary>
/// 连接端点信息
/// </summary>
public class ConnectionEndpoint
{
    /// <summary>
    /// 服务ID
    /// </summary>
    public string ServiceId { get; set; } = "";

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// 端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 权重
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// 标签
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// 传输类型
    /// </summary>
    public TransportType Transport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 从ServiceEndpoint转换为ConnectionEndpoint
    /// </summary>
    /// <param name="serviceEndpoint">服务端点</param>
    /// <returns>连接端点</returns>
    public static ConnectionEndpoint FromServiceEndpoint(ServiceDiscovery.ServiceEndpoint serviceEndpoint)
    {
        return new ConnectionEndpoint
        {
            ServiceId = serviceEndpoint.ServiceId,
            Host = serviceEndpoint.Host,
            Port = serviceEndpoint.Port,
            Weight = serviceEndpoint.Weight,
            Transport = serviceEndpoint.Transport,
            Metadata = serviceEndpoint.Metadata?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value) ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// 转换为ServiceEndpoint（用于服务发现）
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>服务端点</returns>
    public ServiceDiscovery.ServiceEndpoint ToServiceEndpoint(string serviceType = "")
    {
        return new ServiceDiscovery.ServiceEndpoint
        {
            ServiceId = ServiceId,
            ServiceType = serviceType,
            Host = Host,
            Port = Port,
            Weight = Weight,
            Transport = Transport,
            Metadata = Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "") ?? new Dictionary<string, string>()
        };
    }
}

/// <summary>
/// 连接池配置选项
/// </summary>
public class ConnectionPoolOptions
{
    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 10;

    /// <summary>
    /// 最小连接数
    /// </summary>
    public int MinConnections { get; set; } = 1;

    /// <summary>
    /// 连接空闲超时时间
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 连接生存时间
    /// </summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// 认证提供者接口
/// </summary>
public interface IAuthenticationProvider
{
    /// <summary>
    /// 获取认证信息
    /// </summary>
    /// <returns>认证信息</returns>
    Task<AuthenticationInfo> GetAuthenticationAsync();
}

/// <summary>
/// 认证信息
/// </summary>
public class AuthenticationInfo
{
    /// <summary>
    /// 认证类型
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// 认证令牌
    /// </summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// 连接统计信息
/// </summary>
public class ConnectionStatistics
{
    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 空闲连接数
    /// </summary>
    public int IdleConnections { get; set; }

    /// <summary>
    /// 失败连接数
    /// </summary>
    public int FailedConnections { get; set; }

    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 服务统计信息
    /// </summary>
    public Dictionary<string, ServiceConnectionStatistics> ServiceStatistics { get; set; } = new();
}

/// <summary>
/// 服务连接统计信息
/// </summary>
public class ServiceConnectionStatistics
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// 连接数量
    /// </summary>
    public int ConnectionCount { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 失败连接数
    /// </summary>
    public int FailedConnections { get; set; }

    /// <summary>
    /// 请求总数
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数
    /// </summary>
    public long SuccessfulRequests { get; set; }

    /// <summary>
    /// 失败请求数
    /// </summary>
    public long FailedRequests { get; set; }

    /// <summary>
    /// 平均响应时间（毫秒）
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 连接状态变化事件参数
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// 新状态
    /// </summary>
    public ConnectionState NewState { get; set; }

    /// <summary>
    /// 旧状态
    /// </summary>
    public ConnectionState OldState { get; set; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime EventTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 附加信息
    /// </summary>
    public string? Message { get; set; }
}

