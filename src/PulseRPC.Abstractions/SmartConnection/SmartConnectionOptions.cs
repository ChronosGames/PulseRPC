using System;
using System.Collections.Generic;
using PulseRPC.Routing;
using PulseRPC.Transport;

namespace PulseRPC.SmartConnection;

/// <summary>
/// 智能连接选项
/// </summary>
public class SmartConnectionOptions
{
    /// <summary>
    /// 首选传输类型
    /// </summary>
    public TransportType PreferredTransport { get; set; } = TransportType.Tcp;

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 空闲回收时间（无活动连接多久后回收）
    /// </summary>
    public TimeSpan IdleRecycleTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool EnableAutoReconnect { get; set; } = true;

    /// <summary>
    /// 是否需要认证
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// 自定义认证令牌
    /// </summary>
    public string? CustomAuthToken { get; set; }

    /// <summary>
    /// 传输配置
    /// </summary>
    public ITransportOptions? TransportOptions { get; set; }

    /// <summary>
    /// 连接池配置
    /// </summary>
    public ConnectionPoolOptions ConnectionPool { get; set; } = new();

    /// <summary>
    /// 负载均衡配置
    /// </summary>
    public LoadBalancingOptions LoadBalancing { get; set; } = new();

    /// <summary>
    /// 健康检查配置
    /// </summary>
    public HealthCheckOptions HealthCheck { get; set; } = new();

    /// <summary>
    /// 监控配置
    /// </summary>
    public MonitoringOptions Monitoring { get; set; } = new();

    /// <summary>
    /// 扩展配置
    /// </summary>
    public Dictionary<string, object> Extensions { get; set; } = new();
}

/// <summary>
/// 传输配置接口
/// </summary>
public interface ITransportOptions
{
    /// <summary>
    /// 是否禁用Nagle算法
    /// </summary>
    bool NoDelay { get; set; }

    /// <summary>
    /// 是否启用Keep-Alive
    /// </summary>
    bool KeepAlive { get; set; }

    /// <summary>
    /// 是否自动重连
    /// </summary>
    bool AutoReconnect { get; set; }

    /// <summary>
    /// 接收缓冲区大小
    /// </summary>
    int ReceiveBufferSize { get; set; }

    /// <summary>
    /// 发送缓冲区大小
    /// </summary>
    int SendBufferSize { get; set; }
}

/// <summary>
/// TCP传输配置
/// </summary>
public class TcpTransportOptions : ITransportOptions
{
    public bool NoDelay { get; set; } = true;
    public bool KeepAlive { get; set; } = true;
    public bool AutoReconnect { get; set; } = true;
    public int ReceiveBufferSize { get; set; } = 8192;
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// 连接超时时间
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Socket LingerState
    /// </summary>
    public bool EnableLinger { get; set; } = false;

    /// <summary>
    /// Linger超时时间
    /// </summary>
    public int LingerTime { get; set; } = 0;
}

/// <summary>
/// KCP传输配置
/// </summary>
public class KcpTransportOptions : ITransportOptions
{
    public bool NoDelay { get; set; } = true;
    public bool KeepAlive { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public int ReceiveBufferSize { get; set; } = 8192;
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// KCP无延迟模式
    /// </summary>
    public int KcpNoDelay { get; set; } = 1;

    /// <summary>
    /// KCP更新间隔
    /// </summary>
    public int KcpInterval { get; set; } = 10;

    /// <summary>
    /// KCP快重传
    /// </summary>
    public int KcpResend { get; set; } = 2;

    /// <summary>
    /// 是否关闭拥塞控制
    /// </summary>
    public bool DisableFlowControl { get; set; } = true;

    /// <summary>
    /// 窗口大小
    /// </summary>
    public int WindowSize { get; set; } = 128;
}

/// <summary>
/// 连接池配置
/// </summary>
public class ConnectionPoolOptions
{
    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 100;

    /// <summary>
    /// 最小连接数
    /// </summary>
    public int MinConnections { get; set; } = 1;

    /// <summary>
    /// 连接获取超时时间
    /// </summary>
    public TimeSpan ConnectionAcquireTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 连接空闲超时时间
    /// </summary>
    public TimeSpan ConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 连接最大生存时间
    /// </summary>
    public TimeSpan ConnectionMaxLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 是否启用连接验证
    /// </summary>
    public bool EnableConnectionValidation { get; set; } = true;
}

/// <summary>
/// 负载均衡配置
/// </summary>
public class LoadBalancingOptions
{
    /// <summary>
    /// 负载均衡策略
    /// </summary>
    public ServiceRoutingStrategy Strategy { get; set; } = ServiceRoutingStrategy.RoundRobin;

    /// <summary>
    /// 权重配置
    /// </summary>
    public Dictionary<string, int> Weights { get; set; } = new();

    /// <summary>
    /// 故障转移启用
    /// </summary>
    public bool EnableFailover { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试间隔
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// 健康检查配置
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 检查间隔
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 失败阈值
    /// </summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>
    /// 成功阈值
    /// </summary>
    public int SuccessThreshold { get; set; } = 2;

    /// <summary>
    /// 健康检查路径
    /// </summary>
    public string HealthCheckPath { get; set; } = "/health";
}

/// <summary>
/// 监控配置
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// 是否启用性能监控
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = false;

    /// <summary>
    /// 是否启用连接监控
    /// </summary>
    public bool EnableConnectionMonitoring { get; set; } = true;

    /// <summary>
    /// 是否启用请求追踪
    /// </summary>
    public bool EnableRequestTracing { get; set; } = false;

    /// <summary>
    /// 指标收集间隔
    /// </summary>
    public TimeSpan MetricsCollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 追踪采样率
    /// </summary>
    public double TracingSampleRate { get; set; } = 0.1;
}
