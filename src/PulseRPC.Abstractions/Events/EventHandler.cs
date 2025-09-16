using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC;

/// <summary>
/// PulseRPC 事件处理器基础接口 - 标记类可接收服务器推送的事件
/// </summary>
public interface IPulseEventHandler
{
    // 所有事件监听器都应继承此接口
}

/// <summary>
/// 事件处理委托
/// </summary>
/// <typeparam name="T">事件数据类型</typeparam>
/// <param name="sender">事件源</param>
/// <param name="eventData">事件数据</param>
public delegate void EventHandler<T>(object? sender, T eventData);

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
