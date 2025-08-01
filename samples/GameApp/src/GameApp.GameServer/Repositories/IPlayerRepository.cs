using GameApp.Shared.Services;

namespace GameApp.GameServer.Repositories;

/// <summary>
/// 玩家数据仓储接口
/// </summary>
public interface IPlayerRepository
{
    /// <summary>
    /// 根据玩家ID获取玩家信息
    /// </summary>
    Task<PlayerInfo?> GetPlayerByIdAsync(int playerId);

    /// <summary>
    /// 根据用户ID获取玩家信息
    /// </summary>
    Task<PlayerInfo?> GetPlayerByUserIdAsync(int userId);

    /// <summary>
    /// 创建新玩家
    /// </summary>
    Task<PlayerInfo> CreatePlayerAsync(PlayerInfo player);

    /// <summary>
    /// 保存玩家数据
    /// </summary>
    Task SavePlayerDataAsync(PlayerInfo player);

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    Task UpdatePlayerPositionAsync(int playerId, PlayerPosition position);

    /// <summary>
    /// 更新玩家状态
    /// </summary>
    Task UpdatePlayerStatusAsync(int playerId, PlayerStatus status);

    /// <summary>
    /// 更新最后活跃时间
    /// </summary>
    Task UpdateLastActiveTimeAsync(int playerId, DateTime lastActiveTime);

    /// <summary>
    /// 获取玩家统计信息
    /// </summary>
    Task<PlayerStatistics?> GetPlayerStatisticsAsync(int playerId);

    /// <summary>
    /// 更新玩家统计信息
    /// </summary>
    Task UpdatePlayerStatisticsAsync(PlayerStatistics statistics);
}

/// <summary>
/// 世界数据仓储接口
/// </summary>
public interface IWorldRepository
{
    /// <summary>
    /// 根据世界ID获取世界信息
    /// </summary>
    Task<WorldState?> GetWorldByIdAsync(string worldId);

    /// <summary>
    /// 获取所有世界列表
    /// </summary>
    Task<List<WorldState>> GetAllWorldsAsync();

    /// <summary>
    /// 更新世界状态
    /// </summary>
    Task UpdateWorldStateAsync(WorldState world);

    /// <summary>
    /// 创建新世界
    /// </summary>
    Task<WorldState> CreateWorldAsync(WorldState world);

    /// <summary>
    /// 删除世界
    /// </summary>
    Task DeleteWorldAsync(string worldId);
}
