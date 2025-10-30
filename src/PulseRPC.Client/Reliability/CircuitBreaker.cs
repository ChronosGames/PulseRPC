using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using PulseRPC.Messaging;

namespace PulseRPC.Client;

/// <summary>
/// 熔断器状态
/// </summary>
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// 熔断器选项
/// </summary>
public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);
    public int SuccessThreshold { get; set; } = 2;
}

/// <summary>
/// 简单熔断器实现
/// </summary>
public class CircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private volatile CircuitBreakerState _state = CircuitBreakerState.Closed;
    private volatile int _failureCount;
    private volatile int _successCount;
    private DateTime _lastFailureTime;

    public CircuitBreaker(CircuitBreakerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public CircuitBreakerState State => _state;

    public void RecordSuccess()
    {
        if (_state == CircuitBreakerState.HalfOpen)
        {
            var count = Interlocked.Increment(ref _successCount);
            if (count >= _options.SuccessThreshold)
            {
                Reset();
            }
        }
        else if (_state == CircuitBreakerState.Closed)
        {
            Interlocked.Exchange(ref _failureCount, 0);
        }
    }

    public void RecordFailure()
    {
        _lastFailureTime = DateTime.UtcNow;

        if (_state == CircuitBreakerState.HalfOpen)
        {
            Trip();
        }
        else if (_state == CircuitBreakerState.Closed)
        {
            var count = Interlocked.Increment(ref _failureCount);
            if (count >= _options.FailureThreshold)
            {
                Trip();
            }
        }
    }

    public bool CanExecute()
    {
        if (_state == CircuitBreakerState.Closed)
            return true;

        if (_state == CircuitBreakerState.Open)
        {
            if (DateTime.UtcNow - _lastFailureTime >= _options.Timeout)
            {
                _state = CircuitBreakerState.HalfOpen;
                Interlocked.Exchange(ref _successCount, 0);
                return true;
            }
            return false;
        }

        // HalfOpen
        return true;
    }

    private void Trip()
    {
        _state = CircuitBreakerState.Open;
        _lastFailureTime = DateTime.UtcNow;
    }

    private void Reset()
    {
        _state = CircuitBreakerState.Closed;
        Interlocked.Exchange(ref _failureCount, 0);
        Interlocked.Exchange(ref _successCount, 0);
    }
}
