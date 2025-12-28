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
    /// 单例 - 进程内只有一个实例
    /// </summary>
    /// <remarks>
    /// <para>特性：</para>
    /// <list type="bullet">
    /// <item><description>进程内唯一，无需外部协调</description></item>
    /// <item><description>ServiceId 固定为 "default"</description></item>
    /// <item><description>适合：排行榜、匹配系统、缓存管理、事件总线等全局服务</description></item>
    /// </list>
    /// <para>注意：如需跨进程/集群单例语义，请在业务层通过服务发现+分布式锁实现。</para>
    /// </remarks>
    Singleton = 0,

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
    MultiInstance = 1
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

