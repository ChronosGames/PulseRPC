using System;
using System.Collections.Generic;
using System.Linq;

namespace PulseRPC;

/// <summary>
/// 订阅令牌实现
/// </summary>
public class SubscriptionToken : ISubscriptionToken
{
    private readonly Action _unsubscribeAction;
    private bool _isDisposed;

    /// <summary>
    /// 获取订阅标识
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 获取事件名称
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// 获取事件数据类型
    /// </summary>
    public Type EventType { get; }

    /// <summary>
    /// 是否活跃
    /// </summary>
    public bool IsActive => !_isDisposed;

    /// <summary>
    /// 是否已取消订阅
    /// </summary>
    public bool IsUnsubscribed => _isDisposed;

    /// <summary>
    /// 创建订阅令牌
    /// </summary>
    public SubscriptionToken(Guid id, string eventName, Type eventType, Action unsubscribeAction)
    {
        Id = id;
        EventName = eventName;
        EventType = eventType;
        _unsubscribeAction = unsubscribeAction ?? throw new ArgumentNullException(nameof(unsubscribeAction));
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public void Unsubscribe()
    {
        if (_isDisposed)
            return;

        _unsubscribeAction();
        _isDisposed = true;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Unsubscribe();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 组合订阅令牌
/// </summary>
public class CompositeSubscriptionToken : ISubscriptionToken
{
    private readonly List<ISubscriptionToken> _tokens;
    private bool _isDisposed;

    /// <summary>
    /// 获取订阅标识
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 是否活跃
    /// </summary>
    public bool IsActive => !_isDisposed;

    /// <summary>
    /// 是否已取消订阅
    /// </summary>
    public bool IsUnsubscribed => _isDisposed;

    /// <summary>
    /// 创建组合订阅令牌
    /// </summary>
    public CompositeSubscriptionToken(IEnumerable<ISubscriptionToken> tokens)
    {
        Id = Guid.NewGuid();
        _tokens = tokens.ToList();
    }

    /// <summary>
    /// 取消所有订阅
    /// </summary>
    public void Unsubscribe()
    {
        if (_isDisposed)
            return;

        foreach (var token in _tokens)
        {
            token.Unsubscribe();
        }

        _isDisposed = true;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Unsubscribe();
        GC.SuppressFinalize(this);
    }
}
