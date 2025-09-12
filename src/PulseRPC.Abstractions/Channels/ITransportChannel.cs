using PulseRPC.Authentication;

namespace PulseRPC.Transport;

/// <summary>
/// 传输通道接口，管理连接的生命周期和认证状态
/// 继承ISessionChannel提供统一的会话层抽象
/// 保持向后兼容性，同时融入三层抽象架构
/// </summary>
public interface ITransportChannel : ISessionChannel
{
    /// <summary>
    /// 底层传输连接
    /// </summary>
    IServerTransport Transport { get; }

    /// <summary>
    /// 连接建立时间 (向后兼容的属性名)
    /// </summary>
    DateTime ConnectedTime => ConnectedAt;

    /// <summary>
    /// 最后活动时间 (可设置的版本，用于向后兼容)
    /// </summary>
    DateTime LastActiveTime { get; set; }
}
