using MemoryPack;

namespace PulseRPC.Benchmark.Contracts;

#region Echo Messages

/// <summary>
/// Echo测试请求
/// </summary>
[MemoryPackable]
public partial class EchoRequest
{
    /// <summary>
    /// 请求唯一标识符
    /// </summary>
    public long RequestId { get; set; }

    /// <summary>
    /// 客户端发送时间戳（纳秒）
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// Echo消息内容
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 负载数据
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public static EchoRequest Create(string message, int payloadSize = 0)
    {
        var request = new EchoRequest
        {
            RequestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
            Message = message
        };

        if (payloadSize > 0)
        {
            request.Payload = new byte[payloadSize];
            Random.Shared.NextBytes(request.Payload);
        }

        return request;
    }
}

/// <summary>
/// Echo测试响应
/// </summary>
[MemoryPackable]
public partial class EchoResponse
{
    /// <summary>
    /// 对应的请求ID
    /// </summary>
    public long RequestId { get; set; }

    /// <summary>
    /// 回显的消息内容
    /// </summary>
    public string EchoMessage { get; set; } = string.Empty;

    /// <summary>
    /// 服务端处理耗时（纳秒）
    /// </summary>
    public long ProcessingTimeNs { get; set; }

    /// <summary>
    /// 处理是否成功
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

#endregion

#region Upload Messages

/// <summary>
/// 上行测试请求
/// </summary>
[MemoryPackable]
public partial class UploadRequest
{
    /// <summary>
    /// 请求唯一标识符
    /// </summary>
    public long RequestId { get; set; }

    /// <summary>
    /// 序列号
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// 客户端发送时间戳（纳秒）
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 负载数据
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public static UploadRequest Create(int sequenceNumber, int payloadSize)
    {
        var payload = new byte[payloadSize];
        Random.Shared.NextBytes(payload);

        return new UploadRequest
        {
            RequestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
            Payload = payload
        };
    }
}

/// <summary>
/// 上行测试响应
/// </summary>
[MemoryPackable]
public partial class UploadResponse
{
    /// <summary>
    /// 对应的请求ID
    /// </summary>
    public long RequestId { get; set; }

    /// <summary>
    /// 接收到的字节数
    /// </summary>
    public int ReceivedBytes { get; set; }

    /// <summary>
    /// 服务端处理耗时（纳秒）
    /// </summary>
    public long ProcessingTimeNs { get; set; }

    /// <summary>
    /// 处理是否成功
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

#endregion

#region Download Messages

/// <summary>
/// 下行测试请求
/// </summary>
[MemoryPackable]
public partial class DownloadRequest
{
    /// <summary>
    /// 请求唯一标识符
    /// </summary>
    public long RequestId { get; set; }

    /// <summary>
    /// 序列号
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>
    /// 客户端发送时间戳（纳秒）
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 请求的负载大小
    /// </summary>
    public int RequestedPayloadSize { get; set; }

    public static DownloadRequest Create(int sequenceNumber, int requestedPayloadSize)
    {
        return new DownloadRequest
        {
            RequestId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = sequenceNumber,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
            RequestedPayloadSize = requestedPayloadSize
        };
    }
}

/// <summary>
/// 下行测试响应
/// </summary>
[MemoryPackable]
public partial class DownloadResponse
{
    /// <summary>
    /// 对应的请求ID
    /// </summary>
    public long RequestId { get; set; }

    /// <summary>
    /// 负载数据
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 负载大小
    /// </summary>
    public int PayloadSize { get; set; }

    /// <summary>
    /// 服务端处理耗时（纳秒）
    /// </summary>
    public long ProcessingTimeNs { get; set; }

    /// <summary>
    /// 处理是否成功
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }
}

#endregion

#region HealthCheck Messages

/// <summary>
/// 健康检查请求
/// </summary>
[MemoryPackable]
public partial class HealthCheckRequest
{
    /// <summary>
    /// 请求时间戳
    /// </summary>
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

#endregion
