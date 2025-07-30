using System;
using PulseRPC.ServiceDiscovery;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Authentication;
using PulseRPC.Routing;
using PulseRPC.SmartConnection;
using PulseRPC.Transport;

namespace PulseRPC;

/// <summary>
/// 智能 PulseRPC 客户端 - 支持按需连接和自动管理
/// </summary>
public interface ISmartPulseRpcClient : IPulseRpcClient
{
    /// <summary>
    /// 获取服务代理 - 自动连接管理
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="serviceName">服务名称，用于服务发现</param>
    /// <param name="options">连接选项</param>
    /// <returns>服务代理</returns>
    Task<T> GetServiceAsync<T>(string serviceName = "", SmartConnectionOptions? options = null)
        where T : class, IPulseService;

    /// <summary>
    /// 获取特定实例的服务代理
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="serviceName">服务名称</param>
    /// <param name="instanceId">实例ID</param>
    /// <param name="options">连接选项</param>
    /// <returns>服务代理</returns>
    Task<T> GetServiceAsync<T>(string serviceName, string instanceId, SmartConnectionOptions? options = null)
        where T : class, IPulseService;

    /// <summary>
    /// 获取服务代理 - 支持路由策略
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="serviceName">服务名称</param>
    /// <param name="routingContext">路由上下文</param>
    /// <param name="options">连接选项</param>
    /// <returns>服务代理</returns>
    Task<T> GetServiceAsync<T>(string serviceName, IRoutingContext routingContext, SmartConnectionOptions? options = null)
        where T : class, IPulseService;

    /// <summary>
    /// 获取多实例服务管理器
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="serviceName">服务名称</param>
    /// <param name="options">连接选项</param>
    /// <returns>多实例服务管理器</returns>
    Task<IMultiInstanceServiceManager<T>> GetMultiInstanceServiceAsync<T>(string serviceName, SmartConnectionOptions? options = null)
        where T : class, IPulseService;

    /// <summary>
    /// 注册事件监听器 - 自动连接管理
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="listener">监听器实例</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="options">连接选项</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string serviceName = "",
        SmartConnectionOptions? options = null) where T : class, IPulseEventHandler;

    /// <summary>
    /// 注册事件监听器 - 支持路由
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="listener">监听器实例</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="routingContext">路由上下文</param>
    /// <param name="options">连接选项</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync<T>(T listener, string serviceName, IRoutingContext routingContext,
        SmartConnectionOptions? options = null) where T : class, IPulseEventHandler;

    /// <summary>
    /// 配置服务发现
    /// </summary>
    /// <param name="configure">配置委托</param>
    void ConfigureServiceDiscovery(Action<ServiceDiscoveryConfiguration> configure);

    /// <summary>
    /// 配置认证提供者
    /// </summary>
    /// <param name="authProvider">认证提供者</param>
    void ConfigureAuthentication(IAuthenticationProvider authProvider);

    /// <summary>
    /// 配置服务路由策略
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <param name="configure">路由配置委托</param>
    void ConfigureServiceRouting<T>(Action<ServiceRoutingConfiguration<T>> configure) where T : class, IPulseService;

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    /// <returns>连接统计</returns>
    Task<ConnectionStatistics> GetConnectionStatisticsAsync();

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    /// <param name="maxAge">最大空闲时间</param>
    /// <returns>清理的连接数</returns>
    Task<int> CleanupIdleConnectionsAsync(TimeSpan? maxAge = null);

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    /// <summary>
    /// 服务发现状态变化事件
    /// </summary>
    event EventHandler<ServiceDiscoveryEventArgs> ServiceDiscoveryChanged;
}

/// <summary>
/// 服务发现配置
/// </summary>
public class ServiceDiscoveryConfiguration
{
    /// <summary>
    /// 服务发现类型
    /// </summary>
    public ServiceDiscoveryType Type { get; set; } = ServiceDiscoveryType.Static;

    /// <summary>
    /// 静态服务端点映射
    /// </summary>
    public Dictionary<string, ServiceEndpoint> StaticEndpoints { get; set; } = new();

