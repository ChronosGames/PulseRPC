using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChatApp.Server;

/// <summary>
/// 玩家数据仓储接口
/// </summary>
public interface IPlayerRepository
{
    /// <summary>
    /// 根据玩家ID获取玩家数据
    /// </summary>
    Task<Player> GetPlayerAsync(string playerId);

    /// <summary>
    /// 根据玩家名称获取玩家数据
    /// </summary>
    Task<Player> GetPlayerByNameAsync(string playerName);

    /// <summary>
    /// 创建新玩家
    /// </summary>
    Task<Player> CreatePlayerAsync(Player player);

    /// <summary>
    /// 更新玩家数据
    /// </summary>
    Task<bool> UpdatePlayerAsync(Player player);

    /// <summary>
    /// 更新玩家特定字段
    /// </summary>
    Task<bool> UpdatePlayerFieldsAsync(string playerId, Dictionary<string, object> fields);

    /// <summary>
    /// 更新玩家资源（原子操作）
    /// </summary>
    Task<ResourceUpdateResult> UpdatePlayerResourcesAsync(string playerId, ResourceUpdateRequest request);

    /// <summary>
    /// 保存玩家登录信息
    /// </summary>
    Task RecordPlayerLoginAsync(string playerId, string ipAddress, string deviceInfo);

    /// <summary>
    /// 获取玩家背包
    /// </summary>
    Task<List<InventoryItem>> GetPlayerInventoryAsync(string playerId);

    /// <summary>
    /// 获取玩家货币
    /// </summary>
    Task<Dictionary<string, long>> GetPlayerCurrenciesAsync(string playerId);

    /// <summary>
    /// 获取玩家已装备道具
    /// </summary>
    Task<Dictionary<string, Guid>> GetPlayerEquippedItemsAsync(string playerId);

    /// <summary>
    /// 批量获取玩家数据
    /// </summary>
    Task<List<Player>> GetPlayersAsync(IEnumerable<string> playerIds);

    /// <summary>
    /// 按等级范围查询玩家
    /// </summary>
    Task<List<Player>> QueryPlayersByLevelRangeAsync(int minLevel, int maxLevel, int pageSize, int pageNumber);

    /// <summary>
    /// 按公会ID查询玩家
    /// </summary>
    Task<List<Player>> GetPlayersByGuildIdAsync(string guildId);

    /// <summary>
    /// 获取玩家统计信息
    /// </summary>
    Task<PlayerStatistics> GetPlayerStatisticsAsync(string playerId);

    /// <summary>
    /// 保存玩家操作日志
    /// </summary>
    Task LogPlayerActionAsync(PlayerActionLog actionLog);

    /// <summary>
    /// 检查并锁定玩家账户
    /// </summary>
    Task<bool> TryLockPlayerAccountAsync(string playerId, string reason, TimeSpan duration);

    /// <summary>
    /// 解锁玩家账户
    /// </summary>
    Task UnlockPlayerAccountAsync(string playerId);
}
