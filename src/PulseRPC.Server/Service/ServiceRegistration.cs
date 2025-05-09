using PulseRPC.Protocol;

namespace PulseRPC.Server;

/// <summary>
/// 服务注册信息
/// </summary>
public class ServiceRegistration : IMessage
{
    /// <summary>
    /// 服务类型
    /// </summary>
    public string ServiceType { get; set; } = string.Empty;

    /// <summary>
    /// 区ID
    /// </summary>
    public string ZoneId { get; set; } = string.Empty;

    /// <summary>
    /// 服务器ID（用于GameServer等）
    /// </summary>
    public string ServerId { get; set; } = string.Empty;

    /// <summary>
    /// 实例ID（用于BattleServer等动态实例）
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// 服务主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// TCP端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// UDP端口（可选）
    /// </summary>
    public int UdpPort { get; set; }

    /// <summary>
    /// 服务元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// 注册时间
    /// </summary>
    public DateTime RegistrationTime { get; set; } = DateTime.UtcNow;
}
