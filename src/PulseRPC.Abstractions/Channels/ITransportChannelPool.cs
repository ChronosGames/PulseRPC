namespace PulseRPC.Channels;

/// <summary>
/// 传输通道连接池接口 - 管理所有活动的传输通道
/// </summary>
/// <remarks>
/// 连接池的职责：
/// - 注册管理：维护 ConnectionId -> ITransportChannel 映射
/// - 生命周期：自动清理断开的连接
/// - 查询接口：提供多种方式查询连接
/// - 线程安全：支持并发访问
/// </remarks>
public interface ITransportChannelPool
{
    /// <summary>
    /// 注册连接到连接池
    /// </summary>
    /// <param name="connectionId">连接唯一标识</param>
    /// <param name="channel">传输通道实例</param>
    /// <exception cref="ArgumentNullException">参数为 null</exception>
    /// <exception cref="ArgumentException">连接ID为空或通道已存在</exception>
    void Register(string connectionId, ITransportChannel channel);

    /// <summary>
    /// 从连接池移除连接
    /// </summary>
    /// <param name="connectionId">连接唯一标识</param>
    /// <returns>是否成功移除</returns>
    bool Unregister(string connectionId);

    /// <summary>
    /// 获取指定连接
    /// </summary>
    /// <param name="connectionId">连接唯一标识</param>
    /// <returns>传输通道实例，如果不存在返回 null</returns>
    ITransportChannel? GetChannel(string connectionId);

    /// <summary>
    /// 获取所有活动连接
    /// </summary>
    /// <returns>所有活动通道的只读集合</returns>
    IReadOnlyCollection<ITransportChannel> GetAllChannels();

    /// <summary>
    /// 获取所有活动连接ID
    /// </summary>
    /// <returns>所有连接ID的只读集合</returns>
    IReadOnlyCollection<string> GetAllConnectionIds();

    /// <summary>
    /// 检查连接是否存在
    /// </summary>
    /// <param name="connectionId">连接唯一标识</param>
    /// <returns>连接是否存在</returns>
    bool Contains(string connectionId);

    /// <summary>
    /// 活动连接数量
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 清理所有连接（通常在服务器关闭时调用）
    /// </summary>
    void Clear();
}
