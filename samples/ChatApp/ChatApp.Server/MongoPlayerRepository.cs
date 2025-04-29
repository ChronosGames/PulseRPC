using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace ChatApp.Server;

public interface IMongoPlayerRepository : IPlayerRepository;

/// <summary>
/// MongoDB实现的玩家仓储
/// </summary>
public class MongoPlayerRepository : IMongoPlayerRepository
{
    private readonly IMongoCollection<Player> _players;
    private readonly IMongoCollection<InventoryItem> _inventoryItems;
    private readonly IMongoCollection<PlayerStatistics> _playerStats;
    private readonly IMongoCollection<PlayerActionLog> _actionLogs;
    private readonly ILogger _logger;

    public MongoPlayerRepository(
        IMongoDatabase database,
        ILogger<MongoPlayerRepository> logger)
    {
        _players = database.GetCollection<Player>("players");
        _inventoryItems = database.GetCollection<InventoryItem>("inventory_items");
        _playerStats = database.GetCollection<PlayerStatistics>("player_statistics");
        _actionLogs = database.GetCollection<PlayerActionLog>("player_action_logs");
        _logger = logger;

        // 创建索引
        CreateIndexes();
    }

    private async void CreateIndexes()
    {
        // 玩家集合索引
        await _players.Indexes.CreateOneAsync(
            Builders<Player>.IndexKeys.Ascending(p => p.Username),
            new CreateIndexOptions { Unique = true });

        await _players.Indexes.CreateOneAsync(
            Builders<Player>.IndexKeys.Ascending(p => p.DisplayName));

        await _players.Indexes.CreateOneAsync(
            Builders<Player>.IndexKeys.Ascending(p => p.GuildId));

        await _players.Indexes.CreateOneAsync(
            Builders<Player>.IndexKeys.Ascending(p => p.Level));

        // 背包物品索引
        await _inventoryItems.Indexes.CreateOneAsync(
            Builders<InventoryItem>.IndexKeys.Ascending(i => i.Id),
            new CreateIndexOptions { Unique = true });

        await _inventoryItems.Indexes.CreateOneAsync(
            Builders<InventoryItem>.IndexKeys
                .Ascending("PlayerId")
                .Ascending("ItemId"));

        // 玩家操作日志索引
        await _actionLogs.Indexes.CreateOneAsync(
            Builders<PlayerActionLog>.IndexKeys
                .Ascending(l => l.PlayerId)
                .Descending(l => l.Timestamp));

        await _actionLogs.Indexes.CreateOneAsync(
            Builders<PlayerActionLog>.IndexKeys
                .Ascending(l => l.ActionType)
                .Descending(l => l.Timestamp));

        // TTL索引，自动删除7天以前的日志
        await _actionLogs.Indexes.CreateOneAsync(
            Builders<PlayerActionLog>.IndexKeys.Ascending(l => l.Timestamp),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) });
    }

    public async Task<Player> GetPlayerAsync(string playerId)
    {
        try
        {
            return await _players.Find(p => p.Id == playerId).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving player {playerId}");
            throw new RepositoryException("Failed to retrieve player data", ex);
        }
    }

    public async Task<Player> GetPlayerByNameAsync(string playerName)
    {
        try
        {
            var filter = Builders<Player>.Filter.Eq(p => p.Username, playerName) |
                         Builders<Player>.Filter.Eq(p => p.DisplayName, playerName);

            return await _players.Find(filter).FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving player by name {playerName}");
            throw new RepositoryException("Failed to retrieve player data by name", ex);
        }
    }

    public async Task<Player> CreatePlayerAsync(Player player)
    {
        try
        {
            // 设置创建时间和版本
            player.CreateTime = DateTime.UtcNow;
            player.LastLoginTime = DateTime.UtcNow;
            player.Version = 1;

            // 如果未指定ID，生成一个新ID
            if (string.IsNullOrEmpty(player.Id))
            {
                player.Id = Guid.NewGuid().ToString();
            }

            await _players.InsertOneAsync(player);

            // 创建默认的统计数据
            var stats = new PlayerStatistics
            {
                PlayerId = player.Id,
                FirstLoginTime = DateTime.UtcNow,
                TotalLogins = 1
            };

            await _playerStats.InsertOneAsync(stats);

            return player;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            _logger.LogWarning(ex, $"Attempted to create duplicate player: {player.Username}");
            throw new DuplicateEntityException($"Player with username {player.Username} already exists");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error creating player {player.Username}");
            throw new RepositoryException("Failed to create player", ex);
        }
    }

    public async Task<bool> UpdatePlayerAsync(Player player)
    {
        try
        {
            // 乐观锁更新
            player.Version++;

            var filter = Builders<Player>.Filter.And(
                Builders<Player>.Filter.Eq(p => p.Id, player.Id),
                Builders<Player>.Filter.Eq(p => p.Version, player.Version - 1)
            );

            var result = await _players.ReplaceOneAsync(filter, player);

            if (result.ModifiedCount == 0)
            {
                // 可能是版本冲突
                var existingPlayer = await GetPlayerAsync(player.Id);
                if (existingPlayer != null)
                {
                    throw new ConcurrencyException($"Player {player.Id} was modified by another process");
                }
                else
                {
                    throw new EntityNotFoundException($"Player {player.Id} not found");
                }
            }

            return true;
        }
        catch (ConcurrencyException)
        {
            // 重新抛出并发异常
            throw;
        }
        catch (EntityNotFoundException)
        {
            // 重新抛出实体未找到异常
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating player {player.Id}");
            throw new RepositoryException("Failed to update player", ex);
        }
    }

    public async Task<bool> UpdatePlayerFieldsAsync(string playerId, Dictionary<string, object> fields)
    {
        try
        {
            var updates = new List<UpdateDefinition<Player>>();

            // 为每个字段创建更新定义
            foreach (var field in fields)
            {
                updates.Add(Builders<Player>.Update.Set(field.Key, field.Value));
            }

            // 增加版本号
            updates.Add(Builders<Player>.Update.Inc(p => p.Version, 1));

            // 合并所有更新
            var combinedUpdate = Builders<Player>.Update.Combine(updates);

            // 执行更新
            var result = await _players.UpdateOneAsync(
                p => p.Id == playerId,
                combinedUpdate);

            if (result.ModifiedCount == 0)
            {
                throw new EntityNotFoundException($"Player {playerId} not found");
            }

            return true;
        }
        catch (EntityNotFoundException)
        {
            // 重新抛出实体未找到异常
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating player fields for {playerId}");
            throw new RepositoryException("Failed to update player fields", ex);
        }
    }

    public async Task<ResourceUpdateResult> UpdatePlayerResourcesAsync(string playerId, ResourceUpdateRequest request)
    {
        try
        {
            // 开始会话
            using var session = await _players.Database.Client.StartSessionAsync();
            session.StartTransaction();

            try
            {
                // 获取玩家数据
                var player = await _players.Find(session, p => p.Id == playerId).FirstOrDefaultAsync();

                if (player == null)
                {
                    throw new EntityNotFoundException($"Player {playerId} not found");
                }

                // 版本检查
                if (request.CheckOptimisticLock && player.Version != request.ExpectedVersion)
                {
                    throw new ConcurrencyException(
                        $"Expected version {request.ExpectedVersion}, but current version is {player.Version}");
                }

                // 准备更新列表和结果
                var updates = new List<UpdateDefinition<Player>>();
                var newValues = new Dictionary<string, long>();

                // 处理每个资源变更
                foreach (var change in request.ResourceChanges)
                {
                    string resourceField = GetResourceFieldName(change.Key);
                    long currentValue = GetResourceValue(player, change.Key);
                    long newValue = currentValue + change.Value;

                    // 检查负值
                    if (newValue < 0 && !request.AllowNegative)
                    {
                        await session.AbortTransactionAsync();
                        return new ResourceUpdateResult
                        {
                            Success = false,
                            ErrorCode = "InsufficientResources",
                            ErrorMessage =
                                $"Insufficient {change.Key}. Required: {Math.Abs(change.Value)}, Available: {currentValue}"
                        };
                    }

                    // 添加更新项
                    updates.Add(Builders<Player>.Update.Set(resourceField, newValue));
                    newValues[change.Key] = newValue;
                }

                // 增加版本号
                updates.Add(Builders<Player>.Update.Inc(p => p.Version, 1));

                // 合并所有更新
                var combinedUpdate = Builders<Player>.Update.Combine(updates);

                // 执行更新
                var filter = Builders<Player>.Filter.Eq(p => p.Id, playerId);
                if (request.CheckOptimisticLock)
                {
                    filter &= Builders<Player>.Filter.Eq(p => p.Version, request.ExpectedVersion);
                }

                var result = await _players.UpdateOneAsync(session, filter, combinedUpdate);

                if (result.ModifiedCount == 0)
                {
                    await session.AbortTransactionAsync();
                    throw new ConcurrencyException("Resource update failed due to concurrent modification");
                }

                // 记录操作日志
                await LogPlayerActionAsync(new PlayerActionLog
                {
                    Id = Guid.NewGuid(),
                    PlayerId = playerId,
                    ActionType = "ResourceUpdate",
                    ActionDetails = new Dictionary<string, object>
                    {
                        ["RequestId"] = request.RequestId,
                        ["Source"] = request.Source,
                        ["Changes"] = request.ResourceChanges
                    },
                    Timestamp = DateTime.UtcNow
                });

                // 提交事务
                await session.CommitTransactionAsync();

                return new ResourceUpdateResult
                {
                    Success = true,
                    NewValues = newValues,
                    NewVersion = player.Version + 1
                };
            }
            catch (Exception ex)
            {
                await session.AbortTransactionAsync();
                throw;
            }
        }
        catch (EntityNotFoundException)
        {
            // 重新抛出实体未找到异常
            throw;
        }
        catch (ConcurrencyException)
        {
            // 重新抛出并发异常
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating player resources for {playerId}");
            throw new RepositoryException("Failed to update player resources", ex);
        }
    }

    public async Task RecordPlayerLoginAsync(string playerId, string ipAddress, string deviceInfo)
    {
        try
        {
            // 更新最后登录时间和服务器
            var updates = Builders<Player>.Update
                .Set(p => p.LastLoginTime, DateTime.UtcNow)
                .Set(p => p.Status, PlayerStatus.Online);

            await _players.UpdateOneAsync(p => p.Id == playerId, updates);

            // 更新登录统计
            var statsUpdate = Builders<PlayerStatistics>.Update
                .Inc(s => s.TotalLogins, 1);

            await _playerStats.UpdateOneAsync(
                s => s.PlayerId == playerId,
                statsUpdate,
                new UpdateOptions { IsUpsert = true });

            // 记录登录日志
            await LogPlayerActionAsync(new PlayerActionLog
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                ActionType = "Login",
                ActionDetails = new Dictionary<string, object>
                {
                    ["IpAddress"] = ipAddress,
                    ["DeviceInfo"] = deviceInfo
                },
                ClientIp = ipAddress,
                DeviceInfo = deviceInfo,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error recording login for player {playerId}");
            // 不抛出异常，因为这是非关键操作
        }
    }

    public async Task<List<InventoryItem>> GetPlayerInventoryAsync(string playerId)
    {
        try
        {
            return await _inventoryItems
                .Find(i => i.PlayerId == playerId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving inventory for player {playerId}");
            throw new RepositoryException("Failed to retrieve player inventory", ex);
        }
    }

    public async Task<Dictionary<string, long>> GetPlayerCurrenciesAsync(string playerId)
    {
        try
        {
            var player = await GetPlayerAsync(playerId);

            if (player == null)
            {
                throw new EntityNotFoundException($"Player {playerId} not found");
            }

            // 合并内置货币和自定义货币
            var currencies = new Dictionary<string, long>
            {
                ["Gold"] = player.Gold,
                ["Diamonds"] = player.Diamonds,
                ["Energy"] = player.Energy,
                ["EnhanceStones"] = player.EnhanceStones,
                ["ProtectionScrolls"] = player.ProtectionScrolls
            };

            // 添加自定义货币
            foreach (var currency in player.Currencies)
            {
                currencies[currency.Key] = currency.Value;
            }

            return currencies;
        }
        catch (EntityNotFoundException)
        {
            // 重新抛出实体未找到异常
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving currencies for player {playerId}");
            throw new RepositoryException("Failed to retrieve player currencies", ex);
        }
    }

    public async Task<Dictionary<string, Guid>> GetPlayerEquippedItemsAsync(string playerId)
    {
        try
        {
            var player = await GetPlayerAsync(playerId);

            if (player == null)
            {
                throw new EntityNotFoundException($"Player {playerId} not found");
            }

            return player.EquippedItems;
        }
        catch (EntityNotFoundException)
        {
            // 重新抛出实体未找到异常
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving equipped items for player {playerId}");
            throw new RepositoryException("Failed to retrieve player equipped items", ex);
        }
    }

    public async Task<List<Player>> GetPlayersAsync(IEnumerable<string> playerIds)
    {
        try
        {
            return await _players
                .Find(p => playerIds.Contains(p.Id))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving multiple players");
            throw new RepositoryException("Failed to retrieve multiple players", ex);
        }
    }

    public async Task<List<Player>> QueryPlayersByLevelRangeAsync(int minLevel, int maxLevel, int pageSize,
        int pageNumber)
    {
        try
        {
            return await _players
                .Find(p => p.Level >= minLevel && p.Level <= maxLevel)
                .Sort(Builders<Player>.Sort.Descending(p => p.Level))
                .Skip(pageSize * (pageNumber - 1))
                .Limit(pageSize)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error querying players by level range ({minLevel}-{maxLevel})");
            throw new RepositoryException("Failed to query players by level range", ex);
        }
    }

    public async Task<List<Player>> GetPlayersByGuildIdAsync(string guildId)
    {
        try
        {
            return await _players
                .Find(p => p.GuildId == guildId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving players for guild {guildId}");
            throw new RepositoryException("Failed to retrieve guild members", ex);
        }
    }

    public async Task<PlayerStatistics> GetPlayerStatisticsAsync(string playerId)
    {
        try
        {
            var stats = await _playerStats
                .Find(s => s.PlayerId == playerId)
                .FirstOrDefaultAsync();

            // 如果不存在统计数据，创建一个默认的
            if (stats == null)
            {
                stats = new PlayerStatistics
                {
                    PlayerId = playerId,
                    FirstLoginTime = DateTime.UtcNow
                };

                await _playerStats.InsertOneAsync(stats);
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving statistics for player {playerId}");
            throw new RepositoryException("Failed to retrieve player statistics", ex);
        }
    }

    public async Task LogPlayerActionAsync(PlayerActionLog actionLog)
    {
        try
        {
            // 确保时间戳存在
            if (actionLog.Timestamp == default)
            {
                actionLog.Timestamp = DateTime.UtcNow;
            }

            // 确保ID存在
            if (actionLog.Id == Guid.Empty)
            {
                actionLog.Id = Guid.NewGuid();
            }

            await _actionLogs.InsertOneAsync(actionLog);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error logging player action for {actionLog.PlayerId}");
            // 不抛出异常，因为这是非关键操作
        }
    }

    public async Task<bool> TryLockPlayerAccountAsync(string playerId, string reason, TimeSpan duration)
    {
        try
        {
            var update = Builders<Player>.Update
                .Set(p => p.IsLocked, true)
                .Set(p => p.LockReason, reason)
                .Set(p => p.LockExpiry, DateTime.UtcNow.Add(duration));

            var result = await _players.UpdateOneAsync(
                p => p.Id == playerId && !p.IsLocked,
                update);

            if (result.ModifiedCount > 0)
            {
                // 记录操作日志
                await LogPlayerActionAsync(new PlayerActionLog
                {
                    PlayerId = playerId,
                    ActionType = "AccountLock",
                    ActionDetails = new Dictionary<string, object>
                    {
                        ["Reason"] = reason,
                        ["Duration"] = duration.ToString()
                    },
                    Timestamp = DateTime.UtcNow
                });

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error locking account for player {playerId}");
            throw new RepositoryException("Failed to lock player account", ex);
        }
    }

    public async Task UnlockPlayerAccountAsync(string playerId)
    {
        try
        {
            var update = Builders<Player>.Update
                .Set(p => p.IsLocked, false)
                .Set(p => p.LockExpiry, null)
                .Set(p => p.LockReason, null);

            await _players.UpdateOneAsync(
                p => p.Id == playerId,
                update);

            // 记录操作日志
            await LogPlayerActionAsync(new PlayerActionLog
            {
                PlayerId = playerId,
                ActionType = "AccountUnlock",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error unlocking account for player {playerId}");
            throw new RepositoryException("Failed to unlock player account", ex);
        }
    }

    // 辅助方法：获取资源字段名
    private string GetResourceFieldName(string resourceKey)
    {
        // 处理内置资源类型
        switch (resourceKey.ToLower())
        {
            case "gold":
                return nameof(Player.Gold);
            case "diamonds":
                return nameof(Player.Diamonds);
            case "energy":
                return nameof(Player.Energy);
            case "enhancestones":
                return nameof(Player.EnhanceStones);
            case "protectionscrolls":
                return nameof(Player.ProtectionScrolls);
            default:
                // 自定义货币存储在Currencies字典中
                return $"Currencies.{resourceKey}";
        }
    }

    // 辅助方法：获取资源当前值
    private long GetResourceValue(Player player, string resourceKey)
    {
        switch (resourceKey.ToLower())
        {
            case "gold":
                return player.Gold;
            case "diamonds":
                return player.Diamonds;
            case "energy":
                return player.Energy;
            case "enhancestones":
                return player.EnhanceStones;
            case "protectionscrolls":
                return player.ProtectionScrolls;
            default:
                // 尝试从自定义货币字典获取
                if (player.Currencies.TryGetValue(resourceKey, out long value))
                {
                    return value;
                }

                return 0; // 如果不存在则返回0
        }
    }
}
