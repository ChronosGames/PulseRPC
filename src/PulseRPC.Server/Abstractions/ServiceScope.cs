namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务启动类型 - 定义服务何时被创建
/// </summary>
public enum ServiceStartupType
{
    /// <summary>
    /// 自动启动 - 应用初始化时自动创建并启动
    /// </summary>
    /// <remarks>
    /// <para>适用场景：</para>
    /// <list type="bullet">
    /// <item><description>核心基础设施服务（如匹配系统、排行榜服务）</description></item>
    /// <item><description>需要预热的服务（如缓存服务）</description></item>
    /// <item><description>全局单例服务</description></item>
    /// </list>
    /// </remarks>
    AutoStart = 0,

    /// <summary>
    /// 按需启动 - 收到第一个请求时动态创建
    /// </summary>
    /// <remarks>
    /// <para>适用场景：</para>
    /// <list type="bullet">
    /// <item><description>聊天室、游戏房间等实例</description></item>
    /// <item><description>用户会话服务</description></item>
    /// <item><description>资源敏感的服务（延迟创建以节省资源）</description></item>
    /// </list>
    /// </remarks>
    OnDemand = 1
}

/// <summary>
/// 服务实例范围 - 定义服务实例的唯一性边界
/// </summary>
public enum ServiceInstanceScope
{
    /// <summary>
    /// 全集群唯一 - 整个分布式集群中只有一个实例
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>需要集群协调机制（如分布式锁、领导选举）</description></item>
    /// <item><description>ServiceId 固定，通常为 "{ServiceType}:global"</description></item>
    /// <item><description>适合：全局排行榜、全服公告、全局配置服务</description></item>
    /// </list>
    /// <para>注意：集群唯一服务的故障转移需要额外配置。</para>
    /// </remarks>
    ClusterSingleton = 0,

    /// <summary>
    /// 进程内唯一 - 每个进程中只有一个实例
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>本地单例，无需集群协调</description></item>
    /// <item><description>ServiceId 固定，通常为 "{ServiceType}:local"</description></item>
    /// <item><description>适合：本地缓存管理、连接池管理、本地事件总线</description></item>
    /// </list>
    /// </remarks>
    ProcessSingleton = 1,

    /// <summary>
    /// 多实例 - 可以创建多个实例，每个实例有唯一的 ServiceId
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>ServiceId 动态生成，基于业务标识</description></item>
    /// <item><description>通过 ServiceType + ServiceId 唯一定位实例</description></item>
    /// <item><description>适合：聊天室、游戏房间、用户会话、订单处理</description></item>
    /// </list>
    /// <para>ServiceId 示例：</para>
    /// <code>
    /// "ChatRoom:room-123"
    /// "GameRoom:match-456"
    /// "UserSession:user-789"
    /// </code>
    /// </remarks>
    MultiInstance = 2
}

/// <summary>
/// 服务调度模式 - 定义请求如何被调度执行
/// </summary>
public enum ServiceSchedulingMode
{
    /// <summary>
    /// 默认调度池 - 使用公共线程池执行，无顺序保证
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>并发执行，最高吞吐量</description></item>
    /// <item><description>无消息顺序保证</description></item>
    /// <item><description>服务实现必须线程安全</description></item>
    /// <item><description>适合：无状态查询、CPU 密集型计算</description></item>
    /// </list>
    /// </remarks>
    DefaultPool = 0,

    /// <summary>
    /// 服务专属队列 - 每个 ServiceId 有独立队列，保证 FIFO 顺序
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>同一 ServiceId 的请求顺序执行</description></item>
    /// <item><description>不同 ServiceId 的请求可并发</description></item>
    /// <item><description>天然线程安全（无需加锁）</description></item>
    /// <item><description>适合：有状态服务、聊天室、游戏房间</description></item>
    /// </list>
    /// </remarks>
    DedicatedQueue = 1,

    /// <summary>
    /// 线程亲和性 - 基于 ServiceId 哈希分配到固定线程
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>同一 ServiceId 总是在同一线程执行</description></item>
    /// <item><description>利用 CPU 缓存局部性</description></item>
    /// <item><description>适合：高频更新的服务</description></item>
    /// </list>
    /// </remarks>
    ThreadAffinity = 2
}

