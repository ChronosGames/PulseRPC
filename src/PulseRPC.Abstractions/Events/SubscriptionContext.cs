using System;

namespace PulseRPC.Client.Events;

/// <summary>
/// 订阅上下文 - 管理单个订阅的状态和指标
/// </summary>
public sealed class SubscriptionContext : IDisposable
{
    public object Subscriber { get; }
    public SmartSubscriptionOptions Options { get; }
    public EventMetrics Metrics { get; }
    public DateTime CreatedAt { get; }
    public CircuitBreakerState? CircuitBreaker { get; }

    public SubscriptionContext(object subscriber, SmartSubscriptionOptions options, EventMetrics metrics)
    {
        Subscriber = subscriber;
        Options = options;
        Metrics = metrics;
        CreatedAt = DateTime.UtcNow;

        if (options.EnableCircuitBreaker)
        {
            CircuitBreaker = new CircuitBreakerState();
        }
    }

    public void Dispose()
    {
        CircuitBreaker?.Dispose();
    }
}
