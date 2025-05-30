using System;
using System.Collections.Generic;
using MemoryPack;

namespace PulseRPC.Benchmark.Shared.Models
{
    // 基准测试消息模型定义
    // 本文件包含所有用于客户端-服务端通信的消息模型

    /// <summary>
    /// 基准测试基础请求模型
    /// </summary>
    [MemoryPackable]
    public partial class BenchmarkRequest
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
        /// 客户端标识符
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// 测试类型
        /// </summary>
        public string TestType { get; set; } = string.Empty;

        /// <summary>
        /// 负载数据
        /// </summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 测试元数据
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>
        /// 构造函数
        /// </summary>
        public BenchmarkRequest()
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000; // 转换为纳秒
        }
    }

    /// <summary>
    /// 基准测试基础响应模型
    /// </summary>
    [MemoryPackable]
    public partial class BenchmarkResponse
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
        /// 处理是否成功
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 响应负载数据
        /// </summary>
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// 构造函数
        /// </summary>
        public BenchmarkResponse()
        {
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000; // 转换为纳秒
        }

        /// <summary>
        /// 创建成功响应
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="processingTimeNs">处理耗时</param>
        /// <param name="payload">响应负载</param>
        /// <returns>成功响应实例</returns>
        public static BenchmarkResponse CreateSuccess(long requestId, long processingTimeNs, byte[]? payload = null)
        {
            return new BenchmarkResponse
            {
                RequestId = requestId,
                ProcessingTimeNs = processingTimeNs,
                Success = true,
                Payload = payload ?? Array.Empty<byte>()
            };
        }

        /// <summary>
        /// 创建失败响应
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="errorMessage">错误信息</param>
        /// <returns>失败响应实例</returns>
        public static BenchmarkResponse CreateFailure(long requestId, string errorMessage)
        {
            return new BenchmarkResponse
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = errorMessage,
                ProcessingTimeNs = 0
            };
        }
    }

    /// <summary>
    /// Echo测试请求模型
    /// </summary>
    [MemoryPackable]
    public partial class EchoRequest : BenchmarkRequest
    {
        /// <summary>
        /// Echo消息内容
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 期望的响应延迟（毫秒）
        /// </summary>
        public int ExpectedDelayMs { get; set; } = 0;

        /// <summary>
        /// 构造函数
        /// </summary>
        public EchoRequest()
        {
            TestType = "Echo";
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="message">Echo消息</param>
        /// <param name="expectedDelayMs">期望延迟</param>
        [MemoryPackConstructor]
        public EchoRequest(string message, int expectedDelayMs = 0) : this()
        {
            Message = message;
            ExpectedDelayMs = expectedDelayMs;
        }
    }

    /// <summary>
    /// Echo测试响应模型
    /// </summary>
    [MemoryPackable]
    public partial class EchoResponse : BenchmarkResponse
    {
        /// <summary>
        /// 回显的消息内容
        /// </summary>
        public string EchoMessage { get; set; } = string.Empty;

        /// <summary>
        /// 实际延迟时间（毫秒）
        /// </summary>
        public int ActualDelayMs { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public EchoResponse()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="echoMessage">回显消息</param>
        /// <param name="processingTimeNs">处理耗时</param>
        /// <param name="actualDelayMs">实际延迟</param>
        [MemoryPackConstructor]
        public EchoResponse(long requestId, string echoMessage, long processingTimeNs, int actualDelayMs) : base()
        {
            RequestId = requestId;
            EchoMessage = echoMessage;
            ProcessingTimeNs = processingTimeNs;
            ActualDelayMs = actualDelayMs;
        }
    }

    /// <summary>
    /// Ping测试请求模型
    /// </summary>
    [MemoryPackable]
    public partial class PingRequest : BenchmarkRequest
    {
        /// <summary>
        /// Ping序列号
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// 负载大小（字节）
        /// </summary>
        public int PayloadSize { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PingRequest()
        {
            TestType = "Ping";
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="sequenceNumber">序列号</param>
        /// <param name="payloadSize">负载大小</param>
        [MemoryPackConstructor]
        public PingRequest(int sequenceNumber, int payloadSize = 0) : this()
        {
            SequenceNumber = sequenceNumber;
            PayloadSize = payloadSize;
            if (payloadSize > 0)
            {
                Payload = new byte[payloadSize];
                Random.Shared.NextBytes(Payload);
            }
        }
    }

    /// <summary>
    /// Ping测试响应模型
    /// </summary>
    [MemoryPackable]
    public partial class PingResponse : BenchmarkResponse
    {
        /// <summary>
        /// 对应的Ping序列号
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// 往返时间（纳秒）
        /// </summary>
        public long RoundTripTimeNs { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PingResponse()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="sequenceNumber">序列号</param>
        /// <param name="processingTimeNs">处理耗时</param>
        /// <param name="roundTripTimeNs">往返时间</param>
        [MemoryPackConstructor]
        public PingResponse(long requestId, int sequenceNumber, long processingTimeNs, long roundTripTimeNs) : base()
        {
            RequestId = requestId;
            SequenceNumber = sequenceNumber;
            ProcessingTimeNs = processingTimeNs;
            RoundTripTimeNs = roundTripTimeNs;
        }
    }

    /// <summary>
    /// 吞吐量测试请求模型
    /// </summary>
    [MemoryPackable]
    public partial class ThroughputTestRequest : BenchmarkRequest
    {
        /// <summary>
        /// 批次编号
        /// </summary>
        public int BatchNumber { get; set; }

        /// <summary>
        /// 批次大小（消息数量）
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// 每个消息的大小（字节）
        /// </summary>
        public int MessageSize { get; set; }

        /// <summary>
        /// 测试持续时间（秒）
        /// </summary>
        public int DurationSeconds { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ThroughputTestRequest()
        {
            TestType = "Throughput";
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="batchNumber">批次编号</param>
        /// <param name="batchSize">批次大小</param>
        /// <param name="messageSize">消息大小</param>
        /// <param name="durationSeconds">持续时间</param>
        [MemoryPackConstructor]
        public ThroughputTestRequest(int batchNumber, int batchSize, int messageSize, int durationSeconds) : this()
        {
            BatchNumber = batchNumber;
            BatchSize = batchSize;
            MessageSize = messageSize;
            DurationSeconds = durationSeconds;

            // 生成指定大小的负载
            if (messageSize > 0)
            {
                Payload = new byte[messageSize];
                Random.Shared.NextBytes(Payload);
            }
        }
    }

    /// <summary>
    /// 吞吐量测试响应模型
    /// </summary>
    [MemoryPackable]
    public partial class ThroughputTestResponse : BenchmarkResponse
    {
        /// <summary>
        /// 对应的批次编号
        /// </summary>
        public int BatchNumber { get; set; }

        /// <summary>
        /// 处理的消息数量
        /// </summary>
        public int ProcessedMessages { get; set; }

        /// <summary>
        /// 服务端吞吐量（消息/秒）
        /// </summary>
        public double ThroughputMps { get; set; }

        /// <summary>
        /// 服务端带宽（字节/秒）
        /// </summary>
        public long BandwidthBps { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public ThroughputTestResponse()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="batchNumber">批次编号</param>
        /// <param name="processedMessages">处理消息数</param>
        /// <param name="processingTimeNs">处理耗时</param>
        /// <param name="throughputMps">吞吐量</param>
        /// <param name="bandwidthBps">带宽</param>
        [MemoryPackConstructor]
        public ThroughputTestResponse(long requestId, int batchNumber, int processedMessages, long processingTimeNs,
            double throughputMps, long bandwidthBps) : base()
        {
            RequestId = requestId;
            BatchNumber = batchNumber;
            ProcessedMessages = processedMessages;
            ProcessingTimeNs = processingTimeNs;
            ThroughputMps = throughputMps;
            BandwidthBps = bandwidthBps;
        }
    }

    /// <summary>
    /// 流测试请求模型
    /// </summary>
    [MemoryPackable]
    public partial class StreamTestRequest : BenchmarkRequest
    {
        /// <summary>
        /// 流ID
        /// </summary>
        public string StreamId { get; set; } = string.Empty;

        /// <summary>
        /// 流类型（Upload/Download/Bidirectional）
        /// </summary>
        public string StreamType { get; set; } = string.Empty;

        /// <summary>
        /// 数据块大小（字节）
        /// </summary>
        public int ChunkSize { get; set; }

        /// <summary>
        /// 总数据块数量
        /// </summary>
        public int TotalChunks { get; set; }

        /// <summary>
        /// 当前数据块索引
        /// </summary>
        public int ChunkIndex { get; set; }

        /// <summary>
        /// 是否为最后一个数据块
        /// </summary>
        public bool IsLastChunk { get; set; }

        /// <summary>
        /// 流控制命令（Start/Data/End/Cancel）
        /// </summary>
        public string Command { get; set; } = "Data";

        /// <summary>
        /// 构造函数
        /// </summary>
        public StreamTestRequest()
        {
            TestType = "Stream";
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="streamId">流ID</param>
        /// <param name="streamType">流类型</param>
        /// <param name="chunkSize">数据块大小</param>
        /// <param name="totalChunks">总数据块数</param>
        /// <param name="chunkIndex">当前数据块索引</param>
        /// <param name="command">流控制命令</param>
        [MemoryPackConstructor]
        public StreamTestRequest(string streamId, string streamType, int chunkSize, int totalChunks,
            int chunkIndex, string command = "Data") : this()
        {
            StreamId = streamId;
            StreamType = streamType;
            ChunkSize = chunkSize;
            TotalChunks = totalChunks;
            ChunkIndex = chunkIndex;
            IsLastChunk = chunkIndex == totalChunks - 1;
            Command = command;

            // 生成数据块负载
            if (chunkSize > 0 && command == "Data")
            {
                Payload = new byte[chunkSize];
                Random.Shared.NextBytes(Payload);
            }
        }
    }

    /// <summary>
    /// 流测试响应模型
    /// </summary>
    [MemoryPackable]
    public partial class StreamTestResponse : BenchmarkResponse
    {
        /// <summary>
        /// 对应的流ID
        /// </summary>
        public string StreamId { get; set; } = string.Empty;

        /// <summary>
        /// 已接收的数据块索引
        /// </summary>
        public int ReceivedChunkIndex { get; set; }

        /// <summary>
        /// 已接收的总字节数
        /// </summary>
        public long TotalBytesReceived { get; set; }

        /// <summary>
        /// 流传输速率（字节/秒）
        /// </summary>
        public double TransferRateBps { get; set; }

        /// <summary>
        /// 流是否完成
        /// </summary>
        public bool StreamCompleted { get; set; }

        /// <summary>
        /// 响应命令（Ack/Data/Complete/Error）
        /// </summary>
        public string ResponseCommand { get; set; } = "Ack";

        /// <summary>
        /// 构造函数
        /// </summary>
        public StreamTestResponse()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="streamId">流ID</param>
        /// <param name="receivedChunkIndex">接收数据块索引</param>
        /// <param name="totalBytesReceived">总接收字节数</param>
        /// <param name="transferRateBps">传输速率</param>
        /// <param name="streamCompleted">流是否完成</param>
        /// <param name="responseCommand">响应命令</param>
        [MemoryPackConstructor]
        public StreamTestResponse(long requestId, string streamId, int receivedChunkIndex,
            long totalBytesReceived, double transferRateBps, bool streamCompleted,
            string responseCommand = "Ack") : base()
        {
            RequestId = requestId;
            StreamId = streamId;
            ReceivedChunkIndex = receivedChunkIndex;
            TotalBytesReceived = totalBytesReceived;
            TransferRateBps = transferRateBps;
            StreamCompleted = streamCompleted;
            ResponseCommand = responseCommand;
        }
    }

    /// <summary>
    /// 服务器信息响应模型
    /// </summary>
    [MemoryPackable]
    public partial class ServerInfoResponse : BenchmarkResponse
    {
        /// <summary>
        /// 服务器名称
        /// </summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>
        /// 服务器版本
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// 服务器启动时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 支持的传输协议
        /// </summary>
        public List<string> SupportedTransports { get; set; } = new();

        /// <summary>
        /// 支持的测试类型
        /// </summary>
        public List<string> SupportedTestTypes { get; set; } = new();

        /// <summary>
        /// 服务器配置信息
        /// </summary>
        public Dictionary<string, string> Configuration { get; set; } = new();

        /// <summary>
        /// 系统信息
        /// </summary>
        public SystemInfo System { get; set; } = new();

        /// <summary>
        /// 当前连接数
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// 服务器状态
        /// </summary>
        public string Status { get; set; } = "Running";

        /// <summary>
        /// 构造函数
        /// </summary>
        public ServerInfoResponse()
        {
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="requestId">请求ID</param>
        /// <param name="serverName">服务器名称</param>
        /// <param name="version">版本</param>
        /// <param name="startTime">启动时间</param>
        [MemoryPackConstructor]
        public ServerInfoResponse(long requestId, string serverName, string version, DateTime startTime) : base()
        {
            RequestId = requestId;
            ServerName = serverName;
            Version = version;
            StartTime = startTime;
            SupportedTransports = new List<string> { "TCP", "KCP", "WebSocket" };
            SupportedTestTypes = new List<string> { "Echo", "Ping", "Throughput", "Stream" };
        }
    }

    /// <summary>
    /// 系统信息模型
    /// </summary>
    [MemoryPackable]
    public partial class SystemInfo
    {
        /// <summary>
        /// 操作系统
        /// </summary>
        public string OperatingSystem { get; set; } = string.Empty;

        /// <summary>
        /// 处理器信息
        /// </summary>
        public string Processor { get; set; } = string.Empty;

        /// <summary>
        /// 内存大小（字节）
        /// </summary>
        public long TotalMemory { get; set; }

        /// <summary>
        /// 可用内存（字节）
        /// </summary>
        public long AvailableMemory { get; set; }

        /// <summary>
        /// CPU核心数
        /// </summary>
        public int CpuCores { get; set; }

        /// <summary>
        /// .NET运行时版本
        /// </summary>
        public string RuntimeVersion { get; set; } = string.Empty;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SystemInfo()
        {
        }
    }
}
