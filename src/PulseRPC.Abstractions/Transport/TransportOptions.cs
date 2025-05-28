using System.Collections.Generic;
using System.Threading.Tasks;

namespace PulseRPC.Transport
{
    /// <summary>
    /// 传输选项
    /// </summary>
    public class TransportOptions
    {
        /// <summary>
        /// 读取缓冲区大小
        /// </summary>
        public int ReadBufferSize { get; set; } = 8192;

        /// <summary>
        /// 写入缓冲区大小
        /// </summary>
        public int WriteBufferSize { get; set; } = 8192;

        /// <summary>
        /// 连接超时 (毫秒)
        /// </summary>
        public int ConnectionTimeout { get; set; } = 5000;

        /// <summary>
        /// 握手重试次数
        /// </summary>
        public int HandshakeRetryCount { get; set; } = 3;

        /// <summary>
        /// 单次握手超时 (毫秒)
        /// </summary>
        public int HandshakeTimeout { get; set; } = 2000;

        /// <summary>
        /// 是否启用网络诊断
        /// </summary>
        public bool EnableNetworkDiagnostics { get; set; } = true;

        /// <summary>
        /// UDP接收超时 (毫秒)
        /// </summary>
        public int UdpReceiveTimeout { get; set; } = 1000;

        /// <summary>
        /// NoDelay选项 (TCP)
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool AutoReconnect { get; set; } = false;

        /// <summary>
        /// 重连间隔 (毫秒)
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;

        /// <summary>
        /// 最大重连次数 (0表示无限)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 3;

        /// <summary>
        /// 是否启用保活 (TCP)
        /// </summary>
        public bool KeepAlive { get; set; } = true;

        /// <summary>
        /// 保活间隔 (毫秒) (TCP)
        /// </summary>
        public int KeepAliveInterval { get; set; } = 30000;

        /// <summary>
        /// 是否使用压缩
        /// </summary>
        public bool UseCompression { get; set; } = false;

        /// <summary>
        /// 是否使用加密
        /// </summary>
        public bool UseEncryption { get; set; } = false;

        /// <summary>
        /// 小包阈值
        /// </summary>
        public int SmallPacketThreshold { get; set; } = 64 * 1024;

        /// <summary>
        /// 分块大小
        /// </summary>
        public int ChunkSize { get; set; } = 32 * 1024;

        /// <summary>
        /// KCP相关选项
        /// </summary>
        public KcpOptions Kcp { get; set; } = new KcpOptions();

        /// <summary>
        /// 自定义选项
        /// </summary>
        public Dictionary<string, object> CustomOptions { get; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// KCP选项
    /// </summary>
    public class KcpOptions
    {
        /// <summary>
        /// KCP会话ID
        /// </summary>
        public uint ConversationId { get; set; } = 1;

        /// <summary>
        /// 无延迟模式
        /// 0: 默认，正常模式，有延迟
        /// 1: 无延迟模式，适用于交互性强，实时性高的场景
        /// </summary>
        public int NoDelay { get; set; } = 1;

        /// <summary>
        /// 内部更新间隔(毫秒)
        /// 越小，更新频率越高，性能消耗越大
        /// </summary>
        public int Interval { get; set; } = 10;

        /// <summary>
        /// 快速重传门限
        /// 2表示重传一次，收到2次ack未得到响应将会重传
        /// </summary>
        public int Resend { get; set; } = 2;

        /// <summary>
        /// 是否关闭拥塞控制
        /// false: 不关闭拥塞控制
        /// true: 关闭拥塞控制，适用于高带宽低延迟网络
        /// </summary>
        public bool DisableFlowControl { get; set; } = false;

        /// <summary>
        /// 发送窗口大小
        /// </summary>
        public int SendWindow { get; set; } = 32;

        /// <summary>
        /// 接收窗口大小
        /// </summary>
        public int ReceiveWindow { get; set; } = 128;
    }
}
