using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Protocol.Network;

/// <summary>
/// 心跳管理器配置
/// </summary>
public class HeartbeatOptions
{
    /// <summary>
    /// 心跳间隔（毫秒）
    /// </summary>
    public int Interval { get; set; } = 30000;

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int Timeout { get; set; } = 5000;

    /// <summary>
    /// 最大允许的连续超时次数
    /// </summary>
    public int MaxTimeoutCount { get; set; } = 3;
}

/// <summary>
/// 心跳管理器
/// </summary>
public class HeartbeatManager : IDisposable
{
    private readonly ILogger _logger;
    private readonly HeartbeatOptions _options;
    private readonly SessionContext _session;
    private readonly CancellationTokenSource _cts;
    private readonly Task _heartbeatTask;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<HeartbeatResponse>> _pendingResponses;
    private long _sequence;
    private int _timeoutCount;
    private readonly Stopwatch _rttStopwatch;
    private long _lastRtt;

    /// <summary>
    /// 连接断开事件
    /// </summary>
    public event EventHandler? ConnectionLost;

    /// <summary>
    /// 获取最后一次测量的RTT（毫秒）
    /// </summary>
    public long LastRtt => Interlocked.Read(ref _lastRtt);

    /// <summary>
    /// 初始化心跳管理器
    /// </summary>
    public HeartbeatManager(SessionContext session, ILogger logger, HeartbeatOptions? options = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new HeartbeatOptions();
        _cts = new CancellationTokenSource();
        _pendingResponses = new ConcurrentDictionary<long, TaskCompletionSource<HeartbeatResponse>>();
        _rttStopwatch = new Stopwatch();

        // 启动心跳任务
        _heartbeatTask = StartHeartbeatAsync(_cts.Token);
    }

    /// <summary>
    /// 处理心跳响应
    /// </summary>
    public void HandleHeartbeatResponse(HeartbeatResponse response)
    {
        if (_pendingResponses.TryRemove(response.Sequence, out var tcs))
        {
            tcs.TrySetResult(response);

            // 计算RTT
            var rtt = (response.ResponseTimestamp - response.OriginalTimestamp) / 2;
            Interlocked.Exchange(ref _lastRtt, rtt);

            // 重置超时计数
            Interlocked.Exchange(ref _timeoutCount, 0);

            _logger.LogTrace("收到心跳响应 Sequence={Sequence}, RTT={Rtt}ms", response.Sequence, rtt);
        }
    }

    private async Task StartHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Interval, cancellationToken);

                var sequence = Interlocked.Increment(ref _sequence);
                var message = MessagePool.Get<HeartbeatMessage>();
                try
                {
                    message.Sequence = sequence;
                    message.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    var tcs = new TaskCompletionSource<HeartbeatResponse>();
                    _pendingResponses[sequence] = tcs;

                    // 发送心跳消息
                    await _session.SendAsync(message, MessageFlags.Heartbeat);

                    // 等待响应或超时
                    using var timeoutCts = new CancellationTokenSource(_options.Timeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    try
                    {
                        await Task.WhenAny(tcs.Task, Task.Delay(_options.Timeout, linkedCts.Token));
                        if (!tcs.Task.IsCompleted)
                        {
                            throw new OperationCanceledException("心跳响应超时");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 心跳超时
                        var currentTimeoutCount = Interlocked.Increment(ref _timeoutCount);
                        _logger.LogWarning("心跳超时 Sequence={Sequence}, TimeoutCount={TimeoutCount}/{MaxTimeoutCount}",
                            sequence, currentTimeoutCount, _options.MaxTimeoutCount);

                        if (currentTimeoutCount >= _options.MaxTimeoutCount)
                        {
                            _logger.LogError("连续{Count}次心跳超时，判定连接已断开", currentTimeoutCount);
                            ConnectionLost?.Invoke(this, EventArgs.Empty);
                            break;
                        }
                    }
                }
                finally
                {
                    MessagePool.Return(message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "心跳任务发生错误");
                await Task.Delay(1000, cancellationToken); // 发生错误时等待一段时间再重试
            }
        }
    }

    /// <summary>
    /// 停止心跳检测
    /// </summary>
    public async Task StopAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            await _heartbeatTask;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