    /// <summary>
    /// Consul 配置
    /// </summary>
    public ConsulConfiguration? Consul { get; set; }

    /// <summary>
    /// Etcd 配置
    /// </summary>
    public EtcdConfiguration? Etcd { get; set; }

    /// <summary>
    /// DNS 配置
    /// </summary>
    public DnsConfiguration? Dns { get; set; }

    /// <summary>
    /// 缓存配置
    /// </summary>
    public CacheConfiguration Cache { get; set; } = new();

    /// <summary>
    /// 刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用监听模式
    /// </summary>
    public bool EnableWatch { get; set; } = true;
}

/// <summary>
/// 服务发现类型
/// </summary>
public enum ServiceDiscoveryType
{
    /// <summary>
    /// 静态配置
    /// </summary>
    Static,

    /// <summary>
    /// Consul
    /// </summary>
    Consul,

    /// <summary>
    /// Etcd
    /// </summary>
    Etcd,

    /// <summary>
    /// DNS
    /// </summary>
    Dns,

    /// <summary>
    /// 自定义
    /// </summary>
    Custom
}

/// <summary>
/// Consul配置
/// </summary>
public class ConsulConfiguration
{
    /// <summary>
    /// Consul地址
    /// </summary>
    public string Address { get; set; } = "http://localhost:8500";

    /// <summary>
    /// 数据中心
    /// </summary>
    public string? Datacenter { get; set; }

    /// <summary>
    /// 令牌
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// 标签过滤器
    /// </summary>
    public string[] TagFilters { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Etcd配置
/// </summary>
public class EtcdConfiguration
{
    /// <summary>
    /// Etcd端点
    /// </summary>
    public string[] Endpoints { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 用户名
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// 密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// 根路径前缀
    /// </summary>
    public string RootPath { get; set; } = "/pulserpc/services";
}

/// <summary>
/// DNS配置
/// </summary>
public class DnsConfiguration
{
    /// <summary>
    /// DNS服务器
    /// </summary>
    public string[] Servers { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 域名后缀
    /// </summary>
    public string DomainSuffix { get; set; } = ".service.consul";

    /// <summary>
    /// SRV记录查询
    /// </summary>
    public bool UseSrvRecords { get; set; } = true;
}

/// <summary>
/// 缓存配置
/// </summary>
public class CacheConfiguration
{
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 最大缓存条目数
    /// </summary>
    public int MaxEntries { get; set; } = 1000;
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
    /// 按服务分组的连接统计
    /// </summary>
    public Dictionary<string, ServiceConnectionStatistics> ServiceStatistics { get; set; } = new();

    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 服务连接统计
/// </summary>
public class ServiceConnectionStatistics
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// 连接数
    /// </summary>
    public int ConnectionCount { get; set; }

    /// <summary>
    /// 实例数
    /// </summary>
    public int InstanceCount { get; set; }

    /// <summary>
    /// 健康实例数
    /// </summary>
    public int HealthyInstanceCount { get; set; }

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
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageResponseTime { get; set; }
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
    /// 实例ID
    /// </summary>
    public string? InstanceId { get; set; }

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

/// <summary>
/// 服务发现事件参数
/// </summary>
public class ServiceDiscoveryEventArgs : EventArgs
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// 事件类型
    /// </summary>
    public ServiceDiscoveryEventType EventType { get; set; }

    /// <summary>
    /// 实例信息
    /// </summary>
    public ServiceInstanceInfo? Instance { get; set; }

    /// <summary>
    /// 事件时间
    /// </summary>
    public DateTime EventTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 服务发现事件类型
/// </summary>
public enum ServiceDiscoveryEventType
{
    /// <summary>
    /// 服务添加
    /// </summary>
    ServiceAdded,

    /// <summary>
    /// 服务移除
    /// </summary>
    ServiceRemoved,

    /// <summary>
    /// 服务更新
    /// </summary>
    ServiceUpdated,

    /// <summary>
    /// 发现失败
    /// </summary>
    DiscoveryFailed
}
