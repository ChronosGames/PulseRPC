using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Client;

/// <summary>
/// 重连管理器 - 负责处理服务器断线和自动重连
/// </summary>
public class ReconnectionManager : IDisposable
{
    private readonly GameClient _client;
    private readonly ILogger<ReconnectionManager> _logger;
    private readonly ReconnectionStrategy _strategy;

    // 保存的状态
    private ServerInfo? _lastGameServer;
    private string? _lastCharacterId;
    private bool _isReconnecting = false;
    private CancellationTokenSource? _reconnectionCts;

    // 重连统计
    private int _totalDisconnections = 0;
    private int _successfulReconnections = 0;
    private int _failedReconnections = 0;
    private DateTime? _lastDisconnectionTime;

    /// <summary>
    /// 开始重连时触发
    /// </summary>
    public event EventHandler<ReconnectionEventArgs>? OnReconnecting;

    /// <summary>
    /// 重连成功时触发
    /// </summary>
    public event EventHandler<ReconnectionEventArgs>? OnReconnected;

    /// <summary>
    /// 重连失败时触发
    /// </summary>
    public event EventHandler<ReconnectionEventArgs>? OnReconnectionFailed;

    /// <summary>
    /// 重连进度更新时触发
    /// </summary>
    public event EventHandler<ReconnectionProgressEventArgs>? OnReconnectionProgress;

    /// <summary>
    /// 是否启用自动重连
    /// </summary>
    public bool AutoReconnectEnabled { get; set; } = true;

    /// <summary>
    /// 是否正在重连中
    /// </summary>
    public bool IsReconnecting => _isReconnecting;

    /// <summary>
    /// 重连统计信息
    /// </summary>
    public ReconnectionStatistics Statistics => new ReconnectionStatistics
    {
        TotalDisconnections = _totalDisconnections,
        SuccessfulReconnections = _successfulReconnections,
        FailedReconnections = _failedReconnections,
        LastDisconnectionTime = _lastDisconnectionTime
    };

    public ReconnectionManager(
        GameClient client,
        ILogger<ReconnectionManager> logger,
        ReconnectionStrategy? strategy = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategy = strategy ?? ReconnectionStrategy.ExponentialBackoff();
    }

    /// <summary>
    /// 处理断线事件
    /// </summary>
    public async Task HandleDisconnectionAsync(string reason, CancellationToken cancellationToken = default)
    {
        _totalDisconnections++;
        _lastDisconnectionTime = DateTime.UtcNow;

        _logger.LogWarning("检测到断线: {Reason}, 总断线次数: {Count}", reason, _totalDisconnections);

        // 保存当前状态
        SaveCurrentState();

        // 如果启用了自动重连
        if (AutoReconnectEnabled)
        {
            await ReconnectAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation("自动重连已禁用，等待手动重连");
        }
    }

    /// <summary>
    /// 执行重连
    /// </summary>
    public async Task<bool> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isReconnecting)
        {
            _logger.LogWarning("已经在重连中，忽略重复请求");
            return false;
        }

        _isReconnecting = true;
        _reconnectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _logger.LogInformation("开始重连流程...");

            OnReconnecting?.Invoke(this, new ReconnectionEventArgs
            {
                Attempt = 0,
                Timestamp = DateTime.UtcNow,
                Reason = "Starting reconnection"
            });

            var success = await _strategy.ExecuteAsync(
                async (attempt) =>
                {
                    _logger.LogInformation("重连尝试 #{Attempt}/{MaxAttempts}", attempt, _strategy.MaxRetries);

                    OnReconnectionProgress?.Invoke(this, new ReconnectionProgressEventArgs
                    {
                        CurrentAttempt = attempt,
                        MaxAttempts = _strategy.MaxRetries,
                        Timestamp = DateTime.UtcNow
                    });

                    // 步骤 1: 刷新登录令牌
                    if (!string.IsNullOrEmpty(_client.AccessToken))
                    {
                        try
                        {
                            await _client.RefreshTokenAsync(_reconnectionCts.Token);
                            _logger.LogInformation("令牌刷新成功");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "令牌刷新失败，将尝试完全重新登录");
                            throw;
                        }
                    }

                    // 步骤 2: 重连到 GameServer
                    if (_lastGameServer != null)
                    {
                        _logger.LogInformation("正在重连到 GameServer: {ServerName}", _lastGameServer.ServerName);

                        await _client.ConnectToGameServerAsync(
                            _lastGameServer,
                            _reconnectionCts.Token);

                        _logger.LogInformation("GameServer 重连成功");
                    }

                    // 步骤 3: 恢复角色状态
                    if (!string.IsNullOrEmpty(_lastCharacterId))
                    {
                        _logger.LogInformation("正在恢复角色状态: {CharacterId}", _lastCharacterId);

                        await _client.SelectCharacterAsync(
                            _lastCharacterId,
                            _reconnectionCts.Token);

                        _logger.LogInformation("角色状态恢复成功");
                    }

                    return true;
                },
                _reconnectionCts.Token);

            if (success)
            {
                _successfulReconnections++;

                _logger.LogInformation("重连成功! 成功次数: {Count}", _successfulReconnections);

                OnReconnected?.Invoke(this, new ReconnectionEventArgs
                {
                    Success = true,
                    Timestamp = DateTime.UtcNow,
                    Reason = "Reconnection successful"
                });

                return true;
            }
            else
            {
                _failedReconnections++;

                _logger.LogError("重连失败，已达到最大重试次数。失败次数: {Count}", _failedReconnections);

                OnReconnectionFailed?.Invoke(this, new ReconnectionEventArgs
                {
                    Success = false,
                    Timestamp = DateTime.UtcNow,
                    Reason = "Max retries exceeded"
                });

                return false;
            }
        }
        catch (Exception ex)
        {
            _failedReconnections++;

            _logger.LogError(ex, "重连过程中发生异常");

            OnReconnectionFailed?.Invoke(this, new ReconnectionEventArgs
            {
                Success = false,
                Timestamp = DateTime.UtcNow,
                Reason = ex.Message
            });

            return false;
        }
        finally
        {
            _isReconnecting = false;
            _reconnectionCts?.Dispose();
            _reconnectionCts = null;
        }
    }

    /// <summary>
    /// 取消正在进行的重连
    /// </summary>
    public void CancelReconnection()
    {
        if (_isReconnecting && _reconnectionCts != null)
        {
            _logger.LogInformation("取消重连...");
            _reconnectionCts.Cancel();
        }
    }

    /// <summary>
    /// 保存当前状态
    /// </summary>
    public void SaveCurrentState()
    {
        _lastGameServer = _client.CurrentGameServer;
        _lastCharacterId = _client.CurrentCharacter?.CharacterId;

        _logger.LogDebug("已保存客户端状态: Server={Server}, Character={Character}",
            _lastGameServer?.ServerName ?? "None",
            _lastCharacterId ?? "None");
    }

    /// <summary>
    /// 清除保存的状态
    /// </summary>
    public void ClearSavedState()
    {
        _lastGameServer = null;
        _lastCharacterId = null;

        _logger.LogDebug("已清除保存的状态");
    }

    public void Dispose()
    {
        _reconnectionCts?.Cancel();
        _reconnectionCts?.Dispose();
    }
}

