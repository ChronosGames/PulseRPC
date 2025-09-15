using PulseRPC.Transport;

namespace PulseRPC.Client.Core;

/// <summary>
/// 连接策略类型
/// </summary>
public enum ConnectionStrategy
{
    /// <summary>
    /// 持久连接 - 在客户端生命周期内保持连接
    /// </summary>
    Persistent,
    
    /// <summary>
    /// 会话连接 - 在特定会话或上下文中保持连接
    /// </summary>
    Session,
    
    /// <summary>
    /// 按需连接 - 使用时连接，使用后可能断开
    /// </summary>
    OnDemand,
    
    /// <summary>
    /// 池化连接 - 从连接池中获取，用完归还
    /// </summary>
    Pooled
}

/// <summary>
/// 连接配置描述符
/// </summary>
public sealed record ConnectionDescriptor
{
    /// <summary>
    /// 连接唯一标识符
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// 连接名称（可重复，用于逻辑分组）
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// 目标地址（直连模式）
    /// </summary>
    public EndpointAddress? Endpoint { get; init; }
    
    /// <summary>
    /// 服务名称（服务发现模式）
    /// </summary>
    public string? ServiceName { get; init; }
    
    /// <summary>
    /// 传输协议类型
    /// </summary>
    public TransportType Transport { get; init; } = TransportType.Tcp;
    
    /// <summary>
    /// 传输层配置选项
    /// </summary>
    public TransportOptions? TransportOptions { get; init; }
    
    /// <summary>
    /// 连接策略
    /// </summary>
    public ConnectionStrategy Strategy { get; init; } = ConnectionStrategy.Session;
    
    /// <summary>
    /// 连接优先级（用于资源竞争时的决策）
    /// </summary>
    public int Priority { get; init; } = 0;
    
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>
    /// 连接超时
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// 空闲超时（用于自动清理）
    /// </summary>
    public TimeSpan? IdleTimeout { get; init; }
    
    /// <summary>
    /// 生存时间（连接的最大存活时间）
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }
    
    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; init; } = true;
    
    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan? HealthCheckInterval { get; init; }
    
    /// <summary>
    /// 连接标签（用于分类和查询）
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    
    /// <summary>
    /// 扩展属性
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// 端点地址
/// </summary>
public sealed record EndpointAddress
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Scheme { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    
    public override string ToString() => $"{Scheme ?? "tcp"}://{Host}:{Port}";
}

/// <summary>
/// 连接状态枚举
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// 未连接
    /// </summary>
    Disconnected,
    
    /// <summary>
    /// 连接中
    /// </summary>
    Connecting,
    
    /// <summary>
    /// 已连接
    /// </summary>
    Connected,
    
    /// <summary>
    /// 重连中
    /// </summary>
    Reconnecting,
    
    /// <summary>
    /// 正在断开
    /// </summary>
    Disconnecting,
    
    /// <summary>
    /// 连接失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 连接已废弃
    /// </summary>
    Disposed
}

/// <summary>
/// 连接健康状态
/// </summary>
public enum ConnectionHealth
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,
    
    /// <summary>
    /// 警告（性能下降但可用）
    /// </summary>
    Warning,
    
    /// <summary>
    /// 不健康（不可用）
    /// </summary>
    Unhealthy,
    
    /// <summary>
    /// 未知状态
    /// </summary>
    Unknown
}

/// <summary>
/// 连接统计信息
/// </summary>
public sealed record ConnectionStatistics
{
    /// <summary>
    /// 连接创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; }
    
    /// <summary>
    /// 最后连接时间
    /// </summary>
    public DateTime? LastConnectedAt { get; init; }
    
    /// <summary>
    /// 最后使用时间
    /// </summary>
    public DateTime? LastUsedAt { get; init; }
    
    /// <summary>
    /// 总连接次数
    /// </summary>
    public long TotalConnections { get; init; }
    
    /// <summary>
    /// 总重连次数
    /// </summary>
    public long TotalReconnections { get; init; }
    
    /// <summary>
    /// 发送的消息数
    /// </summary>
    public long MessagesSent { get; init; }
    
    /// <summary>
    /// 接收的消息数
    /// </summary>
    public long MessagesReceived { get; init; }
    
    /// <summary>
    /// 发送的字节数
    /// </summary>
    public long BytesSent { get; init; }
    
    /// <summary>
    /// 接收的字节数
    /// </summary>
    public long BytesReceived { get; init; }
    
    /// <summary>
    /// 错误计数
    /// </summary>
    public long ErrorCount { get; init; }
    
    /// <summary>
    /// 平均延迟（毫秒）
    /// </summary>
    public double AverageLatencyMs { get; init; }
    
    /// <summary>
    /// 当前活跃请求数
    /// </summary>
    public int ActiveRequests { get; init; }
    
    /// <summary>
    /// 连接质量评分（0-100）
    /// </summary>
    public int QualityScore { get; init; }
}

/// <summary>
/// 连接状态变化事件参数
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    public required string ConnectionId { get; init; }
    public required ConnectionState PreviousState { get; init; }
    public required ConnectionState CurrentState { get; init; }
    public Exception? Exception { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 连接健康状态变化事件参数
/// </summary>
public sealed class ConnectionHealthChangedEventArgs : EventArgs
{
    public required string ConnectionId { get; init; }
    public required ConnectionHealth PreviousHealth { get; init; }
    public required ConnectionHealth CurrentHealth { get; init; }
    public string? Reason { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}