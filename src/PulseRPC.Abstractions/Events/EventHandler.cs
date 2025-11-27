using System;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC;

// IPulseReceiver 已移动到独立文件 IPulseReceiver.cs

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
