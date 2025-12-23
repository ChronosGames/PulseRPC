using System;

namespace PulseRPC.Client.Events;

/// <summary>
/// 智能订阅令牌 - 增强的订阅管理
/// </summary>
public sealed class SmartSubscriptionToken : ISubscriptionToken
{
    private readonly ISubscriptionToken _innerToken;
    private readonly SubscriptionContext _context;
    private readonly Action _onDispose;
    private bool _disposed;

    public Guid Id => _innerToken.Id;
    public bool IsActive => !_disposed && _innerToken.IsActive;
    public bool IsUnsubscribed => _disposed || _innerToken.IsUnsubscribed;

    public SmartSubscriptionToken(ISubscriptionToken innerToken, SubscriptionContext context, Action onDispose)
    {
        _innerToken = innerToken;
        _context = context;
        _onDispose = onDispose;
    }

    public void Unsubscribe()
    {
        if (_disposed) return;

        _innerToken.Unsubscribe();
        _onDispose();
        _context.Dispose();
        _disposed = true;
    }

    public void Dispose()
    {
        Unsubscribe();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 获取订阅的性能指标
    /// </summary>
    public EventHandlerMetrics GetMetrics()
    {
        return _context.Metrics.GetSnapshot();
    }
}
