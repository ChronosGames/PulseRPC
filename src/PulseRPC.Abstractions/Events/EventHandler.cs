using System;

namespace PulseRPC;

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
