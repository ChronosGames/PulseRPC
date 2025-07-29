using PulseRPC.Messaging;
using PulseRPC.Transport;

namespace PulseRPC;

/// <summary>
/// 通道管理器接口
/// </summary>
public interface IChannelManager : IDisposable
{
    /// <summary>
    /// 获取默认通道
    /// </summary>
    IClientChannel GetDefaultChannel();

    /// <summary>
    /// 获取通道
    /// </summary>
    IClientChannel GetChannel(string channelName);

    /// <summary>
    /// 检查通道是否存在
    /// </summary>
    bool HasChannel(string channelName);

    /// <summary>
    /// 注册通道 - 直接注册已创建的通道
    /// </summary>
    void RegisterChannel(string name, IClientChannel channel, bool isDefault = false);

    /// <summary>
    /// 注册通道 - 根据传输类型创建通道
    /// </summary>
    void RegisterChannel(string name, TransportType type, TransportOptions options, bool isDefault = false);

    /// <summary>
    /// 注销通道
    /// </summary>
    void UnregisterChannel(string channelName);

    /// <summary>
    /// 获取服务代理
    /// </summary>
    T GetService<T>() where T : class, IPulseService;

    /// <summary>
    /// 注册事件监听器
    /// </summary>
    ISubscriptionToken RegisterEventListener<T>(T listener) where T : class, IPulseEventHandler;
}
