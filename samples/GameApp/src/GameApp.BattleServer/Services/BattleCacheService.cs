using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 战斗缓存服务实现
/// </summary>
public class BattleCacheService : IBattleCacheService
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<BattleCacheService> _logger;

    private const string BATTLE_INFO_PREFIX = "battle_info:";
    private const string PLAYER_BATTLE_STATE_PREFIX = "player_battle:";
    private const string SKILL_COOLDOWN_PREFIX = "skill_cooldown:";

    public BattleCacheService(IConnectionMultiplexer redis, ILogger<BattleCacheService> logger)
    {
        _redisDatabase = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// 缓存战斗信息
    /// </summary>
    public async Task CacheBattleInfoAsync(BattleInfo battleInfo)
    {
        try
        {
            var key = BATTLE_INFO_PREFIX + battleInfo.BattleId;
            var json = JsonSerializer.Serialize(battleInfo);

            await _redisDatabase.StringSetAsync(key, json, TimeSpan.FromMinutes(30));

            _logger.LogDebug("Cached battle info: {BattleId}", battleInfo.BattleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching battle info: {BattleId}", battleInfo.BattleId);
        }
    }

    /// <summary>
    /// 获取缓存的战斗信息
    /// </summary>
    public async Task<BattleInfo?> GetCachedBattleInfoAsync(string battleId)
    {
        try
        {
            var key = BATTLE_INFO_PREFIX + battleId;
            var json = await _redisDatabase.StringGetAsync(key);

            if (!json.HasValue)
            {
                return null;
            }

            var battleInfo = JsonSerializer.Deserialize<BattleInfo>(json!);
            _logger.LogDebug("Retrieved cached battle info: {BattleId}", battleId);

            return battleInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached battle info: {BattleId}", battleId);
            return null;
        }
    }

    /// <summary>
    /// 缓存玩家战斗状态
    /// </summary>
    public async Task CachePlayerBattleStateAsync(int playerId, string battleId, BattlePlayer playerState)
    {
        try
        {
            var key = PLAYER_BATTLE_STATE_PREFIX + playerId;
            var stateData = new PlayerBattleStateCache
            {
                BattleId = battleId,
                PlayerState = playerState,
                LastUpdate = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(stateData);
            await _redisDatabase.StringSetAsync(key, json, TimeSpan.FromHours(1));

            _logger.LogDebug("Cached player battle state: Player {PlayerId} in battle {BattleId}",
                playerId, battleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching player battle state: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取玩家战斗状态
    /// </summary>
    public async Task<BattlePlayer?> GetPlayerBattleStateAsync(int playerId)
    {
        try
        {
            var key = PLAYER_BATTLE_STATE_PREFIX + playerId;
            var json = await _redisDatabase.StringGetAsync(key);

            if (!json.HasValue)
            {
                return null;
            }

            var stateData = JsonSerializer.Deserialize<PlayerBattleStateCache>(json!);
            if (stateData == null)
            {
                return null;
            }

            _logger.LogDebug("Retrieved player battle state: Player {PlayerId} in battle {BattleId}",
                playerId, stateData.BattleId);

            return stateData.PlayerState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player battle state: {PlayerId}", playerId);
            return null;
        }
    }

    /// <summary>
    /// 设置技能冷却
    /// </summary>
    public async Task SetSkillCooldownAsync(int playerId, int skillId, DateTime expireTime)
    {
        try
        {
            var key = $"{SKILL_COOLDOWN_PREFIX}{playerId}:{skillId}";
            var expireTimeSpan = expireTime - DateTime.UtcNow;

            if (expireTimeSpan > TimeSpan.Zero)
            {
                await _redisDatabase.StringSetAsync(key, "1", expireTimeSpan);

                _logger.LogDebug("Set skill cooldown: Player {PlayerId}, Skill {SkillId}, Expires: {ExpireTime}",
                    playerId, skillId, expireTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting skill cooldown: Player {PlayerId}, Skill {SkillId}",
                playerId, skillId);
        }
    }

    /// <summary>
    /// 检查技能冷却
    /// </summary>
    public async Task<bool> IsSkillOnCooldownAsync(int playerId, int skillId)
    {
        try
        {
            var key = $"{SKILL_COOLDOWN_PREFIX}{playerId}:{skillId}";
            var exists = await _redisDatabase.KeyExistsAsync(key);

            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking skill cooldown: Player {PlayerId}, Skill {SkillId}",
                playerId, skillId);
            return false;
        }
    }

    /// <summary>
    /// 移除战斗缓存
    /// </summary>
    public async Task RemoveBattleCacheAsync(string battleId)
    {
        try
        {
            var battleKey = BATTLE_INFO_PREFIX + battleId;
            await _redisDatabase.KeyDeleteAsync(battleKey);

            _logger.LogDebug("Removed battle cache: {BattleId}", battleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing battle cache: {BattleId}", battleId);
        }
    }
}

/// <summary>
/// 玩家战斗状态缓存模型
/// </summary>
public class PlayerBattleStateCache
{
    public string BattleId { get; set; } = string.Empty;
    public BattlePlayer PlayerState { get; set; } = new();
    public DateTime LastUpdate { get; set; }
}
