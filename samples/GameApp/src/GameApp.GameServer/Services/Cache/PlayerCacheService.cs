using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Services.Cache;

/// <summary>
/// 玩家缓存服务实现
/// </summary>
public class PlayerCacheService : IPlayerCacheService
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<PlayerCacheService> _logger;

    private const string PLAYER_INFO_PREFIX = "player_info:";
    private const string PLAYER_ONLINE_PREFIX = "player_online:";
    private const string PLAYER_POSITION_PREFIX = "player_position:";
    private const string PLAYER_SYNC_PREFIX = "player_sync:";
    private const string ONLINE_PLAYERS_SET = "online_players";

    public PlayerCacheService(IConnectionMultiplexer redis, ILogger<PlayerCacheService> logger)
    {
        _redisDatabase = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// 缓存玩家信息
    /// </summary>
    public async Task CachePlayerInfoAsync(PlayerInfo player)
    {
        try
        {
            var key = PLAYER_INFO_PREFIX + player.PlayerId;
            var json = JsonSerializer.Serialize(player);

            await _redisDatabase.StringSetAsync(key, json, TimeSpan.FromHours(2));

            _logger.LogDebug("Cached player info: {PlayerId}", player.PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching player info: {PlayerId}", player.PlayerId);
        }
    }

    /// <summary>
    /// 获取缓存的玩家信息
    /// </summary>
    public async Task<PlayerInfo?> GetPlayerInfoAsync(int playerId)
    {
        try
        {
            var key = PLAYER_INFO_PREFIX + playerId;
            var json = await _redisDatabase.StringGetAsync(key);

            if (!json.HasValue)
            {
                return null;
            }

            var player = JsonSerializer.Deserialize<PlayerInfo>(json!);
            _logger.LogDebug("Retrieved cached player info: {PlayerId}", playerId);

            return player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached player info: {PlayerId}", playerId);
            return null;
        }
    }

    /// <summary>
    /// 设置玩家在线状态
    /// </summary>
    public async Task SetPlayerOnlineAsync(int playerId, bool isOnline)
    {
        try
        {
            var onlineKey = PLAYER_ONLINE_PREFIX + playerId;

            if (isOnline)
            {
                await _redisDatabase.StringSetAsync(onlineKey, "1", TimeSpan.FromHours(24));
                await _redisDatabase.SetAddAsync(ONLINE_PLAYERS_SET, playerId);
            }
            else
            {
                await _redisDatabase.KeyDeleteAsync(onlineKey);
                await _redisDatabase.SetRemoveAsync(ONLINE_PLAYERS_SET, playerId);
            }

            _logger.LogDebug("Set player online status: {PlayerId} -> {IsOnline}", playerId, isOnline);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting player online status: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 检查玩家是否在线
    /// </summary>
    public async Task<bool> IsPlayerOnlineAsync(int playerId)
    {
        try
        {
            var key = PLAYER_ONLINE_PREFIX + playerId;
            var isOnline = await _redisDatabase.KeyExistsAsync(key);

            return isOnline;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking player online status: {PlayerId}", playerId);
            return false;
        }
    }

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    public async Task UpdatePlayerPositionAsync(int playerId, PlayerPosition position)
    {
        try
        {
            var key = PLAYER_POSITION_PREFIX + playerId;
            var json = JsonSerializer.Serialize(position);

            await _redisDatabase.StringSetAsync(key, json, TimeSpan.FromMinutes(30));

            _logger.LogDebug("Updated cached player position: {PlayerId} -> ({X}, {Y}, {Z})",
                playerId, position.X, position.Y, position.Z);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cached player position: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取玩家位置
    /// </summary>
    public async Task<PlayerPosition?> GetPlayerPositionAsync(int playerId)
    {
        try
        {
            var key = PLAYER_POSITION_PREFIX + playerId;
            var json = await _redisDatabase.StringGetAsync(key);

            if (!json.HasValue)
            {
                return null;
            }

            var position = JsonSerializer.Deserialize<PlayerPosition>(json!);
            return position;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached player position: {PlayerId}", playerId);
            return null;
        }
    }

    /// <summary>
    /// 设置最后位置同步时间
    /// </summary>
    public async Task SetLastPositionSyncAsync(int playerId, DateTime syncTime)
    {
        try
        {
            var key = PLAYER_SYNC_PREFIX + playerId;
            var ticks = syncTime.Ticks.ToString();

            await _redisDatabase.StringSetAsync(key, ticks, TimeSpan.FromHours(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting last position sync time: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取最后位置同步时间
    /// </summary>
    public async Task<DateTime> GetLastPositionSyncAsync(int playerId)
    {
        try
        {
            var key = PLAYER_SYNC_PREFIX + playerId;
            var ticks = await _redisDatabase.StringGetAsync(key);

            if (ticks.HasValue && long.TryParse(ticks, out var ticksValue))
            {
                return new DateTime(ticksValue);
            }

            return DateTime.MinValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting last position sync time: {PlayerId}", playerId);
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 移除玩家缓存
    /// </summary>
    public async Task RemovePlayerCacheAsync(int playerId)
    {
        try
        {
            var keys = new RedisKey[]
            {
                PLAYER_INFO_PREFIX + playerId,
                PLAYER_ONLINE_PREFIX + playerId,
                PLAYER_POSITION_PREFIX + playerId,
                PLAYER_SYNC_PREFIX + playerId
            };

            await _redisDatabase.KeyDeleteAsync(keys);
            await _redisDatabase.SetRemoveAsync(ONLINE_PLAYERS_SET, playerId);

            _logger.LogDebug("Removed player cache: {PlayerId}", playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing player cache: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取所有在线玩家ID
    /// </summary>
    public async Task<List<int>> GetOnlinePlayersAsync()
    {
        try
        {
            var playerIds = await _redisDatabase.SetMembersAsync(ONLINE_PLAYERS_SET);
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
            _logger.LogError(ex, "Error getting online players");
            return new List<int>();
        }
    }
}
