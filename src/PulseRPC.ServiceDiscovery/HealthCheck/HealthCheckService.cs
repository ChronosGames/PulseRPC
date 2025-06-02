using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PulseRPC.ServiceDiscovery.HealthCheck;

/// <summary>
/// 健康检查配置选项
/// </summary>
public class HealthCheckOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "HealthCheck";

    /// <summary>
    /// 默认健康检查超时时间
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// TCP连接检查超时时间
    /// </summary>
    public TimeSpan TcpCheckTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// HTTP健康检查超时时间
    /// </summary>
    public TimeSpan HttpCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ping检查超时时间
    /// </summary>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 健康检查重试次数
    /// </summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>
    /// 健康检查重试延迟
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 是否启用并发健康检查
    /// </summary>
    public bool EnableConcurrentChecks { get; set; } = true;

    /// <summary>
    /// 最大并发健康检查数量
    /// </summary>
    public int MaxConcurrentChecks { get; set; } = 50;
}

/// <summary>
/// 健康检查类型
/// </summary>
public enum HealthCheckType
{
    /// <summary>
    /// TCP连接检查
    /// </summary>
    TcpConnection,

    /// <summary>
    /// HTTP健康检查
    /// </summary>
    Http,

    /// <summary>
    /// Ping检查
    /// </summary>
    Ping,

    /// <summary>
    /// 自定义检查
    /// </summary>
    Custom
}

/// <summary>
/// 健康检查结果详情
/// </summary>
public class HealthCheckDetails
{
    /// <summary>
    /// 检查类型
    /// </summary>
    public HealthCheckType CheckType { get; set; }

