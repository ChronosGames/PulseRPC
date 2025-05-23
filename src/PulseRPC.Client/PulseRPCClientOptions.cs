using System;

namespace PulseRPC.Client
{
    /// <summary>
    /// PulseRPC 客户端配置选项
    /// </summary>
    public class PulseRPCClientOptions
    {
        /// <summary>
        /// 服务器地址
        /// </summary>
        public string ServerAddress { get; set; } = "localhost";

        /// <summary>
        /// 服务器端口
        /// </summary>
        public int ServerPort { get; set; } = 12345;

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 请求超时时间（毫秒）
        /// </summary>
        public int RequestTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// 心跳间隔（毫秒）
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 30000;

        /// <summary>
        /// 自动重连
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 重连间隔（毫秒）
        /// </summary>
        public int ReconnectIntervalMs { get; set; } = 5000;

        /// <summary>
        /// 最大重连次数
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 5;

        /// <summary>
        /// 平台适配器
        /// </summary>
        public IPlatformAdapter? PlatformAdapter { get; set; }

        /// <summary>
        /// 使用 Unity 优化
        /// </summary>
        public bool UseUnityOptimizations { get; set; } = false;

        /// <summary>
        /// 启用 Unity 主线程调度
        /// </summary>
        public bool EnableUnityMainThreadDispatch { get; set; } = false;

        /// <summary>
        /// 验证配置选项
        /// </summary>
        /// <exception cref="ArgumentException">配置无效时抛出</exception>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ServerAddress))
                throw new ArgumentException("ServerAddress cannot be null or empty", nameof(ServerAddress));

            if (ServerPort <= 0 || ServerPort > 65535)
                throw new ArgumentException("ServerPort must be between 1 and 65535", nameof(ServerPort));

            if (ConnectionTimeoutMs <= 0)
                throw new ArgumentException("ConnectionTimeoutMs must be positive", nameof(ConnectionTimeoutMs));

            if (RequestTimeoutMs <= 0)
                throw new ArgumentException("RequestTimeoutMs must be positive", nameof(RequestTimeoutMs));

            if (HeartbeatIntervalMs <= 0)
                throw new ArgumentException("HeartbeatIntervalMs must be positive", nameof(HeartbeatIntervalMs));

            if (AutoReconnect)
            {
                if (ReconnectIntervalMs <= 0)
                    throw new ArgumentException("ReconnectIntervalMs must be positive when AutoReconnect is enabled", nameof(ReconnectIntervalMs));

                if (MaxReconnectAttempts <= 0)
                    throw new ArgumentException("MaxReconnectAttempts must be positive when AutoReconnect is enabled", nameof(MaxReconnectAttempts));
            }
        }
    }
}
