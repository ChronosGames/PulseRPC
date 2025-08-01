using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using GameApp.Shared.Services;

namespace GameApp.GameServer.Repositories;

/// <summary>
/// 玩家数据仓储实现
/// </summary>
public class PlayerRepository : IPlayerRepository
{
    private readonly IMongoCollection<PlayerInfo> _playerCollection;
    private readonly IMongoCollection<PlayerStatistics> _statisticsCollection;
    private readonly ILogger<PlayerRepository> _logger;

    public PlayerRepository(IMongoDatabase database, ILogger<PlayerRepository> logger)
    {
        _playerCollection = database.GetCollection<PlayerInfo>("players");
        _statisticsCollection = database.GetCollection<PlayerStatistics>("player_statistics");
        _logger = logger;
    }

    /// <summary>
    /// 根据玩家ID获取玩家信息
    /// </summary>
    public async Task<PlayerInfo?> GetPlayerByIdAsync(int playerId)
    {
        try
        {
            var filter = Builders<PlayerInfo>.Filter.Eq(p => p.PlayerId, playerId);
            var player = await _playerCollection.Find(filter).FirstOrDefaultAsync();

            if (player != null)
            {
                _logger.LogDebug("Found player by ID: {PlayerId}", playerId);
            }

            return player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player by ID: {PlayerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// 根据用户ID获取玩家信息
    /// </summary>
    public async Task<PlayerInfo?> GetPlayerByUserIdAsync(int userId)
    {
        try
        {
            var filter = Builders<PlayerInfo>.Filter.Eq(p => p.UserId, userId);
            var player = await _playerCollection.Find(filter).FirstOrDefaultAsync();

            if (player != null)
            {
                _logger.LogDebug("Found player by UserID: {UserId} -> {PlayerId}", userId, player.PlayerId);
            }

            return player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player by UserID: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// 创建新玩家
    /// </summary>
    public async Task<PlayerInfo> CreatePlayerAsync(PlayerInfo player)
    {
        try
        {
            await _playerCollection.InsertOneAsync(player);

            // 创建初始统计数据
            var statistics = new PlayerStatistics
            {
                PlayerId = player.PlayerId,
                TotalPlayTime = 0,
                MonstersKilled = 0,
                QuestsCompleted = 0,
                DeathCount = 0,
                LoginDays = 1,
                LastActiveTime = DateTime.UtcNow
            };

            await _statisticsCollection.InsertOneAsync(statistics);

            _logger.LogInformation("Created new player: {CharacterName} (ID: {PlayerId})",
                player.CharacterName, player.PlayerId);

            return player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating player: {PlayerId}", player.PlayerId);
            throw;
        }
    }

    /// <summary>
    /// 保存玩家数据
    /// </summary>
    public async Task SavePlayerDataAsync(PlayerInfo player)
    {
        try
        {
            var filter = Builders<PlayerInfo>.Filter.Eq(p => p.PlayerId, player.PlayerId);
            var options = new ReplaceOptions { IsUpsert = true };

            await _playerCollection.ReplaceOneAsync(filter, player, options);

            _logger.LogDebug("Saved player data: {PlayerId}", player.PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving player data: {PlayerId}", player.PlayerId);
            throw;
        }
    }

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    public async Task UpdatePlayerPositionAsync(int playerId, PlayerPosition position)
    {
        try
        {
            var filter = Builders<PlayerInfo>.Filter.Eq(p => p.PlayerId, playerId);
            var update = Builders<PlayerInfo>.Update.Set(p => p.Position, position);

            await _playerCollection.UpdateOneAsync(filter, update);

            _logger.LogDebug("Updated player position: {PlayerId} -> ({X}, {Y}, {Z}) in {WorldId}",
                playerId, position.X, position.Y, position.Z, position.WorldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player position: {PlayerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// 更新玩家状态
    /// </summary>
    public async Task UpdatePlayerStatusAsync(int playerId, PlayerStatus status)
    {
        try
        {
            var filter = Builders<PlayerInfo>.Filter.Eq(p => p.PlayerId, playerId);
            var update = Builders<PlayerInfo>.Update.Set(p => p.Status, status);

            await _playerCollection.UpdateOneAsync(filter, update);

            _logger.LogDebug("Updated player status: {PlayerId} -> Health: {Health}/{MaxHealth}, Mana: {Mana}/{MaxMana}",
                playerId, status.Health, status.MaxHealth, status.Mana, status.MaxMana);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player status: {PlayerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// 更新最后活跃时间
    /// </summary>
    public async Task UpdateLastActiveTimeAsync(int playerId, DateTime lastActiveTime)
    {
        try
        {
            // 更新玩家表的活跃时间
            var playerFilter = Builders<PlayerInfo>.Filter.Eq(p => p.PlayerId, playerId);
            var playerUpdate = Builders<PlayerInfo>.Update.Set("LastActiveTime", lastActiveTime);
            await _playerCollection.UpdateOneAsync(playerFilter, playerUpdate);

            // 更新统计表的活跃时间
            var statsFilter = Builders<PlayerStatistics>.Filter.Eq(s => s.PlayerId, playerId);
            var statsUpdate = Builders<PlayerStatistics>.Update.Set(s => s.LastActiveTime, lastActiveTime);
            await _statisticsCollection.UpdateOneAsync(statsFilter, statsUpdate);

            _logger.LogDebug("Updated last active time: {PlayerId} -> {LastActiveTime}", playerId, lastActiveTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last active time: {PlayerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// 获取玩家统计信息
    /// </summary>
    public async Task<PlayerStatistics?> GetPlayerStatisticsAsync(int playerId)
    {
        try
        {
            var filter = Builders<PlayerStatistics>.Filter.Eq(s => s.PlayerId, playerId);
            var statistics = await _statisticsCollection.Find(filter).FirstOrDefaultAsync();

            if (statistics != null)
            {
                _logger.LogDebug("Found player statistics: {PlayerId}", playerId);
            }

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player statistics: {PlayerId}", playerId);
            throw;
        }
    }

    /// <summary>
    /// 更新玩家统计信息
    /// </summary>
    public async Task UpdatePlayerStatisticsAsync(PlayerStatistics statistics)
    {
        try
        {
            var filter = Builders<PlayerStatistics>.Filter.Eq(s => s.PlayerId, statistics.PlayerId);
            var options = new ReplaceOptions { IsUpsert = true };

            await _statisticsCollection.ReplaceOneAsync(filter, statistics, options);

            _logger.LogDebug("Updated player statistics: {PlayerId}", statistics.PlayerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player statistics: {PlayerId}", statistics.PlayerId);
            throw;
        }
    }
}
