namespace DistributedGameApp.Infrastructure.Hosting.Bootstrap;

/// <summary>
/// 服务器启动阶段接口
/// </summary>
public interface IBootstrapPhase
{
    /// <summary>
    /// 阶段名称
    /// </summary>
    string PhaseName { get; }

    /// <summary>
    /// 执行阶段
    /// </summary>
    /// <param name="context">启动上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功</returns>
    Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 服务器启动上下文
/// 携带启动过程中的状态和信息
/// </summary>
public class BootstrapContext
{
    /// <summary>
    /// 服务提供者
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// External 服务器（外网监听）
    /// </summary>
    public PulseRPC.Server.INamedPulseServer? ExternalServer { get; set; }

    /// <summary>
    /// Internal 服务器（内网监听）
    /// </summary>
    public PulseRPC.Server.INamedPulseServer? InternalServer { get; set; }

    /// <summary>
    /// 已发现的服务器节点列表
    /// </summary>
    public Dictionary<string, List<Consul.ServiceRegistration>> DiscoveredServices { get; } = new();

    /// <summary>
    /// 例外信息（黑名单、白名单等）
    /// </summary>
    public ExceptionList ExceptionList { get; } = new();

    /// <summary>
    /// 服务注册ID
    /// </summary>
    public string? ServiceId { get; set; }

    /// <summary>
    /// 启动开始时间
    /// </summary>
    public DateTime StartTime { get; } = DateTime.UtcNow;

    /// <summary>
    /// 自定义状态数据
    /// </summary>
    public Dictionary<string, object> State { get; } = new();
}

/// <summary>
/// 例外信息列表
/// </summary>
public class ExceptionList
{
    /// <summary>
    /// 黑名单节点ID列表
    /// </summary>
    public HashSet<string> Blacklist { get; } = new();

    /// <summary>
    /// 白名单节点ID列表
    /// </summary>
    public HashSet<string> Whitelist { get; } = new();

    /// <summary>
    /// 被封禁的IP地址列表
    /// </summary>
    public HashSet<string> BannedIPs { get; } = new();

    /// <summary>
    /// 优先路由节点
    /// </summary>
    public Dictionary<string, int> PriorityNodes { get; } = new();
}
