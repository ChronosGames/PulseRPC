using System;

namespace PulseRPC.Client.Events;

/// <summary>
/// 断路器状态
/// </summary>
public sealed class CircuitBreakerState : IDisposable
{
    private readonly object _lock = new();
    private int _failureCount;
    private DateTime _lastFailureTime;
    private bool _isOpen;

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (_isOpen && DateTime.UtcNow - _lastFailureTime > TimeSpan.FromMinutes(1))
                {
                    _isOpen = false;
                    _failureCount = 0;
                }
                return _isOpen;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _isOpen = false;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            if (_failureCount >= 5)
            {
                _isOpen = true;
            }
        }
    }

    public void Dispose()
    {
        // 清理资源
    }
}
