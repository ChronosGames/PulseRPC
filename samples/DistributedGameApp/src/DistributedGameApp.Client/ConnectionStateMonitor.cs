using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Transport;

namespace DistributedGameApp.Client;

/// <summary>
/// 连接状态监控器 - 监控服务器连接健康状态
/// </summary>
public class ConnectionStateMonitor : IDisposable
{
    private readonly ILogger<ConnectionStateMonitor> _logger;
    private readonly TimeSpan _healthCheckInterval;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _connectionTimeout;

    private Timer? _healthCheckTimer;
    private Timer? _heartbeatTimer;
    private DateTime _lastSuccessfulActivity = DateTime.UtcNow;
    private IClientChannel? _monitoredChannel;
    private Func<Task<long>>? _heartbeatFunc;
    private CancellationTokenSource? _cts;
    private bool _isMonitoring = false;
    private bool _disposed = false;

    /// <summary>
    /// 连接断开时触发
    /// </summary>
    public event EventHandler<DisconnectionEventArgs>? OnDisconnected;

    /// <summary>
    /// 连接恢复时触发
    /// </summary>
    public event EventHandler<EventArgs>? OnReconnected;

    /// <summary>
    /// 心跳成功时触发
    /// </summary>
    public event EventHandler<HeartbeatEventArgs>? OnHeartbeatSuccess;

    /// <summary>
    /// 心跳失败时触发
    /// </summary>
    public event EventHandler<HeartbeatEventArgs>? OnHeartbeatFailure;

    /// <summary>
    /// 连接状态变化时触发
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? OnStateChanged;

