namespace PulseRPC.Benchmark.Core.Interfaces;

/// <summary>
/// 基准测试传输层接口，定义传输层的基本操作
/// </summary>
public interface IBenchmarkTransport : IDisposable
{
    /// <summary>
    /// 传输层名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 连接到指定的主机和端口
    /// </summary>
    /// <param name="host">主机地址</param>
    /// <param name="port">端口号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>连接任务</returns>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>断开连接任务</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送数据
    /// </summary>
    /// <param name="data">要发送的数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否发送成功</returns>
    Task<bool> SendAsync(byte[] data, CancellationToken cancellationToken = default);

    /// <summary>
    /// 接收数据
    /// </summary>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>接收到的数据，如果超时返回null</returns>
    Task<byte[]?> ReceiveAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取传输层统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    TransportStatistics GetStatistics();

    /// <summary>
    /// 重置统计信息
    /// </summary>
    void ResetStatistics();

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>
    /// 错误发生事件
    /// </summary>
    event Action<Exception>? ErrorOccurred;
}

/// <summary>
/// 传输层统计信息
/// </summary>
public class TransportStatistics
{
    /// <summary>
    /// 总发送字节数
    /// </summary>
    public long TotalBytesSent { get; set; }

    /// <summary>
    /// 总接收字节数
    /// </summary>
    public long TotalBytesReceived { get; set; }

    /// <summary>
    /// 总发送消息数
    /// </summary>
    public long TotalMessagesSent { get; set; }

    /// <summary>
    /// 总接收消息数
    /// </summary>
    public long TotalMessagesReceived { get; set; }

    /// <summary>
    /// 错误计数
    /// </summary>
    public long ErrorCount { get; set; }

    /// <summary>
    /// 最后发送时间
    /// </summary>
    public DateTime? LastSendTime { get; set; }

    /// <summary>
    /// 最后接收时间
    /// </summary>
    public DateTime? LastReceiveTime { get; set; }

    /// <summary>
    /// 连接时间
    /// </summary>
    public DateTime? ConnectTime { get; set; }

    /// <summary>
    /// 延迟测量值列表（毫秒）
    /// </summary>
    public List<double> LatencyMeasurements { get; set; } = new();

    /// <summary>
    /// 平均延迟（毫秒）
    /// </summary>
    public double AverageLatency
    {
        get
        {
            if (LatencyMeasurements.Count == 0) return 0;
            return LatencyMeasurements.Average();
        }
    }

    /// <summary>
    /// 最小延迟（毫秒）
    /// </summary>
    public double MinLatency
    {
        get
        {
            if (LatencyMeasurements.Count == 0) return 0;
            return LatencyMeasurements.Min();
        }
    }

    /// <summary>
    /// 最大延迟（毫秒）
    /// </summary>
    public double MaxLatency
    {
        get
        {
            if (LatencyMeasurements.Count == 0) return 0;
            return LatencyMeasurements.Max();
        }
    }
}
