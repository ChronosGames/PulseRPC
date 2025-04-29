using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ChatApp.Server;

/// <summary>
/// Redis实现的玩家仓储
/// </summary>
public class RedisPlayerRepository : IPlayerRepository
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger _logger;
    private readonly IJsonSerializer _serializer;
    private readonly IMongoPlayerRepository _mongoRepo; // 用于持久化存储

    // Redis键前缀
    private const string PLAYER_KEY_PREFIX = "player:";
    private const string INVENTORY_KEY_PREFIX = "inventory:";
    private const string PLAYER_LOCK_KEY_PREFIX = "player:lock:";
    private const string CURRENCY_KEY_PREFIX = "currency:";

    // 缓存过期时间
    private static readonly TimeSpan PLAYER_CACHE_TTL = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan INVENTORY_CACHE_TTL = TimeSpan.FromMinutes(15);

    public RedisPlayerRepository(
        IConnectionMultiplexer redis,
        IMongoPlayerRepository mongoRepo,
        IJsonSerializer serializer,
        ILogger<RedisPlayerRepository> logger)
    {
        _redis = redis;
        _mongoRepo = mongoRepo;
        _serializer = serializer;
        _logger = logger;
    }

    public async Task<Player> GetPlayerAsync(string playerId)
    {
        try
        {
            // 1. 尝试从Redis获取
            var db = _redis.GetDatabase();
            var playerKey = GetPlayerKey(playerId);
            var cachedData = await db.StringGetAsync(playerKey);

            if (cachedData.HasValue)
            {
                // 从缓存获取成功
                var player1 = _serializer.Deserialize<Player>(cachedData);
                // 刷新过期时间
                await db.KeyExpireAsync(playerKey, PLAYER_CACHE_TTL);
                return player1;
            }

            // 2. 从MongoDB获取
            var player = await _mongoRepo.GetPlayerAsync(playerId);

            if (player != null)
            {
                // 更新缓存
                await CachePlayerAsync(player);
            }

            return player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving player {playerId}");

            // 缓存出错时直接从MongoDB获取
            return await _mongoRepo.GetPlayerAsync(playerId);
        }
    }

    public async Task<Player> GetPlayerByNameAsync(string playerName)
    {
        // 由于用户名查询不常用，直接从MongoDB获取
        return await _mongoRepo.GetPlayerByNameAsync(playerName);
    }

    public async Task<Player> CreatePlayerAsync(Player player)
    {
        // 先在MongoDB中创建
        var createdPlayer = await _mongoRepo.CreatePlayerAsync(player);

        // 创建成功后再缓存
        await CachePlayerAsync(createdPlayer);

        return createdPlayer;
    }

    public async Task<bool> UpdatePlayerAsync(Player player)
    {
        // 先在MongoDB中更新
        bool success = await _mongoRepo.UpdatePlayerAsync(player);

        if (success)
        {
            // 更新成功后再缓存
            await CachePlayerAsync(player);
        }

        return success;
    }

    public async Task<bool> UpdatePlayerFieldsAsync(string playerId, Dictionary<string, object> fields)
    {
        // 先在MongoDB中更新
        bool success = await _mongoRepo.UpdatePlayerFieldsAsync(playerId, fields);

        if (success)
        {
            // 更新缓存
            // 方案1: 使用HMSET更新特定字段 (仅适用于扁平结构)
            // 方案2: 无效化缓存，下次获取时重新加载
            await InvalidatePlayerCacheAsync(playerId);
        }

        return success;
    }

    public async Task<ResourceUpdateResult> UpdatePlayerResourcesAsync(string playerId, ResourceUpdateRequest request)
    {
        var db = _redis.GetDatabase();

        // 1. 尝试获取资源锁
        string lockKey = $"lock:player:resource:{playerId}";
        string lockValue = Guid.NewGuid().ToString();
        bool lockAcquired = await db.LockTakeAsync(lockKey, lockValue, TimeSpan.FromSeconds(10));

        if (!lockAcquired)
        {
            return new ResourceUpdateResult
            {
                Success = false,
                ErrorCode = "ResourceLocked",
                ErrorMessage = "Resource update is already in progress"
            };
        }

        try
        {
            // 2. 先在MongoDB中更新
            var result = await _mongoRepo.UpdatePlayerResourcesAsync(playerId, request);

            if (result.Success)
            {
                // 3. 无效化缓存
                await InvalidatePlayerCacheAsync(playerId);

                // 4. 更新缓存中的货币数据 (可选)
                if (result.NewValues.Count > 0)
                {
                    string currencyKey = GetCurrencyKey(playerId);
                    var hashEntries = result.NewValues
                        .Select(kv => new HashEntry(kv.Key, kv.Value))
                        .ToArray();

                    await db.HashSetAsync(currencyKey, hashEntries);
                    await db.KeyExpireAsync(currencyKey, PLAYER_CACHE_TTL);
                }
            }

            return result;
        }
        finally
        {
            // 释放锁
            await db.LockReleaseAsync(lockKey, lockValue);
        }
    }

    public async Task RecordPlayerLoginAsync(string playerId, string ipAddress, string deviceInfo)
    {
        // 这是写操作，而且需要更新多个集合，直接委托给MongoDB实现
        await _mongoRepo.RecordPlayerLoginAsync(playerId, ipAddress, deviceInfo);

        // 无效化缓存
        await InvalidatePlayerCacheAsync(playerId);
    }

    public async Task<List<InventoryItem>> GetPlayerInventoryAsync(string playerId)
    {
        try
        {
            // 1. 尝试从Redis获取
            var db = _redis.GetDatabase();
            string inventoryKey = GetInventoryKey(playerId);
            var cachedData = await db.StringGetAsync(inventoryKey);

            if (cachedData.HasValue)
            {
                // 从缓存获取成功
                var inventory = _serializer.Deserialize<List<InventoryItem>>(cachedData);
                // 刷新过期时间
                await db.KeyExpireAsync(inventoryKey, INVENTORY_CACHE_TTL);
                return inventory;
            }

            // 2. 从MongoDB获取
            var items = await _mongoRepo.GetPlayerInventoryAsync(playerId);

            if (items != null && items.Count > 0)
            {
                // 缓存结果
                string serialized = _serializer.Serialize(items);
                await db.StringSetAsync(inventoryKey, serialized, INVENTORY_CACHE_TTL);
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving inventory for player {playerId}");

            // 缓存出错时直接从MongoDB获取
            return await _mongoRepo.GetPlayerInventoryAsync(playerId);
        }
    }

    public async Task<Dictionary<string, long>> GetPlayerCurrenciesAsync(string playerId)
    {
        try
        {
            // 1. 尝试从Redis获取
            var db = _redis.GetDatabase();
            string currencyKey = GetCurrencyKey(playerId);
            HashEntry[] entries = await db.HashGetAllAsync(currencyKey);

            if (entries.Length > 0)
            {
                // 从缓存获取成功
                var currencies = entries.ToDictionary(
                    entry => entry.Name.ToString(),
                    entry => (long)entry.Value);

                // 刷新过期时间
                await db.KeyExpireAsync(currencyKey, PLAYER_CACHE_TTL);
                return currencies;
            }

            // 2. 从MongoDB获取
            var currenciesFromDb = await _mongoRepo.GetPlayerCurrenciesAsync(playerId);

            if (currenciesFromDb != null && currenciesFromDb.Count > 0)
            {
                // 缓存结果
                var hashEntries = currenciesFromDb
                    .Select(kv => new HashEntry(kv.Key, kv.Value))
                    .ToArray();

                await db.HashSetAsync(currencyKey, hashEntries);
                await db.KeyExpireAsync(currencyKey, PLAYER_CACHE_TTL);
            }

            return currenciesFromDb;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving currencies for player {playerId}");

            // 缓存出错时直接从MongoDB获取
            return await _mongoRepo.GetPlayerCurrenciesAsync(playerId);
        }
    }

    public async Task<Dictionary<string, Guid>> GetPlayerEquippedItemsAsync(string playerId)
    {
        // 这个信息包含在玩家数据中，所以直接获取玩家数据
        var player = await GetPlayerAsync(playerId);
        return player?.EquippedItems ?? new Dictionary<string, Guid>();
    }

    public async Task<List<Player>> GetPlayersAsync(IEnumerable<string> playerIds)
    {
        // 对于批量查询，使用管道优化
        var db = _redis.GetDatabase();
        var batch = db.CreateBatch();

        // 准备查询任务
        var playerTasks = playerIds
            .Select(id => new
            {
                PlayerId = id,
                Task = batch.StringGetAsync(GetPlayerKey(id))
            })
            .ToList();

        // 执行批量查询
        batch.Execute();

        // 收集结果
        var cachedPlayers = new List<Player>();
        var missingPlayerIds = new List<string>();

        foreach (var item in playerTasks)
        {
            var result = await item.Task;
            if (result.HasValue)
            {
                cachedPlayers.Add(_serializer.Deserialize<Player>(result));
            }
            else
            {
                missingPlayerIds.Add(item.PlayerId);
            }
        }

        // 获取缓存中缺失的玩家
        if (missingPlayerIds.Count > 0)
        {
            var missingPlayers = await _mongoRepo.GetPlayersAsync(missingPlayerIds);

            // 缓存这些玩家
            foreach (var player in missingPlayers)
            {
                await CachePlayerAsync(player);
            }

            cachedPlayers.AddRange(missingPlayers);
        }

        return cachedPlayers;
    }

    public async Task<List<Player>> QueryPlayersByLevelRangeAsync(int minLevel, int maxLevel, int pageSize, int pageNumber)
    {
        // 复杂查询直接委托给MongoDB处理
        return await _mongoRepo.QueryPlayersByLevelRangeAsync(minLevel, maxLevel, pageSize, pageNumber);
    }

    public async Task<List<Player>> GetPlayersByGuildIdAsync(string guildId)
    {
        // 按公会查询也直接委托给MongoDB
        return await _mongoRepo.GetPlayersByGuildIdAsync(guildId);
    }

    public async Task<PlayerStatistics> GetPlayerStatisticsAsync(string playerId)
    {
        // 统计数据可能经常变化且较大，直接从MongoDB获取
        return await _mongoRepo.GetPlayerStatisticsAsync(playerId);
    }

    public async Task LogPlayerActionAsync(PlayerActionLog actionLog)
    {
        // 日志直接写入MongoDB
        await _mongoRepo.LogPlayerActionAsync(actionLog);
    }

    public async Task<bool> TryLockPlayerAccountAsync(string playerId, string reason, TimeSpan duration)
    {
        // 1. 先在MongoDB中锁定
        bool success = await _mongoRepo.TryLockPlayerAccountAsync(playerId, reason, duration);

        if (success)
        {
            // 2. 在Redis中设置锁标记
            var db = _redis.GetDatabase();
            string lockKey = GetPlayerLockKey(playerId);

            await db.StringSetAsync(
                lockKey,
                $"{reason}|{DateTime.UtcNow.Add(duration).Ticks}",
                duration);

            // 3. 无效化玩家缓存
            await InvalidatePlayerCacheAsync(playerId);
        }

        return success;
    }

    public async Task UnlockPlayerAccountAsync(string playerId)
    {
        // 1. 先在MongoDB中解锁
        await _mongoRepo.UnlockPlayerAccountAsync(playerId);

        // 2. 移除Redis中的锁标记
        var db = _redis.GetDatabase();
        string lockKey = GetPlayerLockKey(playerId);
        await db.KeyDeleteAsync(lockKey);

        // 3. 无效化玩家缓存
        await InvalidatePlayerCacheAsync(playerId);
    }

    // 缓存玩家数据
    private async Task CachePlayerAsync(Player player)
    {
        try
        {
            var db = _redis.GetDatabase();
            string playerKey = GetPlayerKey(player.Id);

            string serialized = _serializer.Serialize(player);
            await db.StringSetAsync(playerKey, serialized, PLAYER_CACHE_TTL);

            // 同时更新货币缓存
            string currencyKey = GetCurrencyKey(player.Id);
            var currencyEntries = new List<HashEntry>
            {
                new HashEntry("Gold", player.Gold),
                new HashEntry("Diamonds", player.Diamonds),
                new HashEntry("Energy", player.Energy),
                new HashEntry("EnhanceStones", player.EnhanceStones),
                new HashEntry("ProtectionScrolls", player.ProtectionScrolls)
            };

            // 添加自定义货币
            foreach (var currency in player.Currencies)
            {
                currencyEntries.Add(new HashEntry(currency.Key, currency.Value));
            }

            await db.HashSetAsync(currencyKey, currencyEntries.ToArray());
            await db.KeyExpireAsync(currencyKey, PLAYER_CACHE_TTL);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error caching player data for {player.Id}");
            // 缓存失败不抛出异常
        }
    }

    // 无效化玩家缓存
    private async Task InvalidatePlayerCacheAsync(string playerId)
    {
        try
        {
            var db = _redis.GetDatabase();

            // 移除相关的所有缓存
            var keysToDelete = new[]
            {
                GetPlayerKey(playerId),
                GetInventoryKey(playerId),
                GetCurrencyKey(playerId)
            };

            await db.KeyDeleteAsync(keysToDelete.Select(k => (RedisKey)k).ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error invalidating cache for player {playerId}");
            // 缓存失败不抛出异常
        }
    }

    // 获取Redis键名
    private string GetPlayerKey(string playerId) => $"{PLAYER_KEY_PREFIX}{playerId}";
    private string GetInventoryKey(string playerId) => $"{INVENTORY_KEY_PREFIX}{playerId}";
    private string GetPlayerLockKey(string playerId) => $"{PLAYER_LOCK_KEY_PREFIX}{playerId}";
    private string GetCurrencyKey(string playerId) => $"{CURRENCY_KEY_PREFIX}{playerId}";
}
