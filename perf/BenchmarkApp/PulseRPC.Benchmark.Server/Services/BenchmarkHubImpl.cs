using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Shared;
using PulseRPC.Benchmark.Shared.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace PulseRPC.Benchmark.Server.Services;

/// <summary>
/// 基准测试服务实现类
/// 提供所有基准测试所需的RPC方法实现
/// </summary>
public class BenchmarkHubImpl : IBenchmarkHub
{
    private readonly ILogger<BenchmarkHubImpl> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _activeOperations = new();

    // 服务统计
    private long _totalRequests = 0;
    private long _successfulRequests = 0;
    private long _failedRequests = 0;
    private DateTime _serviceStartTime = DateTime.UtcNow;

    public BenchmarkHubImpl(ILogger<BenchmarkHubImpl> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("BenchmarkService 已初始化，启动时间: {StartTime}", _serviceStartTime);
    }

    public async Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var operationId = request.RequestId.ToString();

        try
        {
            Interlocked.Increment(ref _totalRequests);
            _activeOperations.TryAdd(operationId, startTime);

            _logger.LogDebug("处理Echo请求，请求ID: {RequestId}, 消息长度: {MessageLength}",
                request.RequestId, request.Message?.Length ?? 0);

            // 模拟期望的延迟
            if (request.ExpectedDelayMs > 0)
            {
                await Task.Delay(request.ExpectedDelayMs, cancellationToken);
            }

            // 验证请求完整性
            if (string.IsNullOrEmpty(request.Message))
            {
                throw new ArgumentException("Echo消息不能为空");
            }

            var processingTimeNs = (DateTime.UtcNow - startTime).Ticks * 100; // 转换为纳秒
            var response = new EchoResponse
            {
                RequestId = request.RequestId,
                EchoMessage = request.Message, // 回显原消息
                ActualDelayMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ProcessingTimeNs = processingTimeNs,
                Success = true
            };

            Interlocked.Increment(ref _successfulRequests);
            _logger.LogDebug("Echo请求处理完成，请求ID: {RequestId}, 处理时间: {ProcessingTimeMs}ms",
                request.RequestId, response.ActualDelayMs);

            return response;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);
            _logger.LogError(ex, "Echo请求处理失败，请求ID: {RequestId}", request.RequestId);