    /// <summary>
    /// 当前连接状态
    /// </summary>
    public ConnectionState CurrentState { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// 最后一次成功活动的时间
    /// </summary>
    public DateTime LastSuccessfulActivity => _lastSuccessfulActivity;

    /// <summary>
    /// 是否正在监控
    /// </summary>
    public bool IsMonitoring => _isMonitoring;

    public ConnectionStateMonitor(
        ILogger<ConnectionStateMonitor> logger,
        TimeSpan? healthCheckInterval = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? connectionTimeout = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthCheckInterval = healthCheckInterval ?? TimeSpan.FromSeconds(10);
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// 开始监控指定的连接通道
    /// </summary>
    public void StartMonitoring(IClientChannel channel, Func<Task<long>>? heartbeatFunc = null)
    {
        if (_isMonitoring)
        {
            _logger.LogWarning("已经在监控中，请先停止当前监控");
            return;
        }

        _monitoredChannel = channel ?? throw new ArgumentNullException(nameof(channel));
        _heartbeatFunc = heartbeatFunc;
        _cts = new CancellationTokenSource();
        _isMonitoring = true;
        _lastSuccessfulActivity = DateTime.UtcNow;

        UpdateState(ConnectionState.Connected);

        _logger.LogInformation("开始监控连接状态，健康检查间隔: {Interval}秒", _healthCheckInterval.TotalSeconds);

        // 启动健康检查定时器
        _healthCheckTimer = new Timer(
            HealthCheckCallback,
            null,
            _healthCheckInterval,
            _healthCheckInterval);

        // 如果提供了心跳函数，启动心跳定时器
        if (_heartbeatFunc != null)
        {
            _heartbeatTimer = new Timer(
                HeartbeatCallback,
                null,
                _heartbeatInterval,
                _heartbeatInterval);
        }
    }

    /// <summary>
    /// 停止监控
    /// </summary>
    public void StopMonitoring()
    {
        if (!_isMonitoring)
        {
            return;
        }

        _logger.LogInformation("停止监控连接状态");

        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;

        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _isMonitoring = false;
        _monitoredChannel = null;
        _heartbeatFunc = null;

        UpdateState(ConnectionState.Disconnected);
    }

    /// <summary>
    /// 健康检查回调
    /// </summary>
    private void HealthCheckCallback(object? state)
    {
        if (!_isMonitoring || _monitoredChannel == null)
        {
            return;
        }

        try
        {
            // 检查通道是否仍然连接
            if (!_monitoredChannel.IsConnected)
            {
                _logger.LogWarning("健康检查失败：通道未连接");
                HandleDisconnection("Channel is not connected");
                return;
            }

            // 检查是否超时
            var timeSinceLastActivity = DateTime.UtcNow - _lastSuccessfulActivity;
            if (timeSinceLastActivity > _connectionTimeout)
            {
                _logger.LogWarning("健康检查失败：连接超时 ({Timeout}秒)", timeSinceLastActivity.TotalSeconds);
                HandleDisconnection($"Connection timeout after {timeSinceLastActivity.TotalSeconds:F1} seconds");
                return;
            }

            // 健康检查通过
            _logger.LogDebug("健康检查通过");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "健康检查过程中发生异常");
            HandleDisconnection($"Health check exception: {ex.Message}");
        }
    }

    /// <summary>
    /// 心跳回调
    /// </summary>
    private async void HeartbeatCallback(object? state)
    {
        if (!_isMonitoring || _heartbeatFunc == null || _cts == null)
        {
            return;
        }

        try
        {
            _logger.LogDebug("发送心跳...");

            var startTime = DateTime.UtcNow;
            var timestamp = await _heartbeatFunc();
            var duration = DateTime.UtcNow - startTime;

            _lastSuccessfulActivity = DateTime.UtcNow;

            _logger.LogDebug("心跳成功，延迟: {Latency}ms", duration.TotalMilliseconds);

            OnHeartbeatSuccess?.Invoke(this, new HeartbeatEventArgs
            {
                Timestamp = timestamp,
                Latency = duration,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "心跳失败: {Message}", ex.Message);

            OnHeartbeatFailure?.Invoke(this, new HeartbeatEventArgs
            {
                Success = false,
                ErrorMessage = ex.Message
            });

            // 心跳失败可能意味着连接有问题
            // 但我们不立即判定为断线，因为健康检查会处理
        }
    }

    /// <summary>
    /// 处理断线
    /// </summary>
    private void HandleDisconnection(string reason)
    {
        if (CurrentState == ConnectionState.Disconnected)
        {
            return; // 已经是断线状态
        }

        _logger.LogWarning("检测到断线: {Reason}", reason);

        UpdateState(ConnectionState.Disconnected);

        OnDisconnected?.Invoke(this, new DisconnectionEventArgs
        {
            Reason = reason,
            Timestamp = DateTime.UtcNow,
            LastSuccessfulActivity = _lastSuccessfulActivity
        });

        // 停止监控（等待重连后重新开始）
        StopMonitoring();
    }

    /// <summary>
    /// 记录成功的活动（由外部调用）
    /// </summary>
    public void RecordSuccessfulActivity()
    {
        _lastSuccessfulActivity = DateTime.UtcNow;

        // 如果之前是断线状态，现在可以判定为已恢复
        if (CurrentState == ConnectionState.Disconnected)
        {
            _logger.LogInformation("连接已恢复");

            UpdateState(ConnectionState.Connected);

            OnReconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 更新连接状态
    /// </summary>
    private void UpdateState(ConnectionState newState)
    {
        if (CurrentState != newState)
        {
            var oldState = CurrentState;
            CurrentState = newState;

            _logger.LogInformation("连接状态变化: {OldState} -> {NewState}", oldState, newState);

            OnStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopMonitoring();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 连接状态枚举
/// </summary>
public enum ConnectionState
{
    /// <summary>已连接</summary>
    Connected,

    /// <summary>连接中</summary>
    Connecting,

    /// <summary>重连中</summary>
    Reconnecting,

    /// <summary>已断开</summary>
    Disconnected
}

/// <summary>
/// 断线事件参数
/// </summary>
public class DisconnectionEventArgs : EventArgs
{
    /// <summary>断线原因</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>断线时间</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>最后一次成功活动的时间</summary>
    public DateTime LastSuccessfulActivity { get; set; }
}

/// <summary>
/// 心跳事件参数
/// </summary>
public class HeartbeatEventArgs : EventArgs
{
    /// <summary>服务器时间戳</summary>
    public long Timestamp { get; set; }

    /// <summary>心跳延迟</summary>
    public TimeSpan Latency { get; set; }

    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>错误消息（如果失败）</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 连接状态变化事件参数
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>旧状态</summary>
    public ConnectionState OldState { get; set; }

    /// <summary>新状态</summary>
    public ConnectionState NewState { get; set; }

    /// <summary>变化时间</summary>
    public DateTime Timestamp { get; set; }
}
