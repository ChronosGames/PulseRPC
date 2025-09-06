namespace PulseRPC.Server;

/// <summary>
/// 服务器配置选项
/// </summary>
public class ServerOptions
{
    /// <summary>
    /// 服务名称
    /// </summary>
    public string ServiceName { get; set; } = "PulseRPC-Service";

    /// <summary>
    /// 服务版本
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// 服务标签
    /// </summary>
    public Dictionary<string, string> ServiceTags { get; set; } = new();

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, object> ServiceMetadata { get; set; } = new();

    /// <summary>
    /// 是否启用服务注册
    /// </summary>
    public bool EnableServiceRegistry { get; set; } = false;

    /// <summary>
    /// 服务实例权重
    /// </summary>
    public int ServiceWeight { get; set; } = 1;

    /// <summary>
    /// 自动获取本机IP地址
    /// </summary>
    public bool AutoDetectAddress { get; set; } = true;

    /// <summary>
    /// 手动指定服务地址 (当AutoDetectAddress为false时使用)
    /// </summary>
    public string? ServiceAddress { get; set; }

    /// <summary>
    /// 监听IP地址
    /// </summary>
    public string IPAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// 监听端口
    /// </summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>
    /// 接收缓冲区大小
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// 发送缓冲区大小
    /// </summary>
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 发送超时时间
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum length of the pending connections queue.
    /// </summary>
    public int BacklogSize { get; set; } = 100;

    /// <summary>
    /// 最大数据包大小
    /// </summary>
    public int MaxPacketSize { get; set; } = 64 * 1024;

    /// <summary>
    /// 压缩阈值
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024;

    /// <summary>
    /// 是否使用加密
    /// </summary>
    public bool UseEncryption { get; set; } = false;

    /// <summary>
    /// 空闲超时
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 是否启用TCP_NODELAY
    /// </summary>
    public bool NoDelay { get; set; } = true;

    /// <summary>
    /// 高吞吐量消息处理器配置
    /// </summary>
    public HighThroughputProcessorOptions HighThroughputProcessor { get; set; } = new();
}

/// <summary>
/// 高吞吐量消息处理器配置选项
/// </summary>
public class HighThroughputProcessorOptions
{
    /// <summary>
    /// 是否启用高吞吐量消息处理器
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// L1缓冲区大小（无锁环形缓冲区）
    /// </summary>
    public int L1BufferSize { get; set; } = 2048;

    /// <summary>
    /// L2批量处理队列容量
    /// </summary>
    public int L2QueueCapacity { get; set; } = 100;

    /// <summary>
    /// L3响应发送队列容量
    /// </summary>
    public int L3QueueCapacity { get; set; } = 50;

    /// <summary>
    /// 批处理间隔（毫秒）
    /// </summary>
    public int BatchIntervalMs { get; set; } = 2;

    /// <summary>
    /// 每批最大消息数量
    /// </summary>
    public int MaxBatchSize { get; set; } = 32;

    /// <summary>
    /// 关键消息强制入队超时（微秒）
    /// </summary>
    public int CriticalMessageTimeoutUs { get; set; } = 100;

    /// <summary>
    /// 普通消息背压丢弃率（0.0-1.0）
    /// </summary>
    public double NormalMessageDropRate { get; set; } = 0.8;

    /// <summary>
    /// 批处理软超时阈值（毫秒）
    /// </summary>
    public int BatchSoftTimeoutMs { get; set; } = 50;

    /// <summary>
    /// 性能检查频率（每处理多少条消息检查一次）
    /// </summary>
    public int PerformanceCheckFrequency { get; set; } = 10;

    /// <summary>
    /// L2队列满时的紧急等待时间（毫秒）
    /// </summary>
    public int L2BackpressureWaitMs { get; set; } = 1;

    /// <summary>
    /// 是否启用详细性能日志
    /// </summary>
    public bool EnableDetailedLogging { get; set; }
}
