using System;
using MemoryPack;

namespace PulseRPC.Benchmark.Shared.Models
{
    /// <summary>
    /// 上行测试请求模型
    /// 客户端发送大数据到服务端，测试上行带宽
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
        /// 负载数据（上行数据）
        /// </summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 负载大小（用于验证）
        /// </summary>
        public int PayloadSize { get; set; }

        /// <summary>
        /// 创建指定大小的上行请求
        /// </summary>
        public static UploadRequest Create(long requestId, int sequenceNumber, int payloadSize)
        {
            var payload = new byte[payloadSize];
            Random.Shared.NextBytes(payload);

            return new UploadRequest
            {
                RequestId = requestId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
                PayloadSize = payloadSize,
                Payload = payload
            };
        }
    }

    /// <summary>
    /// 上行测试响应模型
    /// </summary>
    [MemoryPackable]
    public partial class UploadResponse
    {
        /// <summary>
        /// 对应的请求ID
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// 服务端处理时间戳（纳秒）
        /// </summary>
        public long ServerTimestamp { get; set; }

        /// <summary>
        /// 服务端处理耗时（纳秒）
        /// </summary>
        public long ProcessingTimeNs { get; set; }

        /// <summary>
        /// 接收到的字节数
        /// </summary>
        public int ReceivedBytes { get; set; }

        /// <summary>
        /// 处理是否成功
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// 下行测试请求模型
    /// 客户端请求服务端返回大数据，测试下行带宽
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
        /// 请求的负载大小（服务端将返回此大小的数据）
        /// </summary>
        public int RequestedPayloadSize { get; set; }

        /// <summary>
        /// 创建指定大小的下行请求
        /// </summary>
        public static DownloadRequest Create(long requestId, int sequenceNumber, int requestedPayloadSize)
        {
            return new DownloadRequest
            {
                RequestId = requestId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
                RequestedPayloadSize = requestedPayloadSize
            };
        }
    }

    /// <summary>
    /// 下行测试响应模型
    /// </summary>
    [MemoryPackable]
    public partial class DownloadResponse
    {
        /// <summary>
        /// 对应的请求ID
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// 服务端处理时间戳（纳秒）
        /// </summary>
        public long ServerTimestamp { get; set; }

        /// <summary>
        /// 服务端处理耗时（纳秒）
        /// </summary>
        public long ProcessingTimeNs { get; set; }

        /// <summary>
        /// 负载数据（下行数据）
        /// </summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 负载大小
        /// </summary>
        public int PayloadSize { get; set; }

        /// <summary>
        /// 处理是否成功
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
