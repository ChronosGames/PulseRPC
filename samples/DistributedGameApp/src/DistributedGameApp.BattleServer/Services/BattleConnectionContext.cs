using System.Collections.Concurrent;

namespace DistributedGameApp.BattleServer.Services;

/// <summary>
/// 战斗连接上下文 - 管理连接与战斗房间的关联
/// </summary>
/// <remarks>
/// 替代 AsyncLocal，在分布式环境中更可靠
/// 使用 ConnectionId 作为键来跟踪每个连接的战斗状态
/// </remarks>
public class BattleConnectionContext
{
    private readonly ConcurrentDictionary<string, BattleConnectionState> _connectionStates = new();

    /// <summary>
    /// 关联连接到战斗房间
    /// </summary>
    public void JoinBattle(string connectionId, string battleId, string characterId)
    {
        var state = new BattleConnectionState
        {
            BattleId = battleId,
            CharacterId = characterId,
            JoinTime = DateTime.UtcNow
        };

        _connectionStates[connectionId] = state;
    }

    /// <summary>
    /// 获取连接的战斗状态
    /// </summary>
    public BattleConnectionState? GetState(string connectionId)
    {
        _connectionStates.TryGetValue(connectionId, out var state);
        return state;
    }

    /// <summary>
    /// 移除连接的战斗状态
    /// </summary>
    public bool LeaveBattle(string connectionId)
    {
        return _connectionStates.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// 获取战斗房间的所有连接
    /// </summary>
    public List<string> GetConnectionsByBattle(string battleId)
    {
        return _connectionStates
            .Where(kvp => kvp.Value.BattleId == battleId)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// 获取在线连接数
    /// </summary>
    public int GetConnectionCount() => _connectionStates.Count;
}

/// <summary>
/// 战斗连接状态
/// </summary>
public class BattleConnectionState
{
    /// <summary>
    /// 战斗ID
    /// </summary>
    public required string BattleId { get; init; }

    /// <summary>
    /// 角色ID
    /// </summary>
    public required string CharacterId { get; init; }

    /// <summary>
    /// 加入时间
    /// </summary>
    public DateTime JoinTime { get; init; }
}
