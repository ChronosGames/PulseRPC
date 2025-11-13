namespace PulseRPC.Server;

/// <summary>
/// Service 消息队列配置选项
/// </summary>
/// <remarks>
/// 提供统一的配置接口，支持所有 Service 模型的配置需求：
/// - 单线程 Actor 模型（MaxConcurrency=1）
/// - 并发模型（MaxConcurrency>1）
/// - 优先级队列（通过 [Priority] 特性）
/// - 背压流控（BackpressureStrategy）
/// </remarks>
public class ServiceQueueOptions
{
    /// <summary>
    /// 消息队列容量
    /// </summary>
    /// <remarks>
    /// 默认值: 10000
    /// 队列满时，根据 BackpressureStrategy 决定行为
    /// </remarks>
    public int QueueCapacity { get; set; } = 10000;

    /// <summary>
    /// 最大并发度（同时处理的消息数量）
    /// </summary>
    /// <remarks>
    /// 默认值: 1（单线程 Actor 模型）
    ///
    /// 推荐设置:
    /// - 1: 单线程串行处理（Actor 模型），保证消息严格有序
    /// - 4-8: 一般并发场景
    /// - 8-16: IO 密集型服务（数据库查询、HTTP 调用）
    /// - CPU 核心数: CPU 密集型服务（计算任务）
    /// </remarks>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// 背压策略（队列满时的处理策略）
    /// </summary>
    /// <remarks>
    /// 默认值: Block（阻塞等待，抛出异常）
    ///
    /// 可选策略：
    /// - Block: 队列满时抛出异常，让调用者重试（适合不能丢消息的业务）
    /// - DropOldest: 丢弃最旧消息，插入新消息（适合日志、监控数据）
    /// - DropNewest: 拒绝新消息，保留队列中的消息（适合顺序敏感的业务）
    /// - Reject: 拒绝新消息并抛出异常（适合需要重试逻辑的场景）
    /// </remarks>
    public Configuration.BackpressureStrategy BackpressureStrategy { get; set; } = Configuration.BackpressureStrategy.Block;

    /// <summary>
    /// 验证配置有效性
    /// </summary>
    public virtual void Validate()
    {
        if (MaxConcurrency < 1)
            throw new ArgumentException("MaxConcurrency must be at least 1", nameof(MaxConcurrency));

        if (MaxConcurrency > 1000)
            throw new ArgumentException("MaxConcurrency should not exceed 1000", nameof(MaxConcurrency));

        if (QueueCapacity < 1)
            throw new ArgumentException("QueueCapacity must be at least 1", nameof(QueueCapacity));
    }

    /// <summary>
    /// 克隆配置
    /// </summary>
    public virtual ServiceQueueOptions Clone() => new()
    {
        QueueCapacity = QueueCapacity,
        MaxConcurrency = MaxConcurrency,
        BackpressureStrategy = BackpressureStrategy
    };

    // ========== 预定义配置（工厂方法） ==========

    /// <summary>
    /// 创建默认配置（单线程 Actor 模型）
    /// </summary>
    public static ServiceQueueOptions Default => new();

    /// <summary>
    /// 创建单线程 Actor 模型配置（严格有序）
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 聊天服务
    /// - 社交功能
    /// - 一般游戏逻辑
    /// - 状态机驱动的业务
    /// </remarks>
    public static ServiceQueueOptions ForActor => new()
    {
        MaxConcurrency = 1,
        QueueCapacity = 10000,
        BackpressureStrategy = Configuration.BackpressureStrategy.Block
    };

    /// <summary>
    /// 创建并发模型配置（IO 密集型）
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 数据库查询服务
    /// - HTTP API 网关
    /// - 无状态的纯查询操作
    /// </remarks>
    public static ServiceQueueOptions ForConcurrentIO => new()
    {
        MaxConcurrency = 16,
        QueueCapacity = 10000,
        BackpressureStrategy = Configuration.BackpressureStrategy.Block
    };

    /// <summary>
    /// 创建并发模型配置（CPU 密集型）
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 寻路计算服务
    /// - 数据处理服务
    /// - 加密解密服务
    /// </remarks>
    public static ServiceQueueOptions ForConcurrentCPU => new()
    {
        MaxConcurrency = Environment.ProcessorCount,
        QueueCapacity = 5000,
        BackpressureStrategy = Configuration.BackpressureStrategy.Block
    };

