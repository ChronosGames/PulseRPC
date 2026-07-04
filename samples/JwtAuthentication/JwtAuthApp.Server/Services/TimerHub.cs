using System.Collections.Concurrent;
using JwtAuthApp.Shared;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Contexts;

namespace JwtAuthApp.Server.Services;

/// <summary>
/// 定时器 Hub 实现 - 通过 <see cref="IHubContext{TReceiver}"/> 经统一的 <c>IPulseRouter</c>
/// 向调用方连接周期性推送 <see cref="ITimerReceiver.OnTickAsync"/>（§P3 fanout-via-router）。
/// </summary>
public class TimerHub : ITimerHub
{
    private readonly IHubContext<ITimerReceiver> _timerReceiverContext;
    private readonly ILogger<TimerHub> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _timers = new();

    public TimerHub(IHubContext<ITimerReceiver> timerReceiverContext, ILogger<TimerHub> logger)
    {
        _timerReceiverContext = timerReceiverContext ?? throw new ArgumentNullException(nameof(timerReceiverContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(TimeSpan interval)
    {
        var connectionId = RequireConnectionId();
        var userId = PulseContext.CurrentUserId;

        var cts = new CancellationTokenSource();
        if (!_timers.TryAdd(connectionId, cts))
        {
            throw new InvalidOperationException("The timer has already been started for this connection.");
        }

        _ = RunTimerLoopAsync(connectionId, userId, interval, cts.Token);

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        var connectionId = RequireConnectionId();
        if (_timers.TryRemove(connectionId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        return Task.CompletedTask;
    }

    private async Task RunTimerLoopAsync(string connectionId, string? userId, TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _timerReceiverContext.Clients.Single(connectionId)
                    .OnTickAsync($"UserId={userId}; Time={DateTimeOffset.UtcNow:O}")
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Timer loop failed for connection {ConnectionId}", connectionId);
        }
    }

    private static string RequireConnectionId()
    {
        return PulseContext.CurrentConnectionId
            ?? throw new InvalidOperationException("No active connection.");
    }
}
