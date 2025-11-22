using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using System.Diagnostics;

namespace PulseRPC.Server;

/// <summary>
/// 优化的 Service 代理 - 自动选择本地/远程调用
/// </summary>
/// <remarks>
/// 智能代理，根据目标 Service 是否在本地自动选择最优调用方式：
/// - 本地调用：零拷贝，直接传递对象引用（延迟 &lt; 100ns）
/// - 远程调用：序列化 + 网络传输（延迟 &lt; 1ms）
/// </remarks>
public sealed class OptimizedServiceProxy
{
    private readonly LocalServiceInvoker _localInvoker;
    private readonly RemoteServiceInvoker _remoteInvoker;
    private readonly ILogger<OptimizedServiceProxy> _logger;
    private readonly ServiceCallMetrics _metrics;

    /// <summary>
    /// 构造函数
    /// </summary>
    public OptimizedServiceProxy(
        LocalServiceInvoker localInvoker,
        RemoteServiceInvoker remoteInvoker,
        ILogger<OptimizedServiceProxy> logger)
    {
        _localInvoker = localInvoker ?? throw new ArgumentNullException(nameof(localInvoker));
        _remoteInvoker = remoteInvoker ?? throw new ArgumentNullException(nameof(remoteInvoker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = new ServiceCallMetrics();
    }

    /// <summary>
    /// 调用 Service（无返回值）
    /// </summary>
    /// <param name="targetPID">目标 Service PID</param>
    /// <param name="protocolId">协议号</param>
    /// <param name="args">参数</param>
    /// <param name="callerContext">调用者认证上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task InvokeAsync(
        PID targetPID,
        ProtocolId protocolId,
        object?[] args,
        IServiceRequestContext callerContext,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 判断是本地还是远程调用
            if (_localInvoker.IsLocalService(targetPID))
            {
                // 本地调用（零拷贝）
                await _localInvoker.InvokeLocalAsync(
                    targetPID,
                    protocolId,
                    args,
                    callerContext,
                    cancellationToken);

                stopwatch.Stop();
                _metrics.RecordLocalCall(stopwatch.Elapsed);

                _logger.LogDebug(
                    "Local call completed - Target: {TargetPID}, Protocol: {ProtocolId}, Duration: {Duration}μs",
                    targetPID, protocolId, stopwatch.Elapsed.TotalMicroseconds);
            }
            else
            {
                // 远程调用（序列化 + 网络传输）
                await _remoteInvoker.InvokeRemoteAsync(
                    targetPID,
                    protocolId,
                    args,
                    callerContext,
                    cancellationToken);

                stopwatch.Stop();
                _metrics.RecordRemoteCall(stopwatch.Elapsed);

                _logger.LogDebug(
                    "Remote call completed - Target: {TargetPID}, Protocol: {ProtocolId}, Duration: {Duration}ms",
                    targetPID, protocolId, stopwatch.Elapsed.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordFailedCall();

            _logger.LogError(ex,
                "Service call failed - Target: {TargetPID}, Protocol: {ProtocolId}, Duration: {Duration}ms",
                targetPID, protocolId, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 调用 Service（有返回值）
    /// </summary>
    /// <typeparam name="TResult">返回值类型</typeparam>
    /// <param name="targetPID">目标 Service PID</param>
    /// <param name="protocolId">协议号</param>
    /// <param name="args">参数</param>
    /// <param name="callerContext">调用者认证上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>返回值</returns>
    public async Task<TResult> InvokeAsync<TResult>(
        PID targetPID,
        ProtocolId protocolId,
        object?[] args,
        IServiceRequestContext callerContext,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            TResult result;

            // 判断是本地还是远程调用
            if (_localInvoker.IsLocalService(targetPID))
            {
                // 本地调用（零拷贝）
                result = await _localInvoker.InvokeLocalAsync<TResult>(
                    targetPID,
                    protocolId,
                    args,
                    callerContext,
                    cancellationToken);

                stopwatch.Stop();
                _metrics.RecordLocalCall(stopwatch.Elapsed);

                _logger.LogDebug(
                    "Local call with result completed - Target: {TargetPID}, Protocol: {ProtocolId}, Duration: {Duration}μs",
                    targetPID, protocolId, stopwatch.Elapsed.TotalMicroseconds);
            }
            else
            {
                // 远程调用（序列化 + 网络传输）
                result = await _remoteInvoker.InvokeRemoteAsync<TResult>(
                    targetPID,
                    protocolId,
                    args,
                    callerContext,
                    cancellationToken);

                stopwatch.Stop();
                _metrics.RecordRemoteCall(stopwatch.Elapsed);

                _logger.LogDebug(
                    "Remote call with result completed - Target: {TargetPID}, Protocol: {ProtocolId}, Duration: {Duration}ms",
                    targetPID, protocolId, stopwatch.Elapsed.TotalMilliseconds);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordFailedCall();

            _logger.LogError(ex,
                "Service call with result failed - Target: {TargetPID}, Protocol: {ProtocolId}, Duration: {Duration}ms",
                targetPID, protocolId, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 检查目标 Service 的类型（本地/远程）
    /// </summary>
    /// <param name="targetPID">目标 PID</param>
    /// <returns>本地或远程</returns>
    public ServiceLocationType GetServiceLocation(PID targetPID)
    {
        if (_localInvoker.IsLocalService(targetPID))
            return ServiceLocationType.Local;

        if (_remoteInvoker.IsRemoteService(targetPID))
            return ServiceLocationType.Remote;

        return ServiceLocationType.Unknown;
    }

    /// <summary>
    /// 获取调用统计信息
    /// </summary>
    public ServiceCallMetricsSnapshot GetMetrics() => _metrics.GetSnapshot();
}

/// <summary>
/// Service 位置类型
/// </summary>
public enum ServiceLocationType
{
    /// <summary>未知</summary>
    Unknown,
    /// <summary>本地</summary>
    Local,
    /// <summary>远程</summary>
    Remote
}

/// <summary>
/// Service 调用统计指标
/// </summary>
public sealed class ServiceCallMetrics
{
    private long _totalLocalCalls;
    private long _totalRemoteCalls;
    private long _totalFailedCalls;

    // 本地调用延迟统计（微秒）
    private long _localCallTotalMicroseconds;
    private long _localCallMinMicroseconds = long.MaxValue;
    private long _localCallMaxMicroseconds;

    // 远程调用延迟统计（微秒）
    private long _remoteCallTotalMicroseconds;
    private long _remoteCallMinMicroseconds = long.MaxValue;
    private long _remoteCallMaxMicroseconds;

    private readonly object _lock = new();

    internal void RecordLocalCall(TimeSpan duration)
    {
        var microseconds = (long)duration.TotalMicroseconds;

        lock (_lock)
        {
            Interlocked.Increment(ref _totalLocalCalls);
            Interlocked.Add(ref _localCallTotalMicroseconds, microseconds);

            // 更新最小值
            if (microseconds < _localCallMinMicroseconds)
                _localCallMinMicroseconds = microseconds;

            // 更新最大值
            if (microseconds > _localCallMaxMicroseconds)
                _localCallMaxMicroseconds = microseconds;
        }
    }

    internal void RecordRemoteCall(TimeSpan duration)
    {
        var microseconds = (long)duration.TotalMicroseconds;

        lock (_lock)
        {
            Interlocked.Increment(ref _totalRemoteCalls);
            Interlocked.Add(ref _remoteCallTotalMicroseconds, microseconds);

            // 更新最小值
            if (microseconds < _remoteCallMinMicroseconds)
                _remoteCallMinMicroseconds = microseconds;

            // 更新最大值
            if (microseconds > _remoteCallMaxMicroseconds)
                _remoteCallMaxMicroseconds = microseconds;
        }
    }

    internal void RecordFailedCall()
    {
        Interlocked.Increment(ref _totalFailedCalls);
    }

    public ServiceCallMetricsSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new ServiceCallMetricsSnapshot
            {
                TotalLocalCalls = _totalLocalCalls,
                TotalRemoteCalls = _totalRemoteCalls,
                TotalFailedCalls = _totalFailedCalls,
                AverageLocalCallMicroseconds = _totalLocalCalls > 0
                    ? (double)_localCallTotalMicroseconds / _totalLocalCalls
                    : 0,
                MinLocalCallMicroseconds = _localCallMinMicroseconds == long.MaxValue
                    ? 0
                    : _localCallMinMicroseconds,
                MaxLocalCallMicroseconds = _localCallMaxMicroseconds,
                AverageRemoteCallMicroseconds = _totalRemoteCalls > 0
                    ? (double)_remoteCallTotalMicroseconds / _totalRemoteCalls
                    : 0,
                MinRemoteCallMicroseconds = _remoteCallMinMicroseconds == long.MaxValue
                    ? 0
                    : _remoteCallMinMicroseconds,
                MaxRemoteCallMicroseconds = _remoteCallMaxMicroseconds
            };
        }
    }
}

/// <summary>
/// Service 调用统计快照
/// </summary>
public sealed class ServiceCallMetricsSnapshot
{
    /// <summary>本地调用总数</summary>
    public long TotalLocalCalls { get; init; }

    /// <summary>远程调用总数</summary>
    public long TotalRemoteCalls { get; init; }

    /// <summary>失败调用总数</summary>
    public long TotalFailedCalls { get; init; }

    /// <summary>平均本地调用延迟（微秒）</summary>
    public double AverageLocalCallMicroseconds { get; init; }

    /// <summary>最小本地调用延迟（微秒）</summary>
    public long MinLocalCallMicroseconds { get; init; }

    /// <summary>最大本地调用延迟（微秒）</summary>
    public long MaxLocalCallMicroseconds { get; init; }

    /// <summary>平均远程调用延迟（微秒）</summary>
    public double AverageRemoteCallMicroseconds { get; init; }

    /// <summary>最小远程调用延迟（微秒）</summary>
    public long MinRemoteCallMicroseconds { get; init; }

    /// <summary>最大远程调用延迟（微秒）</summary>
    public long MaxRemoteCallMicroseconds { get; init; }

    /// <summary>总调用数</summary>
    public long TotalCalls => TotalLocalCalls + TotalRemoteCalls;

    /// <summary>成功率</summary>
    public double SuccessRate => TotalCalls > 0
        ? (double)(TotalCalls - TotalFailedCalls) / TotalCalls * 100
        : 100;

    /// <summary>本地调用比例</summary>
    public double LocalCallPercentage => TotalCalls > 0
        ? (double)TotalLocalCalls / TotalCalls * 100
        : 0;

    public override string ToString()
    {
        return $"ServiceCallMetrics[Total={TotalCalls}, Local={TotalLocalCalls}, Remote={TotalRemoteCalls}, " +
               $"Failed={TotalFailedCalls}, SuccessRate={SuccessRate:F1}%, " +
               $"AvgLocal={AverageLocalCallMicroseconds:F1}μs, AvgRemote={AverageRemoteCallMicroseconds / 1000:F2}ms]";
    }
}
