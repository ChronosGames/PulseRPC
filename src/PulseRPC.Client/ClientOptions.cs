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

        /// <summary>
        /// 服务发现配置
        /// </summary>
        public ServiceDiscoveryOptions ServiceDiscoveryOptions { get; set; } = new();

        /// <summary>
        /// 连接池配置
        /// </summary>
        public ConnectionPoolOptions ConnectionPoolOptions { get; set; } = new();
    }

    /// <summary>
    /// 服务发现类型
    /// </summary>
    public enum ServiceDiscoveryType
    {
        /// <summary>
        /// 静态配置
        /// </summary>
        Static,

        /// <summary>
        /// Consul
        /// </summary>
        Consul,

        /// <summary>
        /// Etcd
        /// </summary>
        Etcd,

        /// <summary>
        /// DNS
        /// </summary>
        Dns,

        /// <summary>
        /// 自定义
        /// </summary>
        Custom
    }

    /// <summary>
    /// 服务发现配置选项
    /// </summary>
    public class ServiceDiscoveryOptions
    {
        /// <summary>
        /// 服务发现类型
        /// </summary>
        public ServiceDiscoveryType Type { get; set; } = ServiceDiscoveryType.Static;

        /// <summary>
        /// 是否启用服务发现
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Consul 服务器地址
        /// </summary>
        public string? ConsulAddress { get; set; } = "http://localhost:8500";

        /// <summary>
        /// Etcd 端点列表
        /// </summary>
        public string[]? EtcdEndpoints { get; set; }

        /// <summary>
        /// DNS 解析域名
        /// </summary>
        public string? DnsDomain { get; set; }

        /// <summary>
        /// 静态端点配置 (服务名 -> 端点列表)
        /// </summary>
        public Dictionary<string, string[]> StaticEndpoints { get; set; } = new();

        /// <summary>
        /// 服务发现刷新间隔
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 是否启用服务监听 (实时更新)
        /// </summary>
        public bool EnableWatch { get; set; } = true;

        /// <summary>
        /// 是否启用缓存
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// 缓存超时时间
        /// </summary>
        public TimeSpan CacheTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 服务发现超时时间
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 标签过滤条件
        /// </summary>
        public Dictionary<string, string> TagFilters { get; set; } = new();

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 重试延迟
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 是否启用故障转移
        /// </summary>
        public bool EnableFailover { get; set; } = true;
    }

    /// <summary>
    /// 连接池配置选项
    /// </summary>
    public class ConnectionPoolOptions
    {
        /// <summary>
        /// 是否启用连接池
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 最大连接数
        /// </summary>
        public int MaxConnections { get; set; } = 100;

        /// <summary>
        /// 最小连接数
        /// </summary>
        public int MinConnections { get; set; } = 10;

        /// <summary>
        /// 连接空闲超时时间
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// 连接生存时间
        /// </summary>
        public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// 获取连接超时时间
        /// </summary>
        public TimeSpan AcquireTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// 连接验证间隔
        /// </summary>
        public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// 是否在借用时验证连接
        /// </summary>
        public bool ValidateOnBorrow { get; set; } = true;

        /// <summary>
        /// 是否在归还时验证连接
        /// </summary>
        public bool ValidateOnReturn { get; set; } = false;

        /// <summary>
        /// 是否在空闲时验证连接
        /// </summary>
        public bool ValidateOnIdle { get; set; } = true;
    }
}
