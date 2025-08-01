using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using GameApp.Shared.Services;

namespace GameApp.BattleServer.Services;

/// <summary>
/// 战斗数据仓储实现
/// </summary>
public class BattleRepository : IBattleRepository
{
    private readonly IMongoCollection<BattleInfo> _battleCollection;
    private readonly IMongoCollection<BattleStatistics> _statisticsCollection;
    private readonly ILogger<BattleRepository> _logger;

    public BattleRepository(IMongoDatabase database, ILogger<BattleRepository> logger)
    {
        _battleCollection = database.GetCollection<BattleInfo>("battles");
        _statisticsCollection = database.GetCollection<BattleStatistics>("battle_statistics");
        _logger = logger;
    }

    /// <summary>
    /// 保存战斗信息
    /// </summary>
    public async Task SaveBattleInfoAsync(BattleInfo battleInfo)
    {
        try
        {
            var filter = Builders<BattleInfo>.Filter.Eq(b => b.BattleId, battleInfo.BattleId);
            var options = new ReplaceOptions { IsUpsert = true };

            await _battleCollection.ReplaceOneAsync(filter, battleInfo, options);

            _logger.LogDebug("Saved battle info: {BattleId}", battleInfo.BattleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving battle info: {BattleId}", battleInfo.BattleId);
            throw;
        }
    }

    /// <summary>
    /// 获取战斗信息
    /// </summary>
    public async Task<BattleInfo?> GetBattleInfoAsync(string battleId)
    {
        try
        {
            var filter = Builders<BattleInfo>.Filter.Eq(b => b.BattleId, battleId);
            var battle = await _battleCollection.Find(filter).FirstOrDefaultAsync();

            if (battle != null)
            {
                _logger.LogDebug("Retrieved battle info: {BattleId}", battleId);
            }

            return battle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting battle info: {BattleId}", battleId);
            throw;
        }
    }

    /// <summary>
    /// 保存战斗统计
    /// </summary>
    public async Task SaveBattleStatisticsAsync(string battleId, BattleStatistics statistics)
    {
        try
        {
            // 添加战斗ID到统计对象中
            var filter = Builders<BattleStatistics>.Filter.Eq(s => s.BattleId, statistics.BattleId);
            var options = new ReplaceOptions { IsUpsert = true };

            await _statisticsCollection.ReplaceOneAsync(filter, statistics, options);

            _logger.LogDebug("Saved battle statistics: {BattleId}", battleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving battle statistics: {BattleId}", battleId);
            throw;
        }
    }

    /// <summary>
    /// 获取玩家战斗历史
    /// </summary>
    public async Task<List<BattleInfo>> GetPlayerBattleHistoryAsync(int playerId, int limit = 10)
    {
        try
        {
            var filter = Builders<BattleInfo>.Filter.ElemMatch(b => b.Players, p => p.PlayerId == playerId);
            var sort = Builders<BattleInfo>.Sort.Descending(b => b.StartTime);

            var battles = await _battleCollection
                .Find(filter)
                .Sort(sort)
                .Limit(limit)
                .ToListAsync();

            _logger.LogDebug("Retrieved battle history for player {PlayerId}: {Count} battles",
                playerId, battles.Count);

            return battles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player battle history: {PlayerId}", playerId);
            throw;
        }
    }
}

/// <summary>
/// 战斗统计文档（用于MongoDB存储）
/// </summary>
public class BattleStatisticsDocument
{
    public string BattleId { get; set; } = string.Empty;
    public BattleStatistics Statistics { get; set; } = new();
}
