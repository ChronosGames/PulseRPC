namespace PulseRPC.Client
{
    /// <summary>
    /// 客户端选项
    /// </summary>
    public class ClientOptions
    {
        /// <summary>
        /// 连接超时时间
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 接收缓冲区大小
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 8192;

        /// <summary>
        /// 发送缓冲区大小
        /// </summary>
        public int SendBufferSize { get; set; } = 8192;

        /// <summary>
        /// 是否自动重连
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连尝试次数
        /// </summary>
        public int ReconnectAttempts { get; set; } = 3;

        /// <summary>
        /// 初始重连延迟
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 最大重连延迟
        /// </summary>
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 心跳间隔
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否禁用Nagle算法
        /// </summary>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// 是否使用加密
        /// </summary>
        public bool UseEncryption { get; set; } = false;

        /// <summary>
        /// 加密密钥
        /// </summary>
        public string? EncryptionKey { get; set; } = null;
    }
}
