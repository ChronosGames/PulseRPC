using System;
using System.Collections.Generic;

namespace PulseRPC.Server.Scheduling;

/// <summary>
/// 移动窗口计数器 - 用于计算吞吐量
/// </summary>
internal class MovingWindowCounter
{
    private readonly TimeSpan _windowSize;
    private readonly object _lock = new object();
    private readonly Queue<(DateTime timestamp, long count)> _samples = new();
    private long _totalCount = 0;

    public MovingWindowCounter(TimeSpan windowSize)
    {
        _windowSize = windowSize;
    }

    public void Increment(long count = 1)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _samples.Enqueue((now, count));
            _totalCount += count;

            // 清理过期样本
            var cutoff = now - _windowSize;
            while (_samples.Count > 0 && _samples.Peek().timestamp < cutoff)
            {
                var expired = _samples.Dequeue();
                _totalCount -= expired.count;
            }
        }
    }

    public double GetRate()
    {
        lock (_lock)
        {
            if (_samples.Count == 0)
                return 0.0;

            // 清理过期样本
            var now = DateTime.UtcNow;
            var cutoff = now - _windowSize;
            while (_samples.Count > 0 && _samples.Peek().timestamp < cutoff)
            {
                var expired = _samples.Dequeue();
                _totalCount -= expired.count;
            }

            if (_samples.Count == 0)
                return 0.0;

            var oldestSample = _samples.Peek();
            var actualWindow = now - oldestSample.timestamp;

            if (actualWindow.TotalSeconds <= 0)
                return 0.0;

            return _totalCount / actualWindow.TotalSeconds;
        }
    }
}
