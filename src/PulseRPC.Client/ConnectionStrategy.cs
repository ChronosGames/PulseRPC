using PulseRPC.Transport;

namespace PulseRPC.Client;

/// <summary>
/// 连接策略枚举 - 定义连接的生命周期和管理方式
/// </summary>
public enum ConnectionStrategy
{
    /// <summary>
    /// 持久连接 - 长期保持连接，适用于核心服务
    /// </summary>
    Persistent,

    /// <summary>
    /// 会话连接 - 在会话期间保持连接，会话结束时断开
    /// </summary>
    Session,

    /// <summary>
    /// 临时连接 - 短期连接，使用后立即断开
    /// </summary>
    Transient,

    /// <summary>
    /// 池化连接 - 使用连接池管理，支持复用
    /// </summary>
    Pooled
}

/// <summary>
/// 连接生命周期枚举 - 更细粒度的生命周期控制
/// </summary>
public enum ConnectionLifetime
{
    /// <summary>
    /// 持久连接 - 手动管理生命周期
    /// </summary>
    Persistent,

    /// <summary>
    /// 会话连接 - 绑定到会话生命周期
    /// </summary>
    Session,

    /// <summary>
    /// 临时连接 - 自动回收
    /// </summary>
    Transient
}

/// <summary>
/// 负载均衡策略枚举
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// 轮询策略
    /// </summary>
    RoundRobin,

    /// <summary>
    /// 最少连接策略
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 一致性哈希策略
    /// </summary>
    ConsistentHash,

    /// <summary>
    /// 加权轮询策略
    /// </summary>
    WeightedRoundRobin,

    /// <summary>
    /// 随机策略
    /// </summary>
    Random
}

/// <summary>
/// 负载均衡提示枚举 - 用于客户端提供负载均衡偏好
/// </summary>
public enum LoadBalancingHint
{
    /// <summary>
    /// 无特定偏好
    /// </summary>
    None,

    /// <summary>
    /// 偏好最少连接
    /// </summary>
    LeastConnections,

    /// <summary>
    /// 偏好一致性哈希
    /// </summary>
    ConsistentHash,

    /// <summary>
    /// 偏好本地区域
    /// </summary>
    LocalRegion,

    /// <summary>
    /// 偏好高性能实例
    /// </summary>
    HighPerformance,

    /// <summary>
    /// 偏好本地连接
    /// </summary>
    PreferLocal,

    /// <summary>
    /// 偏好快速响应
    /// </summary>
    PreferFast,

    /// <summary>
    /// 粘性连接（会话亲和性）
    /// </summary>
    Sticky,

    /// <summary>
    /// 均匀分布
    /// </summary>
    Distribute
}

/// <summary>
/// 连接池策略枚举
/// </summary>
public enum PoolingStrategy
{
    /// <summary>
    /// 固定大小池
    /// </summary>
    FixedSize,

    /// <summary>
    /// 动态大小池
    /// </summary>
    Dynamic
}

/// <summary>
/// 退避策略枚举 - 用于重试机制
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// 固定延迟
    /// </summary>
    Fixed,

    /// <summary>
    /// 线性增长
    /// </summary>
    Linear,

    /// <summary>
    /// 指数增长
    /// </summary>
    Exponential
}

/// <summary>
/// 连接策略配置 - 为不同策略提供配置选项
/// </summary>
public sealed class ConnectionStrategyOptions
{
    /// <summary>
    /// 策略类型
    /// </summary>
    public ConnectionStrategy Strategy { get; set; } = ConnectionStrategy.Session;

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    /// 空闲超时时间（仅适用于 Session 和 Transient）
    /// </summary>
    public TimeSpan? IdleTimeout { get; set; }

    /// <summary>
    /// 最大重连次数
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 3;

    /// <summary>
    /// 重连间隔
    /// </summary>
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 连接优先级（用于资源竞争时的优先级排序）
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 创建持久连接策略配置
    /// </summary>
    public static ConnectionStrategyOptions Persistent(bool autoReconnect = true) => new()
    {
        Strategy = ConnectionStrategy.Persistent,
        AutoReconnect = autoReconnect,
        Priority = 100 // 持久连接优先级最高
    };

    /// <summary>
    /// 创建会话连接策略配置
    /// </summary>
    public static ConnectionStrategyOptions Session(TimeSpan? idleTimeout = null) => new()
    {
        Strategy = ConnectionStrategy.Session,
        AutoReconnect = true,
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30),
        Priority = 50
    };

    /// <summary>
    /// 创建临时连接策略配置
    /// </summary>
    public static ConnectionStrategyOptions Transient(TimeSpan? idleTimeout = null) => new()
    {
        Strategy = ConnectionStrategy.Transient,
        AutoReconnect = false,
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(5),
        Priority = 10
    };

    /// <summary>
    /// 创建池化连接策略配置
    /// </summary>
    public static ConnectionStrategyOptions Pooled(bool autoReconnect = true) => new()
    {
        Strategy = ConnectionStrategy.Pooled,
        AutoReconnect = autoReconnect,
        Priority = 30
    };
}
