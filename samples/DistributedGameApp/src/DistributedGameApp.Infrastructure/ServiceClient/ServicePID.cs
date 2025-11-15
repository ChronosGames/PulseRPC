namespace DistributedGameApp.Infrastructure.ServiceClient;

/// <summary>
/// 服务进程标识符 (PID)
/// 组合结构：ServerId + NodeId + ServiceType + ServiceId
/// </summary>
public readonly struct ServicePID : IEquatable<ServicePID>
{
    /// <summary>
    /// 区服配置编号
    /// </summary>
    public int ServerId { get; init; }

    /// <summary>
    /// 进程配置编号（节点ID）
    /// </summary>
    public int NodeId { get; init; }

    /// <summary>
    /// 服务类型
    /// </summary>
    public string ServiceType { get; init; }

    /// <summary>
    /// 服务ID（若非单例则为具体ID，单例则为空字符串）
    /// </summary>
    public string ServiceId { get; init; }

    /// <summary>
    /// 是否为单例服务
    /// </summary>
    public bool IsSingleton => string.IsNullOrEmpty(ServiceId);

    public ServicePID(int serverId, int nodeId, string serviceType, string serviceId = "")
    {
        ServerId = serverId;
        NodeId = nodeId;
        ServiceType = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        ServiceId = serviceId ?? string.Empty;
    }

    /// <summary>
    /// 获取完整的 PID 字符串
    /// 格式：{ServerId}-{NodeId}-{ServiceType}[-{ServiceId}]
    /// </summary>
    public string GetFullPID()
    {
        if (IsSingleton)
            return $"{ServerId}-{NodeId}-{ServiceType}";
        else
            return $"{ServerId}-{NodeId}-{ServiceType}-{ServiceId}";
    }

    /// <summary>
    /// 获取路由键（用于一致性哈希）
    /// </summary>
    public string GetRoutingKey()
    {
        // 如果是单例，使用 ServiceType 作为路由键
        // 如果非单例，使用 ServiceId 作为路由键
        return IsSingleton ? ServiceType : ServiceId;
    }

    /// <summary>
    /// 从字符串解析 PID
    /// </summary>
    public static ServicePID Parse(string pid)
    {
        if (string.IsNullOrEmpty(pid))
            throw new ArgumentException("PID cannot be null or empty", nameof(pid));

        var parts = pid.Split('-');
        if (parts.Length < 3)
            throw new FormatException($"Invalid PID format: {pid}. Expected: ServerId-NodeId-ServiceType[-ServiceId]");

        if (!int.TryParse(parts[0], out var serverId))
            throw new FormatException($"Invalid ServerId in PID: {pid}");

        if (!int.TryParse(parts[1], out var nodeId))
            throw new FormatException($"Invalid NodeId in PID: {pid}");

        var serviceType = parts[2];
        var serviceId = parts.Length > 3 ? string.Join("-", parts.Skip(3)) : string.Empty;

        return new ServicePID(serverId, nodeId, serviceType, serviceId);
    }

    /// <summary>
    /// 尝试从字符串解析 PID
    /// </summary>
    public static bool TryParse(string pid, out ServicePID result)
    {
        try
        {
            result = Parse(pid);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public override string ToString() => GetFullPID();

    public override bool Equals(object? obj) => obj is ServicePID pid && Equals(pid);

    public bool Equals(ServicePID other) =>
        ServerId == other.ServerId &&
        NodeId == other.NodeId &&
        ServiceType == other.ServiceType &&
        ServiceId == other.ServiceId;

    public override int GetHashCode() => HashCode.Combine(ServerId, NodeId, ServiceType, ServiceId);

    public static bool operator ==(ServicePID left, ServicePID right) => left.Equals(right);
    public static bool operator !=(ServicePID left, ServicePID right) => !left.Equals(right);
}