/// <summary>
/// 重连事件参数
/// </summary>
public class ReconnectionEventArgs : EventArgs
{
    public int Attempt { get; set; }
    public bool Success { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 重连进度事件参数
/// </summary>
public class ReconnectionProgressEventArgs : EventArgs
{
    public int CurrentAttempt { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 重连统计信息
/// </summary>
public class ReconnectionStatistics
{
    public int TotalDisconnections { get; set; }
    public int SuccessfulReconnections { get; set; }
    public int FailedReconnections { get; set; }
    public DateTime? LastDisconnectionTime { get; set; }

    public double SuccessRate =>
        TotalDisconnections > 0
            ? (double)SuccessfulReconnections / TotalDisconnections
            : 0;
}

/// <summary>
/// 重连策略
/// </summary>
public class ReconnectionStrategy
{
    public int MaxRetries { get; set; }
    public Func<int, TimeSpan> DelayCalculator { get; set; }

    public ReconnectionStrategy(int maxRetries, Func<int, TimeSpan> delayCalculator)
    {
        MaxRetries = maxRetries;
        DelayCalculator = delayCalculator;
    }

    /// <summary>
    /// 指数退避策略（推荐）
    /// 延迟序列: 1s, 2s, 4s, 8s, 16s, 32s, 64s, 128s, 256s, 300s (max)
    /// </summary>
    public static ReconnectionStrategy ExponentialBackoff(
        int maxRetries = 10,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        var initial = initialDelay ?? TimeSpan.FromSeconds(1);
        var max = maxDelay ?? TimeSpan.FromMinutes(5);

        return new ReconnectionStrategy(
            maxRetries,
            attempt =>
            {
                var exponential = TimeSpan.FromSeconds(
                    initial.TotalSeconds * Math.Pow(2, attempt - 1));
                return exponential > max ? max : exponential;
            });
    }

    /// <summary>
    /// 固定间隔策略
    /// 延迟序列: 5s, 5s, 5s, ...
    /// </summary>
    public static ReconnectionStrategy FixedInterval(
        int maxRetries = 20,
        TimeSpan? interval = null)
    {
        var delay = interval ?? TimeSpan.FromSeconds(5);

        return new ReconnectionStrategy(
            maxRetries,
            _ => delay);
    }

    /// <summary>
    /// 渐进式策略
    /// 延迟序列: 1s, 2s, 5s, 10s, 15s, 30s, 60s
    /// </summary>
    public static ReconnectionStrategy Progressive()
    {
        var delays = new[] { 1, 2, 5, 10, 15, 30, 60 };

        return new ReconnectionStrategy(
            delays.Length,
            attempt =>
            {
                var index = Math.Min(attempt - 1, delays.Length - 1);
                return TimeSpan.FromSeconds(delays[index]);
            });
    }

    /// <summary>
    /// 执行带重试的操作
    /// </summary>
    public async Task<bool> ExecuteAsync(
        Func<int, Task<bool>> action,
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var result = await action(attempt);
                if (result)
                {
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 如果不是最后一次尝试，等待后重试
                if (attempt < MaxRetries)
                {
                    var delay = DelayCalculator(attempt);
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        return false;
    }
}
