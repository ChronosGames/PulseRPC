using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC;

/// <summary>
/// PulseRPC 事件处理器基础接口 - 标记类可接收服务器推送的事件
/// </summary>
/// <remarks>
/// 此接口已过时。请直接使用 <see cref="IPulseHub"/> 并通过 <see cref="ChannelAttribute"/> 配置通道和认证信息。
/// <para>
/// 旧用法：<c>public interface IGameReceiver : IPulseReceiver { }</c>
/// </para>
/// <para>
/// 新用法：<c>[Channel("CLIENT")] public interface IGameReceiver : IPulseHub { }</c>
/// </para>
/// </remarks>
[Obsolete("请直接使用 IPulseHub 并通过 ChannelAttribute 配置通道信息。此接口将在未来版本中移除。", false)]
public interface IPulseReceiver : IPulseHub
{
    // 所有事件监听器都应继承此接口
    // 为了向后兼容，此接口现在继承 IPulseHub
}

/// <summary>
/// 订阅令牌接口
/// </summary>
public interface ISubscriptionToken : IDisposable
{
    /// <summary>
    /// 订阅标识
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// 是否活跃
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// 是否已取消订阅
    /// </summary>
    bool IsUnsubscribed { get; }

    /// <summary>
    /// 取消订阅
    /// </summary>
    void Unsubscribe();
}

/// <summary>
/// 事件总线接口
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 发布事件
    /// </summary>
    Task PublishAsync<T>(string eventName, T eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅事件
    /// </summary>
    ISubscriptionToken Subscribe<T>(string eventName, EventHandler<T> handler);

    /// <summary>
    /// 取消订阅
    /// </summary>
    void Unsubscribe(ISubscriptionToken token);
}
