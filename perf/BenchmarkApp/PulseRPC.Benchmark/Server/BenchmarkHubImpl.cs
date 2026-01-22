using Microsoft.Extensions.Logging;
using PulseRPC.Benchmark.Contracts;

namespace PulseRPC.Benchmark.Server;

/// <summary>
/// 基准测试服务实现
/// </summary>
public class BenchmarkHubImpl : IBenchmarkHub
{
    private readonly ILogger<BenchmarkHubImpl> _logger;
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;

    // 预分配的下行响应缓存
    private static readonly byte[] DownloadPayload64B = new byte[64];
    private static readonly byte[] DownloadPayload1KB = new byte[1024];
    private static readonly byte[] DownloadPayload64KB = new byte[65536];
    private static readonly byte[] DownloadPayload256KB = new byte[262144];

    static BenchmarkHubImpl()
    {
        Random.Shared.NextBytes(DownloadPayload64B);
        Random.Shared.NextBytes(DownloadPayload1KB);
        Random.Shared.NextBytes(DownloadPayload64KB);
        Random.Shared.NextBytes(DownloadPayload256KB);
    }

    public BenchmarkHubImpl(ILogger<BenchmarkHubImpl> logger)
    {
        _logger = logger;
        _logger.LogInformation("BenchmarkHubImpl 已初始化");
    }

    public Task<EchoResponse> EchoAsync(EchoRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _successfulRequests);

            var response = new EchoResponse
            {
                RequestId = request.RequestId,
                EchoMessage = request.Message,
                ProcessingTimeNs = 0,
                Success = true
            };

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRequests);

            return Task.FromResult(new EchoResponse
            {
                RequestId = request.RequestId,
                EchoMessage = string.Empty,
                ProcessingTimeNs = 0,
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    public Task<UploadResponse> UploadAsync(UploadRequest request, CancellationToken cancellationToken = default)
    {
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

    public Task<DownloadResponse> DownloadAsync(DownloadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            Interlocked.Increment(ref _totalRequests);
            Interlocked.Increment(ref _successfulRequests);

            // 使用预分配缓存
            byte[] payload = request.RequestedPayloadSize switch
            {
                <= 64 => DownloadPayload64B[..request.RequestedPayloadSize],
                <= 1024 => DownloadPayload1KB[..request.RequestedPayloadSize],
                <= 65536 => DownloadPayload64KB[..request.RequestedPayloadSize],
                <= 262144 => DownloadPayload256KB[..request.RequestedPayloadSize],
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

    public Task<string> HealthCheckAsync(HealthCheckRequest request, CancellationToken cancellationToken = default)
    {
        var errorRate = _totalRequests > 0 ? (double)_failedRequests / _totalRequests : 0;
        var status = errorRate > 0.1 ? "Degraded" : "Healthy";
        return Task.FromResult(status);
    }

    private static byte[] CreateLargePayload(int size)
    {
        var payload = new byte[size];
        Random.Shared.NextBytes(payload);
        return payload;
    }
}
