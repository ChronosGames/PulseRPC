namespace PulseRPC.Protocol.Network;

public class NetworkOptions
{
    // 压缩阈值，超过该大小的数据包将被压缩(默认1KB)
    public int CompressionThreshold { get; set; } = 1024;

    // 最大包大小（64KB - 1）
    public int MaxPacketSize { get; set; } = 65535;

    // Socket缓冲区大小
    public int SocketBufferSize { get; set; } = 8192;

    // 请求超时时间(毫秒)
    public int RequestTimeout { get; set; } = 10000;

    /// <summary>
    /// 发送超时时间(毫秒)
    /// </summary>
    public int SendTimeout { get; set; } = 10000;

    /// <summary>
    /// 接收超时时间(毫秒)
    /// </summary>
    public int ReceiveTimeout { get; set; } = 10000;

    /// <summary>
    /// 心跳间隔(毫秒)
    /// </summary>
    public int HeartbeatInterval { get; set; } = 30000;
}
