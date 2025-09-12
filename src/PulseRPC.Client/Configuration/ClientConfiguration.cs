using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC;

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
