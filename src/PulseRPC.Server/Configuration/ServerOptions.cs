namespace PulseRPC.Server;

/// <summary>
/// 服务器配置选项
/// </summary>
public class ServerOptions
{
    /// <summary>
    /// 监听IP地址
    /// </summary>
    public string IPAddress { get; set; } = "0.0.0.0";

    /// <summary>
    /// 监听端口
    /// </summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>
    /// 接收缓冲区大小
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 8192;

    /// <summary>
    /// 发送缓冲区大小
    /// </summary>
    public int SendBufferSize { get; set; } = 8192;

    /// <summary>
    /// 心跳间隔
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 发送超时时间
    /// </summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum length of the pending connections queue.
    /// </summary>
    public int BacklogSize { get; set; } = 100;

    /// <summary>
    /// 最大数据包大小
    /// </summary>
    public int MaxPacketSize { get; set; } = 64 * 1024;

    /// <summary>
    /// 压缩阈值
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024;

    /// <summary>
    /// 是否使用加密
    /// </summary>
    public bool UseEncryption { get; set; } = false;

    /// <summary>
    /// 空闲超时
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// 是否启用TCP_NODELAY
    /// </summary>
    public bool NoDelay { get; set; } = true;
}
