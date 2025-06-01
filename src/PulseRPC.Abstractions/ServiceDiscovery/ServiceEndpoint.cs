using System.Net;

namespace PulseRPC.ServiceDiscovery
{
    /// <summary>
    /// 服务端点信息
    /// </summary>
    public class ServiceEndpoint
    {
        /// <summary>
        /// 服务唯一标识
        /// </summary>
        public string ServiceId { get; set; } = string.Empty;

        /// <summary>
        /// 服务名称
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// 服务版本
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// 服务地址
        /// </summary>
        public IPEndPoint EndPoint { get; set; } = new(IPAddress.Any, 0);

        /// <summary>
        /// 服务权重 (用于负载均衡)
        /// </summary>
        public int Weight { get; set; } = 1;

        /// <summary>
        /// 服务标签
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new();

        /// <summary>
        /// 服务元数据
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// 健康状态
        /// </summary>
        public HealthStatus HealthStatus { get; set; } = HealthStatus.Healthy;

        /// <summary>
        /// 最后健康检查时间
        /// </summary>
        public DateTime LastHealthCheck { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 服务注册时间
        /// </summary>
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        public override string ToString()
        {
            return $"{ServiceName}({ServiceId}) @ {EndPoint} - {HealthStatus}";
        }

        public override bool Equals(object? obj)
        {
            return obj is ServiceEndpoint other && ServiceId.Equals(other.ServiceId);
        }

        public override int GetHashCode()
        {
            return ServiceId.GetHashCode();
        }
    }

    /// <summary>
    /// 健康状态枚举
    /// </summary>
    public enum HealthStatus
    {
        /// <summary>
        /// 健康
        /// </summary>
        Healthy,

        /// <summary>
        /// 不健康
        /// </summary>
        Unhealthy,

        /// <summary>
        /// 未知状态
        /// </summary>
        Unknown,

        /// <summary>
        /// 维护中
        /// </summary>
        Maintenance
    }
}
