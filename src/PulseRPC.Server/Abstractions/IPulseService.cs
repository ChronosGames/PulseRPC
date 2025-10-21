namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例接口，用于启用基于 ServiceId 的线程调度和灾难隔离
/// </summary>
/// <remarks>
/// <para>
/// 实现此接口的服务类将获得以下能力：
/// </para>
/// <list type="bullet">
/// <item><description><strong>线程亲和性</strong>：相同 ServiceId 的所有请求路由到同一专用线程顺序执行</description></item>
/// <item><description><strong>灾难隔离</strong>：单个实例故障自动隔离，不影响其他实例</description></item>
/// <item><description><strong>自动恢复</strong>：隔离实例经过冷却期后自动尝试恢复</description></item>
/// <item><description><strong>实时监控</strong>：实例级别的健康状态和性能指标</description></item>
/// </list>
/// <para>
/// <strong>向后兼容性</strong>：
/// 此接口为可选增强接口，可与 <see cref="IPulseHub"/> 共同实现。
/// 仅实现 <see cref="IPulseHub"/> 的服务保持原有行为（使用默认线程池调度）。
/// </para>
/// <para>
/// <strong>ServiceId 生成指南</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>推荐格式：<c>ServiceName:业务ID</c>（例如：<c>ChatRoom:room-123</c>）</description></item>
/// <item><description>必须在构造函数中初始化，之后不可更改（不可变）</description></item>
/// <item><description>长度限制：1 到 1000 字符</description></item>
/// <item><description>允许字符：字母、数字、连字符、下划线、冒号</description></item>
/// <item><description>避免所有实例使用相同 ServiceId（会导致单线程瓶颈）</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // 示例 1: 聊天室服务 - 按房间 ID 隔离
/// public class ChatRoomService : IPulseHub, IPulseService
/// {
///     private readonly string _roomId;
///
///     public string ServiceName => "ChatRoom";
///     public string ServiceId { get; }
///
///     public ChatRoomService(string roomId)
///     {
///         _roomId = roomId;
///         ServiceId = $"ChatRoom:{roomId}"; // 每个房间独立线程
///     }
///
///     public async Task&lt;string&gt; SendMessageAsync(string message)
///     {
///         // 同一房间的所有消息在同一线程顺序处理（无需加锁）
///         return $"Message sent to room {_roomId}";
///     }
/// }
///
/// // 示例 2: 订单处理服务 - 按用户 ID 隔离
/// public class OrderProcessorService : IPulseHub, IPulseService
/// {
///     private readonly string _userId;
///
///     public string ServiceName => "OrderProcessor";
///     public string ServiceId { get; }
///
///     public OrderProcessorService(string userId)
///     {
///         _userId = userId;
///         ServiceId = $"OrderProcessor:{userId}"; // 每个用户独立线程
///     }
///
///     public async Task&lt;bool&gt; ProcessOrderAsync(int orderId)
///     {
///         // 同一用户的所有订单顺序处理
///         return true;
///     }
/// }
/// </code>
/// </example>
public interface IPulseService
{
    /// <summary>
    /// 服务类型名称（不可变）
    /// </summary>
    /// <remarks>
    /// <para>用于标识服务类型，例如："ChatRoom"、"OrderProcessor"、"GameRoom"</para>
    /// <para>通常使用类名或业务领域名称</para>
    /// </remarks>
    /// <value>服务类型名称字符串</value>
    string ServiceName { get; }

    /// <summary>
    /// 服务实例唯一标识符（不可变）
    /// </summary>
    /// <remarks>
    /// <para>用于唯一标识一个服务实例，例如："room-123"、"user-456"、"game-789"</para>
    /// <para>
    /// <strong>重要约束</strong>：
    /// </para>
    /// <list type="bullet">
    /// <item><description>必须在构造函数中初始化，之后不可更改</description></item>
    /// <item><description>相同 ServiceName + ServiceId 的所有请求路由到同一线程</description></item>
    /// <item><description>不同 ServiceId 的请求可并发执行（不同线程）</description></item>
    /// </list>
    /// <para>
    /// <strong>性能考虑</strong>：
    /// ServiceId 用于计算 xxHash64 哈希值进行线程映射，长度建议控制在 100 字符以内。
    /// </para>
    /// </remarks>
    /// <value>服务实例唯一标识符字符串</value>
    string ServiceId { get; }
}
