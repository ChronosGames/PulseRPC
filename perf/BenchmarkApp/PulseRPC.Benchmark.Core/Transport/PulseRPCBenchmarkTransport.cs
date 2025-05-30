using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Core.Abstract;
using PulseRPC.Benchmark.Core.Models;
using PulseRPC.Client;
using System.Collections.Concurrent;

namespace PulseRPC.Benchmark.Core.Transport;

/// <summary>
/// PulseRPC基准测试传输层实现
/// 提供基于PulseRPC客户端的传输功能
/// </summary>
public class PulseRpcBenchmarkTransport : BaseBenchmarkTransport
{
    private readonly ILogger<PulseRpcBenchmarkTransport> _logger;
    private readonly ConcurrentDictionary<Type, object> _serviceProxies = new();
    private readonly ConcurrentQueue<byte[]> _receivedMessages = new();
    private CancellationTokenSource? _connectionCts;

    public PulseRpcBenchmarkTransport(ILogger<PulseRpcBenchmarkTransport> logger)
        : base("PulseRPC", logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.LogInformation("正在连接到 {Host}:{Port}", host, port);

            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // 模拟连接过程
            await Task.Delay(100, cancellationToken);

            IsConnected = true;
            RecordConnectTime();

            _logger.LogInformation("成功连接到 {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接失败: {Host}:{Port}", host, port);
            RecordError(ex);
            await DisconnectAsync(CancellationToken.None);
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("正在断开连接");

            // 模拟断开连接过程
            await Task.Delay(50, cancellationToken);

            IsConnected = false;
            _serviceProxies.Clear();

            _logger.LogInformation("连接已断开");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "断开连接时发生错误");
        }

        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = null;
    }

    public override async Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new InvalidOperationException("传输层未连接");
        }

        try
        {
            // 模拟发送数据
            await Task.Delay(1, cancellationToken);
            UpdateSendStatistics(data.Length);
            return true;
        }
        catch (Exception ex)
        {
            RecordError(ex);
            return false;
        }
    }

    public override async Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new InvalidOperationException("传输层未连接");
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            // 尝试从队列中获取已接收的消息
            if (_receivedMessages.TryDequeue(out var message))
            {
                UpdateReceiveStatistics(message.Length);
                return message;
            }

            // 如果队列为空，等待一段时间或直到取消
            await Task.Delay(10, timeoutCts.Token);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null; // 超时
        }
        catch (Exception ex)
        {
            RecordError(ex);
            return null;
        }
    }

    /// <summary>
    /// 注册服务代理
    /// </summary>
    public async Task<T> RegisterServiceAsync<T>() where T : class
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new InvalidOperationException("传输层未连接");
        }

        try
        {
            var serviceType = typeof(T);

            if (_serviceProxies.TryGetValue(serviceType, out var existingProxy))
            {
                return (T)existingProxy;
            }

            // 对于基准测试，我们直接返回服务类型，不需要实际的代理对象
            _serviceProxies.TryAdd(serviceType, serviceType);

            _logger.LogDebug("已注册服务代理: {ServiceType}", serviceType.Name);

            // 返回一个占位符对象，实际调用会通过InvokeAsync进行
            return default(T)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册服务代理失败: {ServiceType}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// 调用RPC方法
    /// </summary>
    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(string methodName, TRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            throw new InvalidOperationException("传输层未连接");
        }

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 模拟RPC调用
            await Task.Delay(10, cancellationToken);

            stopwatch.Stop();
            AddLatencyMeasurement(stopwatch.Elapsed.TotalMilliseconds);

            // 返回默认响应
            return default(TResponse)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC调用失败: {MethodName}", methodName);
            RecordError(ex);
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisconnectAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
        }

        base.Dispose(disposing);
    }
}
