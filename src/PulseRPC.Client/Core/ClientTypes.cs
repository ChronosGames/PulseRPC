using PulseRPC.Transport;

namespace PulseRPC.Client.Core;

/// <summary>
/// 客户端状态
/// </summary>
public enum ClientState
{
    /// <summary>
    /// 未初始化
    /// </summary>
    Uninitialized,

    /// <summary>
    /// 初始化中
    /// </summary>
    Initializing,

    /// <summary>
    /// 运行中
    /// </summary>
    Running,

    /// <summary>
    /// 停止中
    /// </summary>
    Stopping,

    /// <summary>
    /// 已停止
    /// </summary>
    Stopped,

    /// <summary>
    /// 错误状态
    /// </summary>
    Error
}

/// <summary>
/// 客户端状态变化事件参数
/// </summary>
public sealed class ClientStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 之前的状态
    /// </summary>
    public ClientState PreviousState { get; set; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public ClientState CurrentState { get; set; }

    /// <summary>
    /// 异常信息
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 客户端统计信息
/// </summary>
public sealed class ClientStatistics
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// 启动时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 运行时间
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 统计时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 客户端健康检查结果
/// </summary>
public sealed class ClientHealthCheckResult
{
    /// <summary>
    /// 整体健康状态
    /// </summary>
    public ConnectionHealth OverallHealth { get; set; }

    /// <summary>
    /// 连接健康检查结果
    /// </summary>
    public IReadOnlyList<HealthCheckResult> ConnectionResults { get; set; } = Array.Empty<HealthCheckResult>();

    /// <summary>
    /// 服务发现健康状态
    /// </summary>
    public ConnectionHealth ServiceDiscoveryHealth { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// 总检查时间
    /// </summary>
    public TimeSpan TotalCheckTime { get; set; }
}

/// <summary>
/// 客户端选项
/// </summary>
public sealed class ClientOptions
{
    /// <summary>
    /// 客户端名称
    /// </summary>
    public string Name { get; set; } = "PulseRPC-Client";

    /// <summary>
    /// 默认超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 最大并发连接数
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 100;

    /// <summary>
    /// 启用调试模式
    /// </summary>
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// 启用统计信息收集
    /// </summary>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// 自动清理间隔
    /// </summary>
    public TimeSpan AutoCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 服务连接选项
/// </summary>
public sealed class ServiceConnectionOptions
{
    /// <summary>
    /// 偏好的传输类型
    /// </summary>
    public TransportType? PreferredTransport { get; set; }

    /// <summary>
    /// 连接策略
    /// </summary>
    public ConnectionStrategy? Strategy { get; set; }

    /// <summary>
    /// 自动重连
    /// </summary>
    public bool? AutoReconnect { get; set; }

    /// <summary>
    /// 连接超时
    /// </summary>
    public TimeSpan? ConnectTimeout { get; set; }
}

/// <summary>
/// 服务代理选项
/// </summary>
public sealed class ServiceProxyOptions
{
    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// 重试策略
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }

    /// <summary>
    /// 缓存策略
    /// </summary>
    public bool UseCache { get; set; } = true;
}

/// <summary>
/// 事件监听器选项
/// </summary>
public sealed class EventListenerOptions
{
    /// <summary>
    /// 监听器名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 自动重新订阅
    /// </summary>
    public bool AutoResubscribe { get; set; } = true;

    /// <summary>
    /// 缓冲区大小
    /// </summary>
    public int BufferSize { get; set; } = 1000;
}

/// <summary>
/// 重试策略
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 重试间隔
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 最大重试间隔
    /// </summary>
    public TimeSpan MaxRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
}