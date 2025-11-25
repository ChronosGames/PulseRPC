using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

// ========================
// 1. 消息类型定义
// ========================

/// <summary>
/// 定时器消息
/// </summary>
internal class TimerMessage : ServiceMessage
{
    public string TimerId { get; set; } = string.Empty;
    public Func<Task> Callback { get; set; } = null!;
    public bool IsRecurring { get; set; }
    public TimeSpan? Interval { get; set; }

    public TimerMessage()
    {
        Type = ActorMessageType.Timer;
    }
}

/// <summary>
/// 系统消息
/// </summary>
internal class SystemMessage : ServiceMessage
{
    public string Command { get; set; } = string.Empty;
    public object? Data { get; set; }

    public SystemMessage()
    {
        Type = ActorMessageType.System;
    }
}

// ========================
// 2. 定时器管理器
// ========================

/// <summary>
/// 定时器信息
/// </summary>
public class TimerInfo
{
    public string TimerId { get; set; } = string.Empty;
    public TimeSpan DueTime { get; set; }
    public TimeSpan? Period { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastFireTime { get; set; }
    public DateTime? NextFireTime { get; set; }
    public long FireCount { get; set; }
}

/// <summary>
/// Actor定时器 - 将定时器回调调度到Actor的消息队列
/// </summary>
internal class ServiceTimer : IAsyncDisposable
{
    private readonly string _timerId;
    private readonly Func<Task> _callback;
    private readonly Channel<TimerMessage> _messageChannel;
    private readonly ILogger _logger;

    private Timer? _systemTimer;
    private TimeSpan _dueTime;
    private TimeSpan? _period;
    private long _fireCount;
    private DateTime? _lastFireTime;
    private bool _isActive;
    private readonly Lock _lock = new();

    public string TimerId => _timerId;
    public bool IsActive => _isActive;
    public long FireCount => _fireCount;
    public DateTime? LastFireTime => _lastFireTime;

    public DateTime? NextFireTime
    {
        get
        {
            if (!_isActive) return null;
            if (_lastFireTime == null) return DateTime.UtcNow.Add(_dueTime);
            if (_period == null) return null;
            return _lastFireTime.Value.Add(_period.Value);
        }
    }

    public ServiceTimer(
        string timerId,
        TimeSpan dueTime,
        TimeSpan? period,
        Func<Task> callback,
        Channel<TimerMessage> messageChannel,
        ILogger logger)
    {
        _timerId = timerId;
        _dueTime = dueTime;
        _period = period;
        _callback = callback;
        _messageChannel = messageChannel;
        _logger = logger;
    }

    public void Start()
    {
        using (_lock.EnterScope())
        {
            if (_isActive)
                return;

            var periodMs = _period.HasValue
                ? (int)_period.Value.TotalMilliseconds
                : Timeout.Infinite;

            _systemTimer = new Timer(
                TimerCallback,
                null,
                _dueTime,
                _period ?? TimeSpan.FromMilliseconds(-1));

            _isActive = true;

            _logger.LogDebug(
                "Timer started - TimerId: {TimerId}, DueTime: {DueTime}ms, Period: {Period}",
                _timerId,
                _dueTime.TotalMilliseconds,
                _period?.TotalMilliseconds.ToString() ?? "None");
        }
    }

    public void Stop()
    {
        using (_lock.EnterScope())
        {
            if (!_isActive)
                return;

            _systemTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _isActive = false;

            _logger.LogDebug("Timer stopped - TimerId: {TimerId}", _timerId);
        }
    }

    private void TimerCallback(object? state)
    {
        try
        {
            Interlocked.Increment(ref _fireCount);
            _lastFireTime = DateTime.UtcNow;

            // 将定时器消息放入Actor的消息队列
            var message = new TimerMessage
            {
                TimerId = _timerId,
                Callback = _callback,
                IsRecurring = _period.HasValue,
                Interval = _period
            };

            // 非阻塞写入
            if (!_messageChannel.Writer.TryWrite(message))
            {
                _logger.LogWarning(
                    "Timer message dropped - TimerId: {TimerId}, Queue is full",
                    _timerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in timer callback - TimerId: {TimerId}", _timerId);
        }
    }

    public TimerInfo GetInfo()
    {
        return new TimerInfo
        {
            TimerId = _timerId,
            DueTime = _dueTime,
            Period = _period,
            IsActive = _isActive,
            LastFireTime = _lastFireTime,
            NextFireTime = NextFireTime,
            FireCount = _fireCount
        };
    }

    public async ValueTask DisposeAsync()
    {
        Stop();

        if (_systemTimer != null)
        {
            await _systemTimer.DisposeAsync();
        }
    }

    /// <summary>
    /// 简单的 Timer 包装器，用于统一管理定时器
    /// </summary>
    public class TimerWrapper : IDisposable
    {
        private readonly Timer _timer;

        public TimerWrapper(Timer timer)
        {
            _timer = timer;
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