    /// <summary>
    /// 检查时间
    /// </summary>
    public DateTime CheckTime { get; set; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan ResponseTime { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 额外的检查数据
    /// </summary>
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// 健康检查服务 - 实现多种健康检查机制
/// </summary>
public class HealthCheckService : IHealthChecker, IDisposable
{
    private readonly HealthCheckOptions _options;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _concurrencyLimiter;

    // 健康检查结果缓存
    private readonly ConcurrentDictionary<string, CachedHealthResult> _healthCache = new();

    // 性能统计
    private readonly ConcurrentDictionary<string, HealthCheckStatistics> _statistics = new();

    private bool _disposed;

    public HealthCheckService(
        IOptions<HealthCheckOptions> options,
        ILogger<HealthCheckService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 配置HttpClient
        _httpClient = new HttpClient();
        _httpClient.Timeout = _options.HttpCheckTimeout;
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PulseRPC-HealthChecker/1.0");

        // 配置并发限制
        _concurrencyLimiter = new SemaphoreSlim(_options.MaxConcurrentChecks, _options.MaxConcurrentChecks);

        _logger.LogInformation("HealthCheckService 已初始化，最大并发: {MaxConcurrent}", _options.MaxConcurrentChecks);
    }

    /// <summary>
    /// 开始监控服务健康状态
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="interval">检查间隔</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果流</returns>
    public async IAsyncEnumerable<HealthCheckResult> MonitorHealthAsync(
        ServiceEndpoint endpoint,
        TimeSpan interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        if (interval <= TimeSpan.Zero)
            throw new ArgumentException("检查间隔必须大于零", nameof(interval));

        if (_disposed)
            throw new ObjectDisposedException(nameof(HealthCheckService));

        _logger.LogInformation("开始监控服务健康状态: {ServiceId} @ {EndPoint}, 间隔: {Interval}",
            endpoint.ServiceId, endpoint.EndPoint, interval);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HealthCheckResult? result;

                try
                {
                    // 执行健康检查
                    result = await CheckHealthAsync(endpoint, cancellationToken);

                    _logger.LogDebug("健康检查完成: {ServiceId}, 状态: {Status}, 响应时间: {ResponseTime}ms",
                        endpoint.ServiceId, result.Status, result.ResponseTimeMs);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("健康监控已取消: {ServiceId}", endpoint.ServiceId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "健康检查失败: {ServiceId}", endpoint.ServiceId);

                    // 即使健康检查失败，也要返回失败结果
                    result = new HealthCheckResult
                    {
                        ServiceId = endpoint.ServiceId,
                        Status = HealthStatus.Unhealthy,
                        CheckTime = DateTime.UtcNow,
                        ResponseTime = TimeSpan.Zero,
                        ErrorMessage = ex.Message,
                        Attempts = 1,
                        Data = { ["exception"] = ex.GetType().Name }
                    };
                }

                yield return result;

                try
                {
                    // 等待下一次检查间隔
                    await Task.Delay(interval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("健康监控延迟已取消: {ServiceId}", endpoint.ServiceId);
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation("停止监控服务健康状态: {ServiceId}", endpoint.ServiceId);
        }
    }

    /// <summary>
    /// 检查服务端点健康状态
    /// </summary>
    /// <param name="endpoint">服务端点</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var checkKey = $"{endpoint.ServiceId}-{endpoint.EndPoint}";

        // 检查缓存
        if (_healthCache.TryGetValue(checkKey, out var cachedResult) &&
            DateTime.UtcNow - cachedResult.CheckTime < TimeSpan.FromSeconds(10))
        {
            _logger.LogDebug("使用缓存的健康检查结果: {ServiceId}", endpoint.ServiceId);
            return cachedResult.Result;
        }

        if (!_options.EnableConcurrentChecks)
        {
            return await PerformHealthCheckAsync(endpoint, checkKey, cancellationToken);
        }

        // 使用并发限制
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            return await PerformHealthCheckAsync(endpoint, checkKey, cancellationToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// 批量检查多个端点的健康状态
    /// </summary>
    /// <param name="endpoints">服务端点列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>健康检查结果列表</returns>
    public async Task<Dictionary<string, HealthCheckResult>> CheckHealthBatchAsync(
        IEnumerable<ServiceEndpoint> endpoints,
        CancellationToken cancellationToken = default)
    {
        var endpointList = endpoints.ToList();
        _logger.LogDebug("开始批量健康检查，端点数量: {Count}", endpointList.Count);

        var checkTasks = endpointList.Select(endpoint => CheckHealthAsync(endpoint, cancellationToken));
        var results = await Task.WhenAll(checkTasks);

        _logger.LogDebug("批量健康检查完成，健康端点: {HealthyCount}/{TotalCount}",
            results.Count(r => r.Status == HealthStatus.Healthy), results.Length);

        Dictionary<string, HealthCheckResult> dict = new();
        foreach (var result in results)
        {
            dict[result.ServiceId] = result;
        }
        return dict;
    }

    /// <summary>
    /// TCP连接健康检查
    /// </summary>
    /// <param name="endpoint">端点地址</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检查详情</returns>
    public async Task<HealthCheckDetails> CheckTcpConnectionAsync(
        IPEndPoint endpoint,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var checkTimeout = timeout ?? _options.TcpCheckTimeout;
        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(endpoint.Address, endpoint.Port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(checkTimeout, cancellationToken));
            stopwatch.Stop();
            if (completedTask == connectTask && client.Connected)
            {
                return new HealthCheckDetails
                {
                    CheckType = HealthCheckType.TcpConnection,
                    CheckTime = startTime,
                    ResponseTime = stopwatch.Elapsed,
                    IsSuccess = true,
                    Data = { ["endpoint"] = endpoint.ToString() }
                };
            }
            else
            {
                return new HealthCheckDetails
                {
                    CheckType = HealthCheckType.TcpConnection,
                    CheckTime = startTime,
                    ResponseTime = stopwatch.Elapsed,
                    IsSuccess = false,
                    ErrorMessage = completedTask == connectTask ? "连接被拒绝" : "连接超时",
                    Data = { ["endpoint"] = endpoint.ToString() }
                };
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new HealthCheckDetails
            {
                CheckType = HealthCheckType.TcpConnection,
                CheckTime = startTime,
                ResponseTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = { ["endpoint"] = endpoint.ToString() }
            };
        }
    }

    /// <summary>
    /// HTTP健康检查
    /// </summary>
    /// <param name="healthCheckUrl">健康检查URL</param>
    /// <param name="expectedStatusCode">期望的HTTP状态码</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检查详情</returns>
    public async Task<HealthCheckDetails> CheckHttpHealthAsync(
        string healthCheckUrl,
        HttpStatusCode expectedStatusCode = HttpStatusCode.OK,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var checkTimeout = timeout ?? _options.HttpCheckTimeout;
        var startTime = DateTime.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(checkTimeout);

            var response = await _httpClient.GetAsync(healthCheckUrl, cts.Token);
            stopwatch.Stop();

            var isSuccess = response.StatusCode == expectedStatusCode;
            return new HealthCheckDetails
            {
                CheckType = HealthCheckType.Http,
                CheckTime = startTime,
                ResponseTime = stopwatch.Elapsed,
                IsSuccess = isSuccess,
                ErrorMessage = isSuccess ? null : $"期望状态码 {expectedStatusCode}，实际 {response.StatusCode}",
                Data =
                {
                    ["url"] = healthCheckUrl,
                    ["statusCode"] = (int)response.StatusCode,
                    ["expectedStatusCode"] = (int)expectedStatusCode,
                    ["contentLength"] = response.Content.Headers.ContentLength ?? 0
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new HealthCheckDetails
            {
                CheckType = HealthCheckType.Http,
                CheckTime = startTime,
                ResponseTime = stopwatch.Elapsed,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = { ["url"] = healthCheckUrl }
            };
        }
    }

    /// <summary>
    /// Ping健康检查
    /// </summary>
    /// <param name="address">目标地址</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检查详情</returns>
    public async Task<HealthCheckDetails> CheckPingAsync(
        IPAddress address,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var checkTimeout = timeout ?? _options.PingTimeout;
        var startTime = DateTime.UtcNow;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, (int)checkTimeout.TotalMilliseconds);

            return new HealthCheckDetails
            {
                CheckType = HealthCheckType.Ping,
                CheckTime = startTime,
                ResponseTime = TimeSpan.FromMilliseconds(reply.RoundtripTime),
                IsSuccess = reply.Status == IPStatus.Success,
                ErrorMessage = reply.Status != IPStatus.Success ? reply.Status.ToString() : null,
                Data =
                {
                    ["address"] = address.ToString(),
                    ["status"] = reply.Status.ToString(),
                    ["roundtripTime"] = reply.RoundtripTime
                }
            };
        }
        catch (Exception ex)
        {
            return new HealthCheckDetails
            {
                CheckType = HealthCheckType.Ping,
                CheckTime = startTime,
                ResponseTime = TimeSpan.Zero,
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Data = { ["address"] = address.ToString() }
            };
        }
    }

    /// <summary>
    /// 获取健康检查统计信息
    /// </summary>
    public Dictionary<string, object> GetStatistics()
    {
        var stats = new Dictionary<string, object>
        {
            ["TotalChecks"] = _statistics.Values.Sum(s => s.TotalChecks),
            ["SuccessfulChecks"] = _statistics.Values.Sum(s => s.SuccessfulChecks),
            ["FailedChecks"] = _statistics.Values.Sum(s => s.FailedChecks),
            ["AverageResponseTime"] = _statistics.Values
                .Where(s => s.TotalChecks > 0)
                .Select(s => s.TotalResponseTime.TotalMilliseconds / s.TotalChecks)
                .DefaultIfEmpty(0)
                .Average(),
            ["CacheHits"] = _healthCache.Count,
            ["ServiceStatistics"] = _statistics.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    TotalChecks = kvp.Value.TotalChecks,
                    SuccessfulChecks = kvp.Value.SuccessfulChecks,
                    FailedChecks = kvp.Value.FailedChecks,
                    SuccessRate = kvp.Value.TotalChecks > 0
                        ? (double)kvp.Value.SuccessfulChecks / kvp.Value.TotalChecks * 100
                        : 0,
                    AverageResponseTime = kvp.Value.TotalChecks > 0
                        ? kvp.Value.TotalResponseTime.TotalMilliseconds / kvp.Value.TotalChecks
                        : 0,
                    LastCheckTime = kvp.Value.LastCheckTime
                })
        };

        return stats;
    }

    #region Private Methods

    /// <summary>
    /// 执行健康检查
    /// </summary>
    private async Task<HealthCheckResult> PerformHealthCheckAsync(
        ServiceEndpoint endpoint,
        string checkKey,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var attempts = 0;
        HealthCheckDetails? lastCheckDetails = null;

        while (attempts <= _options.RetryCount)
        {
            attempts++;

            try
            {
                // 执行TCP连接检查
                lastCheckDetails = await CheckTcpConnectionAsync(endpoint.EndPoint,
                    _options.TcpCheckTimeout, cancellationToken);

                var result = new HealthCheckResult
                {
                    ServiceId = endpoint.ServiceId,
                    Status = lastCheckDetails.IsSuccess ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                    CheckTime = lastCheckDetails.CheckTime,
                    ResponseTime = lastCheckDetails.ResponseTime,
                    ErrorMessage = lastCheckDetails.ErrorMessage,
                    Attempts = attempts
                };

                // 更新缓存
                _healthCache[checkKey] = new CachedHealthResult
                {
                    Result = result,
                    CheckTime = startTime
                };

                // 更新统计信息
                UpdateStatistics(endpoint.ServiceId, result);

                if (lastCheckDetails.IsSuccess || attempts > _options.RetryCount)
                {
                    return result;
                }

                // 重试延迟
                if (attempts <= _options.RetryCount)
                {
                    await Task.Delay(_options.RetryDelay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "健康检查失败: {ServiceId}, 尝试 {Attempt}/{MaxAttempts}",
                    endpoint.ServiceId, attempts, _options.RetryCount + 1);

                if (attempts > _options.RetryCount)
                {
                    var failedResult = new HealthCheckResult
                    {
                        ServiceId = endpoint.ServiceId,
                        Status = HealthStatus.Unhealthy,
                        CheckTime = startTime,
                        ResponseTime = DateTime.UtcNow - startTime,
                        ErrorMessage = ex.Message,
                        Attempts = attempts
                    };

                    UpdateStatistics(endpoint.ServiceId, failedResult);
                    return failedResult;
                }

                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
        }

        // 所有重试都失败了
        var errorResult = new HealthCheckResult
        {
            ServiceId = endpoint.ServiceId,
            Status = HealthStatus.Unhealthy,
            CheckTime = startTime,
            ResponseTime = DateTime.UtcNow - startTime,
            ErrorMessage = lastCheckDetails?.ErrorMessage ?? "健康检查失败",
            Attempts = attempts
        };

        UpdateStatistics(endpoint.ServiceId, errorResult);
        return errorResult;
    }

    /// <summary>
    /// 更新统计信息
    /// </summary>
    private void UpdateStatistics(string serviceId, HealthCheckResult result)
    {
        var stats = _statistics.GetOrAdd(serviceId, _ => new HealthCheckStatistics());

        stats.TotalChecks++;
        stats.TotalResponseTime += result.ResponseTime;
        stats.LastCheckTime = result.CheckTime;

        if (result.Status == HealthStatus.Healthy)
        {
            stats.SuccessfulChecks++;
        }
        else
        {
            stats.FailedChecks++;
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _httpClient?.Dispose();
        _concurrencyLimiter?.Dispose();

        _logger.LogInformation("HealthCheckService 已释放");
    }

    #endregion

    /// <summary>
    /// 缓存的健康检查结果
    /// </summary>
    private class CachedHealthResult
    {
        public required HealthCheckResult Result { get; init; }
        public DateTime CheckTime { get; init; }
    }

    /// <summary>
    /// 健康检查统计信息
    /// </summary>
    private class HealthCheckStatistics
    {
        public long TotalChecks { get; set; }
        public long SuccessfulChecks { get; set; }
        public long FailedChecks { get; set; }
        public TimeSpan TotalResponseTime { get; set; }
        public DateTime LastCheckTime { get; set; }
    }
}
