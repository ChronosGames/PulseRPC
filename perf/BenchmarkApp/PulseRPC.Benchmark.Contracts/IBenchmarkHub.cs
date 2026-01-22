using PulseRPC;

namespace PulseRPC.Benchmark.Contracts;

/// <summary>
/// 基准测试服务接口（简化版）
/// 仅保留核心性能测试方法
/// </summary>
[Channel("TcpChannel")]
public interface IBenchmarkHub : IPulseHub
{
    /// <summary>
    /// Echo测试 - 单次延迟测试
    /// </summary>
    Task<EchoResponse> EchoAsync(EchoRequest request);

    /// <summary>
    /// 上行测试 - 客户端发送大数据到服务端
    /// </summary>
    Task<UploadResponse> UploadAsync(UploadRequest request);

    /// <summary>
    /// 下行测试 - 服务端返回大数据到客户端
    /// </summary>
    Task<DownloadResponse> DownloadAsync(DownloadRequest request);

    /// <summary>
    /// 健康检查
    /// </summary>
    Task<string> HealthCheckAsync(HealthCheckRequest request);
}
