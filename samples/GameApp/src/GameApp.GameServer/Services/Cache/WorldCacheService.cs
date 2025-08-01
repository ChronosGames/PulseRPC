using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Services.Cache;

/// <summary>
/// 世界缓存服务实现
/// </summary>
public class WorldCacheService : IWorldCacheService
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<WorldCacheService> _logger;

    private const string WORLD_STATE_PREFIX = "world_state:";
    private const string WORLD_PLAYERS_PREFIX = "world_players:";
    private const string ACTIVE_WORLDS_SET = "active_worlds";

    public WorldCacheService(IConnectionMultiplexer redis, ILogger<WorldCacheService> logger)
    {
        _redisDatabase = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// 缓存世界状态
    /// </summary>
    public async Task CacheWorldStateAsync(WorldState world)
    {
        try
        {
            var key = WORLD_STATE_PREFIX + world.WorldId;
            var json = JsonSerializer.Serialize(world);

            await _redisDatabase.StringSetAsync(key, json, TimeSpan.FromMinutes(10));
            await _redisDatabase.SetAddAsync(ACTIVE_WORLDS_SET, world.WorldId);

            _logger.LogDebug("Cached world state: {WorldId} -> Players: {CurrentPlayers}/{MaxPlayers}",
                world.WorldId, world.CurrentPlayers, world.MaxPlayers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching world state: {WorldId}", world.WorldId);
        }
    }

    /// <summary>
    /// 获取缓存的世界状态
    /// </summary>
    public async Task<WorldState?> GetWorldStateAsync(string worldId)
    {
        try
        {
            var key = WORLD_STATE_PREFIX + worldId;
            var json = await _redisDatabase.StringGetAsync(key);

            if (!json.HasValue)
            {
                return null;
            }

            var world = JsonSerializer.Deserialize<WorldState>(json!);
            _logger.LogDebug("Retrieved cached world state: {WorldId}", worldId);

            return world;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached world state: {WorldId}", worldId);
            return null;
        }
    }

    /// <summary>
    /// 添加玩家到世界
    /// </summary>
    public async Task AddPlayerToWorldAsync(string worldId, int playerId)
    {
        try
        {
            var key = WORLD_PLAYERS_PREFIX + worldId;
            await _redisDatabase.SetAddAsync(key, playerId);

            // 设置过期时间
            await _redisDatabase.KeyExpireAsync(key, TimeSpan.FromHours(2));

            _logger.LogDebug("Added player to world: {PlayerId} -> {WorldId}", playerId, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding player to world: {PlayerId} -> {WorldId}", playerId, worldId);
        }
    }

    /// <summary>
    /// 从世界移除玩家
    /// </summary>
    public async Task RemovePlayerFromWorldAsync(string worldId, int playerId)
    {
        try
        {
            var key = WORLD_PLAYERS_PREFIX + worldId;
            await _redisDatabase.SetRemoveAsync(key, playerId);

            _logger.LogDebug("Removed player from world: {PlayerId} <- {WorldId}", playerId, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing player from world: {PlayerId} <- {WorldId}", playerId, worldId);
        }
    }

    /// <summary>
    /// 获取世界中的所有玩家
    /// </summary>
    public async Task<List<int>> GetWorldPlayersAsync(string worldId)
    {
        try
        {
            var key = WORLD_PLAYERS_PREFIX + worldId;
            var playerIds = await _redisDatabase.SetMembersAsync(key);
            var result = new List<int>();

            foreach (var playerId in playerIds)
            {
                if (playerId.HasValue && int.TryParse(playerId, out var id))
                {
                    result.Add(id);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting world players: {WorldId}", worldId);
            return new List<int>();
        }
    }

    /// <summary>
    /// 获取世界玩家数量
    /// </summary>
    public async Task<int> GetWorldPlayerCountAsync(string worldId)
    {
        try
        {
            var key = WORLD_PLAYERS_PREFIX + worldId;
            var count = await _redisDatabase.SetLengthAsync(key);

            return (int)count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting world player count: {WorldId}", worldId);
            return 0;
        }
    }

    /// <summary>
    /// 移除世界缓存
    /// </summary>
    public async Task RemoveWorldCacheAsync(string worldId)
    {
        try
        {
            var keys = new RedisKey[]
            {
                WORLD_STATE_PREFIX + worldId,
                WORLD_PLAYERS_PREFIX + worldId
            };

            await _redisDatabase.KeyDeleteAsync(keys);
            await _redisDatabase.SetRemoveAsync(ACTIVE_WORLDS_SET, worldId);

            _logger.LogDebug("Removed world cache: {WorldId}", worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing world cache: {WorldId}", worldId);
        }
    }

    /// <summary>
    /// 获取所有活跃世界ID
    /// </summary>
    public async Task<List<string>> GetActiveWorldsAsync()
    {
        try
        {
            var worldIds = await _redisDatabase.SetMembersAsync(ACTIVE_WORLDS_SET);
            var result = new List<string>();

            foreach (var worldId in worldIds)
            {
                if (worldId.HasValue)
                {
                    result.Add(worldId!);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active worlds");
            return new List<string>();
        }
    }
}