            return new EchoResponse
            {
                RequestId = request.RequestId,
                EchoMessage = string.Empty,
                ActualDelayMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                ProcessingTimeNs = (DateTime.UtcNow - startTime).Ticks * 100,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _activeOperations.TryRemove(operationId, out _);
        }
    }

    public Task<PingResponse> PingAsync(PingRequest request)
    {
        // 极简优化版本 - 移除所有不必要的开销
        try
        {
            // 快速统计计数，不使用字符串操作和字典
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _successfulRequests);

            // 直接返回响应，移除时间计算和日志
            var response = new PingResponse
            {
                RequestId = request.RequestId,
                SequenceNumber = request.SequenceNumber,
                ProcessingTimeNs = 0, // 服务端处理时间近似为0
                RoundTripTimeNs = 0,
                Success = true
            };

            return Task.FromResult(response);
        }
        catch
        {
            // 快速错误处理，不记录详细信息
            Interlocked.Increment(ref _failedRequests);

            return Task.FromResult(new PingResponse
            {
                RequestId = request.RequestId,
                SequenceNumber = request.SequenceNumber,
                ProcessingTimeNs = 0,
                RoundTripTimeNs = 0,
                Success = false,
                ErrorMessage = "Server Error"
            });
        }
    }

    public Task<ThroughputTestResponse> ThroughputTestAsync(ThroughputTestRequest request, CancellationToken cancellationToken = default)
    {
        // 极简优化版本 - 移除所有不必要的开销
        try
        {
            // 快速统计计数
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _successfulRequests);

            // 直接返回响应，移除所有开销：
            // - 不再使用 DateTime.UtcNow (耗时)
            // - 不再使用 operationId.ToString() (分配)
            // - 不再使用 _activeOperations 字典 (锁竞争)
            // - 不再调用 SimulateBatchProcessing (延迟)
            // - 不再记录日志 (I/O)
            var response = new ThroughputTestResponse
            {
                RequestId = request.RequestId,
                BatchNumber = request.BatchNumber,
                ProcessedMessages = request.BatchSize,
                ThroughputMps = 0, // 客户端计算
                BandwidthBps = 0,  // 客户端计算
                ProcessingTimeNs = 0, // 服务端处理时间近似为0
                Success = true
            };

            return Task.FromResult(response);
        }
        catch
        {
            // 快速错误处理
            Interlocked.Increment(ref _failedRequests);

            return Task.FromResult(new ThroughputTestResponse
            {
                RequestId = request.RequestId,
                BatchNumber = request.BatchNumber,
                ProcessedMessages = 0,
                ThroughputMps = 0,
                BandwidthBps = 0,
                ProcessingTimeNs = 0,
                Success = false,
                ErrorMessage = "Server Error"
            });
        }
    }

    public async Task<StreamTestResponse> StreamTestAsync(StreamTestRequest request, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var operationId = request.RequestId.ToString();

        try
        {
            Interlocked.Increment(ref _totalRequests);
            _activeOperations.TryAdd(operationId, startTime);

            _logger.LogDebug("处理StreamTest请求，请求ID: {RequestId}, 流ID: {StreamId}, 数据块索引: {ChunkIndex}",
                request.RequestId, request.StreamId, request.ChunkIndex);

            // 模拟流处理延迟
            await Task.Delay(Random.Shared.Next(1, 5), cancellationToken);

            // 验证流请求
            if (string.IsNullOrEmpty(request.StreamId))
            {
                throw new ArgumentException("流ID不能为空");
            }

            var processingTimeNs = (DateTime.UtcNow - startTime).Ticks * 100;

            // 生成响应数据
            var responseData = Encoding.UTF8.GetBytes($"StreamResponse_{request.ChunkIndex}_{DateTime.UtcNow:HHmmss}");

            var response = new StreamTestResponse
            {
                RequestId = request.RequestId,
                StreamId = request.StreamId,
                ReceivedChunkIndex = request.ChunkIndex,
                TotalBytesReceived = responseData.Length,
                StreamCompleted = request.IsLastChunk, // 使用请求中的IsLastChunk
                ProcessingTimeNs = processingTimeNs,
                Success = true,
                Payload = responseData
            };

            Interlocked.Increment(ref _successfulRequests);
            _logger.LogDebug("StreamTest请求处理完成，请求ID: {RequestId}, 数据块索引: {ChunkIndex}",
                request.RequestId, request.ChunkIndex);

            return response;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);
            _logger.LogError(ex, "StreamTest请求处理失败，请求ID: {RequestId}", request.RequestId);

            return new StreamTestResponse
            {
                RequestId = request.RequestId,
                StreamId = request.StreamId ?? "unknown",
                ReceivedChunkIndex = 0,
                TotalBytesReceived = 0,
                StreamCompleted = true,
                ProcessingTimeNs = (DateTime.UtcNow - startTime).Ticks * 100,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _activeOperations.TryRemove(operationId, out _);
        }
    }

    public async Task<ServerInfoResponse> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("处理GetServerInfo请求");

            var uptime = DateTime.UtcNow - _serviceStartTime;
            var response = new ServerInfoResponse
            {
                RequestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ServerName = Environment.MachineName,
                Version = "1.0.0",
                StartTime = _serviceStartTime,
                ActiveConnections = _activeOperations.Count,
                System = new SystemInfo
                {
                    CpuCores = Environment.ProcessorCount,
                    TotalMemory = GC.GetTotalMemory(false),
                    AvailableMemory = GC.GetTotalMemory(false),
                    OperatingSystem = Environment.OSVersion.ToString(),
                    RuntimeVersion = Environment.Version.ToString()
                },
                ProcessingTimeNs = 0,
                Success = true
            };

            _logger.LogDebug("GetServerInfo请求处理完成");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetServerInfo请求处理失败");

            return new ServerInfoResponse
            {
                RequestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ServerName = Environment.MachineName,
                Version = "1.0.0",
                StartTime = _serviceStartTime,
                ProcessingTimeNs = 0,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("处理HealthCheck请求");

            // 简单的健康检查
            await Task.CompletedTask;

            var errorRate = _totalRequests > 0 ? (double)_failedRequests / _totalRequests : 0;
            var healthStatus = errorRate > 0.1 ? "Degraded" : "Healthy";

            _logger.LogDebug("HealthCheck请求处理完成，状态: {Status}, 错误率: {ErrorRate:P2}",
                healthStatus, errorRate);

            return healthStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HealthCheck请求处理失败");
            return "Unhealthy";
        }
    }

    #region Quick Benchmark Methods

    // Notify 统计计数器
    private long _notifyCount;

    public ValueTask NotifyAsync(NotifyRequest request)
    {
        // 极简实现 - 仅计数，无其他开销
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _successfulRequests);
        Interlocked.Increment(ref _notifyCount);

        return ValueTask.CompletedTask;
    }

    public Task<UploadResponse> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
        // 极简实现 - 仅确认接收，不做复杂处理
        try
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _successfulRequests);

            var response = new UploadResponse
            {
                RequestId = request.RequestId,
                ReceivedBytes = request.Payload?.Length ?? 0,
                ProcessingTimeNs = 0,
                Success = true
            };

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);

            return Task.FromResult(new UploadResponse
            {
                RequestId = request.RequestId,
                ReceivedBytes = 0,
                ProcessingTimeNs = 0,
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    // 预分配的下行响应缓存（避免频繁内存分配）
    private static readonly byte[] _downloadPayload64B = new byte[64];
    private static readonly byte[] _downloadPayload1KB = new byte[1024];
    private static readonly byte[] _downloadPayload64KB = new byte[65536];
    private static readonly byte[] _downloadPayload256KB = new byte[262144];

    static BenchmarkHubImpl()
    {
        // 初始化下行缓存数据
        Random.Shared.NextBytes(_downloadPayload64B);
        Random.Shared.NextBytes(_downloadPayload1KB);
        Random.Shared.NextBytes(_downloadPayload64KB);
        Random.Shared.NextBytes(_downloadPayload256KB);
    }

    public Task<DownloadResponse> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        // 高性能实现 - 使用预分配缓存
        try
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _successfulRequests);

            // 根据请求大小选择预分配缓存
            byte[] payload = request.RequestedPayloadSize switch
            {
                <= 64 => _downloadPayload64B[..request.RequestedPayloadSize],
                <= 1024 => _downloadPayload1KB[..request.RequestedPayloadSize],
                <= 65536 => _downloadPayload64KB[..request.RequestedPayloadSize],
                <= 262144 => _downloadPayload256KB[..request.RequestedPayloadSize],
                _ => CreateLargePayload(request.RequestedPayloadSize)
            };

            var response = new DownloadResponse
            {
                RequestId = request.RequestId,
                PayloadSize = payload.Length,
                Payload = payload,
                ProcessingTimeNs = 0,
                Success = true
            };

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);

            return Task.FromResult(new DownloadResponse
            {
                RequestId = request.RequestId,
                PayloadSize = 0,
                Payload = Array.Empty<byte>(),
                ProcessingTimeNs = 0,
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private static byte[] CreateLargePayload(int size)
    {
        var payload = new byte[size];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    #endregion

    /// <summary>
    /// 模拟批量消息处理
    /// </summary>
    private async Task SimulateBatchProcessing(int messageCount, int messageSize, CancellationToken cancellationToken)
    {
        // 根据消息数量和大小模拟处理时间
        var totalSize = (long)messageCount * messageSize;
        var processingTime = totalSize switch
        {
            < 10_000 => 0, // 小批量，无延迟
            < 100_000 => 1, // 中等批量，1ms延迟
            < 1_000_000 => 5, // 大批量，5ms延迟
            _ => 10 // 超大批量，10ms延迟
        };

        if (processingTime > 0)
        {
            await Task.Delay(processingTime, cancellationToken);
        }
    }

    /// <summary>
    /// 获取服务统计信息（用于监控）
    /// </summary>
    public ServiceStatistics GetStatistics()
    {
        var uptime = DateTime.UtcNow - _serviceStartTime;

        return new ServiceStatistics
        {
            ServiceStartTime = _serviceStartTime,
            TotalRequests = _totalRequests,
            SuccessfulRequests = _successfulRequests,
            FailedRequests = _failedRequests,
            ActiveOperations = _activeOperations.Count,
            UptimeSeconds = (long)uptime.TotalSeconds,
            RequestsPerSecond = _totalRequests / Math.Max(uptime.TotalSeconds, 1)
        };
    }
}

/// <summary>
/// 服务统计信息
/// </summary>
public class ServiceStatistics
{
    public DateTime ServiceStartTime { get; set; }
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public int ActiveOperations { get; set; }
    public long UptimeSeconds { get; set; }
    public double RequestsPerSecond { get; set; }
}
