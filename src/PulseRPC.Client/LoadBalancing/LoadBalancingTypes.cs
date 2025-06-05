namespace PulseRPC.Client.LoadBalancing
{
    /// <summary>
    /// 负载均衡策略枚举
    /// </summary>
    public enum LoadBalancingStrategy
    {
        /// <summary>
        /// 轮询
        /// </summary>
        RoundRobin,

        /// <summary>
        /// 加权轮询
        /// </summary>
        WeightedRoundRobin,

        /// <summary>
        /// 最少连接
        /// </summary>
        LeastConnections,

        /// <summary>
        /// 加权最少连接
        /// </summary>
        WeightedLeastConnections,

        /// <summary>
        /// 随机
        /// </summary>
        Random,

        /// <summary>
        /// 加权随机
        /// </summary>
        WeightedRandom,

        /// <summary>
        /// 一致性哈希
        /// </summary>
        ConsistentHash,

        /// <summary>
        /// 最快响应
        /// </summary>
        FastestResponse,

        /// <summary>
        /// 故障转移
        /// </summary>
        Failover,

        /// <summary>
        /// 自定义
        /// </summary>
        Custom
    }

    /// <summary>
    /// 负载均衡结果
    /// </summary>
    public enum LoadBalancingResult
    {
        /// <summary>
        /// 成功
        /// </summary>
        Success,

        /// <summary>
        /// 连接失败
        /// </summary>
        ConnectionFailed,

        /// <summary>
        /// 超时
        /// </summary>
        Timeout,

        /// <summary>
        /// 服务器错误
        /// </summary>
        ServerError,

        /// <summary>
        /// 客户端错误
        /// </summary>
        ClientError,

        Failure,

        ServiceUnavailable,
    }

    /// <summary>
    /// 负载均衡上下文
    /// </summary>
    public class LoadBalancingContext
    {
        /// <summary>
        /// 请求标识
        /// </summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 客户端标识
        /// </summary>
        public string? ClientId { get; set; }

        /// <summary>
        /// 会话标识 (用于会话粘滞)
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// 一致性哈希键
        /// </summary>
        public string? HashKey { get; set; }

        /// <summary>
        /// 请求权重
        /// </summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// 上下文数据
        /// </summary>
        public Dictionary<string, object> Data { get; set; } = new();

        /// <summary>
        /// 请求时间
        /// </summary>
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    }
}
