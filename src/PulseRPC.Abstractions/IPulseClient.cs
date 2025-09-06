using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC;

/// <summary>
/// PulseRPC 统一客户端接口 - 整合所有客户端功能
/// </summary>
public interface IPulseClient : IDisposable
{
    /// <summary>
    /// 连接到服务器
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 获取服务代理 - 自动处理服务发现和连接管理
    /// </summary>
    /// <typeparam name="T">服务接口类型</typeparam>
    /// <param name="serviceName">服务名称，为空则使用接口名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务代理</returns>
    Task<T> GetServiceAsync2<T>(string? serviceName = null, CancellationToken cancellationToken = default)
        where T : class, IPulseService;

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    /// <typeparam name="T">监听器接口类型</typeparam>
    /// <param name="listener">监听器实例</param>
    /// <param name="serviceName">服务名称，为空则使用接口名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅令牌</returns>
    Task<ISubscriptionToken> RegisterEventListenerAsync2<T>(T listener, string? serviceName = null,
        CancellationToken cancellationToken = default) where T : class, IPulseEventHandler;

    /// <summary>
    /// 获取连接统计信息
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接统计</returns>
    Task<ConnectionStatistics> GetConnectionStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    /// <summary>
    /// 获取通道管理器 - 用于底层通道操作
    /// </summary>
    /// <returns>通道管理器实例</returns>
    IChannelManager GetChannelManager();
}

/// <summary>
/// PulseClient 构建器接口 - 流式配置
/// </summary>
public interface IPulseClientBuilder
{
    /// <summary>
    /// 添加 TCP 传输
    /// </summary>
    IPulseClientBuilder AddTcp(string name, string host, int port);

    /// <summary>
    /// 添加 KCP 传输
    /// </summary>
    IPulseClientBuilder AddKcp(string name, string host, int port);

    /// <summary>
    /// 配置服务发现
    /// </summary>
    IPulseClientBuilder WithServiceDiscovery(Action<ServiceDiscoveryOptions> configure);

    /// <summary>
    /// 配置认证
    /// </summary>
    IPulseClientBuilder WithAuthentication(IAuthenticationProvider provider);

    /// <summary>
    /// 配置超时
    /// </summary>
    IPulseClientBuilder WithTimeout(TimeSpan timeout);

    /// <summary>
    /// 配置重试策略
    /// </summary>
    IPulseClientBuilder WithRetry(Action<RetryOptions> configure);

    /// <summary>
    /// 配置连接池
    /// </summary>
    IPulseClientBuilder WithConnectionPool(Action<ConnectionPoolOptions> configure);

    /// <summary>
    /// 构建客户端
    /// </summary>
    IPulseClient Build();
}

/// <summary>
/// 服务发现配置选项
/// </summary>
public class ServiceDiscoveryOptions
{
    /// <summary>
    /// 使用 Consul 服务发现
    /// </summary>
    public ConsulOptions? Consul { get; set; }

    /// <summary>
    /// 使用静态端点配置
    /// </summary>
    public Dictionary<string, ConnectionEndpoint> StaticEndpoints { get; set; } = new();

    /// <summary>
    /// 刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Consul 配置选项
/// </summary>
public class ConsulOptions
{
    /// <summary>
    /// Consul 地址
    /// </summary>
    public string Address { get; set; } = "http://localhost:8500";

    /// <summary>
    /// 数据中心
    /// </summary>
    public string? Datacenter { get; set; }

    /// <summary>
    /// 访问令牌
    /// </summary>
    public string? Token { get; set; }
}

/// <summary>
/// 重试配置选项
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 基础延迟
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 最大延迟
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
}
