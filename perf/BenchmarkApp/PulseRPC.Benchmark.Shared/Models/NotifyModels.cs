using System;
using MemoryPack;

namespace PulseRPC.Benchmark.Shared.Models
{
    /// <summary>
    /// Notify测试请求模型 (Fire-and-Forget)
    /// 用于测试无返回值的单向消息吞吐量
    /// </summary>
    [MemoryPackable]
    public partial class NotifyRequest
    {
        /// <summary>
        /// 请求唯一标识符
        /// </summary>
        public long RequestId { get; set; }

        /// <summary>
        /// 序列号，用于统计
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

        /// <summary>
        /// 创建指定大小的 Notify 请求
        /// </summary>
        public static NotifyRequest Create(long requestId, int sequenceNumber, int payloadSize = 64)
        {
            var payload = new byte[payloadSize];
            Random.Shared.NextBytes(payload);

            return new NotifyRequest
            {
                RequestId = requestId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
                Payload = payload
            };
        }
    }
}
