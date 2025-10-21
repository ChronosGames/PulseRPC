namespace PulseRPC.Server.Abstractions;

/// <summary>
/// 服务实例接口，提供线程调度所需的标识信息
/// </summary>
/// <remarks>
/// 服务实现类可以同时实现 IPulseHub 和 IPulseService 以启用线程调度控制。
/// ServiceName 和 ServiceId 必须在服务实例初始化时确定，之后不可更改。
///
/// 此接口为契约定义，实际实现位于 src/PulseRPC.Server/Abstractions/IPulseService.cs
/// </remarks>
public interface IPulseService
{
    /// <summary>
    /// 服务类型名称（不可变）
    /// </summary>
    /// <value>
    /// 标识服务类型的字符串，例如 "ChatRoom", "OrderProcessor", "GameRoom"
    /// </value>
    /// <remarks>
    /// 要求:
    /// - 非空字符串
    /// - 最大长度 200 字符
    /// - 格式: 以字母开头，仅包含字母和数字 (^[a-zA-Z][a-zA-Z0-9]*$)
    /// </remarks>
    string ServiceName { get; }

    /// <summary>
    /// 服务实例唯一标识符（不可变）
    /// </summary>
    /// <value>
    /// 唯一标识服务实例的字符串，例如 "ChatRoom:room-123", "OrderProcessor:order-456"
    /// </value>
    /// <remarks>
    /// 要求:
    /// - 非空字符串
    /// - 最大长度 1000 字符
    /// - 格式: 允许字母、数字、连字符、下划线、冒号 (^[a-zA-Z0-9\-:_]+$)
    /// - 推荐格式: "{ServiceName}:{业务ID}"
    ///
    /// 生成策略:
    /// - 由服务实现类在构造函数中生成
    /// - 必须在服务实例整个生命周期内保持不变
    /// - 应具有业务语义以便调试（避免纯 GUID）
    ///
    /// 示例:
    /// - "ChatRoom:room-123"
    /// - "OrderProcessor:order-456:server1"
    /// - "GameRoom:game-abc-def"
    /// </remarks>
    string ServiceId { get; }
}
