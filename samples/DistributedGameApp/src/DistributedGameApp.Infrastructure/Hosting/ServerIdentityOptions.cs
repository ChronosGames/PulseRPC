namespace DistributedGameApp.Infrastructure.Hosting;

/// <summary>
/// 服务器标识配置
/// </summary>
public sealed class ServerIdentityOptions
{
    /// <summary>
    /// 服务类型（如 GameServer, BattleServer, BackendServer, LoginServer）
    /// </summary>
    public required string ServiceType { get; init; }

    /// <summary>
    /// 节点ID（同类型服务器的唯一标识）
    /// </summary>
    public required int NodeId { get; init; }

    /// <summary>
    /// 节点名称（人类可读的标识）
    /// </summary>
    public required string NodeName { get; init; }

    /// <summary>
    /// 最大容量（并发连接数、玩家数等）
    /// </summary>
    public int MaxCapacity { get; init; } = 5000;
}
