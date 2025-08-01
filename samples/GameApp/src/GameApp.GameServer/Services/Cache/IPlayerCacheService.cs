using GameApp.Shared.Services;

namespace GameApp.GameServer.Services.Cache;

/// <summary>
/// 玩家缓存服务接口
/// </summary>
public interface IPlayerCacheService
{
    /// <summary>
    /// 缓存玩家信息
    /// </summary>
    Task CachePlayerInfoAsync(PlayerInfo player);

    /// <summary>
    /// 获取缓存的玩家信息
    /// </summary>
    Task<PlayerInfo?> GetPlayerInfoAsync(int playerId);

    /// <summary>
    /// 设置玩家在线状态
    /// </summary>
    Task SetPlayerOnlineAsync(int playerId, bool isOnline);

    /// <summary>
    /// 检查玩家是否在线
    /// </summary>
    Task<bool> IsPlayerOnlineAsync(int playerId);

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    Task UpdatePlayerPositionAsync(int playerId, PlayerPosition position);

    /// <summary>
    /// 获取玩家位置
    /// </summary>
    Task<PlayerPosition?> GetPlayerPositionAsync(int playerId);

    /// <summary>
    /// 设置最后位置同步时间
    /// </summary>
    Task SetLastPositionSyncAsync(int playerId, DateTime syncTime);

    /// <summary>
    /// 获取最后位置同步时间
    /// </summary>
    Task<DateTime> GetLastPositionSyncAsync(int playerId);

    /// <summary>
    /// 移除玩家缓存
    /// </summary>
    Task RemovePlayerCacheAsync(int playerId);

    /// <summary>
    /// 获取所有在线玩家ID
    /// </summary>
    Task<List<int>> GetOnlinePlayersAsync();
}

/// <summary>
/// 世界缓存服务接口
/// </summary>
public interface IWorldCacheService
{
    /// <summary>
    /// 缓存世界状态
    /// </summary>
    Task CacheWorldStateAsync(WorldState world);

    /// <summary>
    /// 获取缓存的世界状态
    /// </summary>
    Task<WorldState?> GetWorldStateAsync(string worldId);

    /// <summary>
    /// 添加玩家到世界
    /// </summary>
    Task AddPlayerToWorldAsync(string worldId, int playerId);

    /// <summary>
    /// 从世界移除玩家
    /// </summary>
    Task RemovePlayerFromWorldAsync(string worldId, int playerId);

    /// <summary>
    /// 获取世界中的所有玩家
    /// </summary>
    Task<List<int>> GetWorldPlayersAsync(string worldId);

    /// <summary>
    /// 获取世界玩家数量
    /// </summary>
    Task<int> GetWorldPlayerCountAsync(string worldId);

    /// <summary>
    /// 移除世界缓存
    /// </summary>
    Task RemoveWorldCacheAsync(string worldId);

    /// <summary>
    /// 获取所有活跃世界ID
    /// </summary>
    Task<List<string>> GetActiveWorldsAsync();
}