    /// <summary>
    /// 创建优先级队列配置（单线程 Actor + 优先级）
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - GM 命令服务（紧急操作优先）
    /// - 带优先级的任务队列
    ///
    /// 注意：优先级通过方法上的 [Priority(MessagePriority.Critical)] 特性指定
    /// </remarks>
    public static ServiceQueueOptions ForPriorityQueue => new()
    {
        MaxConcurrency = 1,
        QueueCapacity = 10000,
        BackpressureStrategy = Configuration.BackpressureStrategy.Block
    };

    /// <summary>
    /// 创建背压流控配置（丢弃最旧消息）
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 日志收集服务（旧日志可丢弃）
    /// - 历史监控数据（最新数据更重要）
    /// - 缓存更新通知
    /// </remarks>
    public static ServiceQueueOptions ForBackpressureDropOldest => new()
    {
        MaxConcurrency = 1,
        QueueCapacity = 5000,
        BackpressureStrategy = Configuration.BackpressureStrategy.DropOldest
    };

    /// <summary>
    /// 创建背压流控配置（拒绝新消息）
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 支付服务（不能丢消息，队列满时拒绝新请求）
    /// - 关键业务操作
    /// </remarks>
    public static ServiceQueueOptions ForBackpressureReject => new()
    {
        MaxConcurrency = 1,
        QueueCapacity = 100,
        BackpressureStrategy = Configuration.BackpressureStrategy.Reject
    };
}

/// <summary>
/// Service 配置构建器（Fluent API）
/// </summary>
public class ServiceQueueOptionsBuilder
{
    private readonly ServiceQueueOptions _options = new();

    /// <summary>
    /// 设置队列容量
    /// </summary>
    public ServiceQueueOptionsBuilder WithQueueCapacity(int capacity)
    {
        _options.QueueCapacity = capacity;
        return this;
    }

    /// <summary>
    /// 设置最大并发度
    /// </summary>
    public ServiceQueueOptionsBuilder WithMaxConcurrency(int concurrency)
    {
        _options.MaxConcurrency = concurrency;
        return this;
    }

    /// <summary>
    /// 设置背压策略
    /// </summary>
    public ServiceQueueOptionsBuilder WithBackpressureStrategy(Configuration.BackpressureStrategy strategy)
    {
        _options.BackpressureStrategy = strategy;
        return this;
    }

    /// <summary>
    /// 启用单线程 Actor 模型
    /// </summary>
    public ServiceQueueOptionsBuilder AsActor()
    {
        _options.MaxConcurrency = 1;
        return this;
    }

    /// <summary>
    /// 启用并发模型
    /// </summary>
    public ServiceQueueOptionsBuilder AsConcurrent(int maxConcurrency = 4)
    {
        _options.MaxConcurrency = maxConcurrency;
        return this;
    }

    /// <summary>
    /// 启用优先级队列
    /// </summary>
    /// <remarks>
    /// 优先级通过方法上的 [Priority(MessagePriority.Critical)] 特性指定
    /// </remarks>
    public ServiceQueueOptionsBuilder WithPriority()
    {
        // 优先级队列已默认启用，无需额外配置
        return this;
    }

    /// <summary>
    /// 启用背压流控（丢弃最旧消息）
    /// </summary>
    public ServiceQueueOptionsBuilder WithBackpressureDropOldest()
    {
        _options.BackpressureStrategy = Configuration.BackpressureStrategy.DropOldest;
        return this;
    }

    /// <summary>
    /// 启用背压流控（丢弃最新消息）
    /// </summary>
    public ServiceQueueOptionsBuilder WithBackpressureDropNewest()
    {
        _options.BackpressureStrategy = Configuration.BackpressureStrategy.DropNewest;
        return this;
    }

    /// <summary>
    /// 启用背压流控（拒绝新消息）
    /// </summary>
    public ServiceQueueOptionsBuilder WithBackpressureReject()
    {
        _options.BackpressureStrategy = Configuration.BackpressureStrategy.Reject;
        return this;
    }

    /// <summary>
    /// 构建配置对象
    /// </summary>
    public ServiceQueueOptions Build()
    {
        _options.Validate();
        return _options;
    }

    /// <summary>
    /// 从现有配置开始构建
    /// </summary>
    public static ServiceQueueOptionsBuilder From(ServiceQueueOptions options)
    {
        var builder = new ServiceQueueOptionsBuilder();
        builder._options.QueueCapacity = options.QueueCapacity;
        builder._options.MaxConcurrency = options.MaxConcurrency;
        builder._options.BackpressureStrategy = options.BackpressureStrategy;
        return builder;
    }
}
