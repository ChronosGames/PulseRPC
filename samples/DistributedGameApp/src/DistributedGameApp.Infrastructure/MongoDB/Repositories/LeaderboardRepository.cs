using DistributedGameApp.Shared.Domain.Leaderboards;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 排行榜 Repository
/// </summary>
public class LeaderboardRepository : MongoRepository<LeaderboardEntry>
{
    public LeaderboardRepository(MongoDbContext context)
        : base(context.LeaderboardsDatabase, "leaderboards")
    {
    }

    /// <summary>
    /// 获取排行榜
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(
        string leaderboardType,
        string? seasonId = null,
        int startRank = 1,
        int count = 100,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LeaderboardEntry>.Filter;
        var filter = filterBuilder.Eq(e => e.LeaderboardType, leaderboardType);

        if (!string.IsNullOrEmpty(seasonId))
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(e => e.SeasonId, seasonId));
        }

        return await Collection
            .Find(filter)
            .SortBy(e => e.Rank)
            .Skip(startRank - 1)
            .Limit(count)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取实体的排名
    /// </summary>
    public async Task<LeaderboardEntry?> GetRankAsync(
        string leaderboardType,
        string entityId,
        string? seasonId = null,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LeaderboardEntry>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(e => e.LeaderboardType, leaderboardType),
            filterBuilder.Eq(e => e.EntityId, entityId)
        );

        if (!string.IsNullOrEmpty(seasonId))
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(e => e.SeasonId, seasonId));
        }

        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 更新或插入排行榜条目
    /// </summary>
    public async Task<bool> UpsertEntryAsync(
        string leaderboardType,
        string entityId,
        string displayName,
        long score,
        string? seasonId = null,
        string extraData = "",
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LeaderboardEntry>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(e => e.LeaderboardType, leaderboardType),
            filterBuilder.Eq(e => e.EntityId, entityId)
        );

        if (!string.IsNullOrEmpty(seasonId))
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(e => e.SeasonId, seasonId));
        }

        var update = Builders<LeaderboardEntry>.Update
            .Set(e => e.DisplayName, displayName)
            .Set(e => e.Score, score)
            .Set(e => e.ExtraData, extraData)
            .Set(e => e.UpdatedAt, DateTime.UtcNow)
            .SetOnInsert(e => e.LeaderboardType, leaderboardType)
            .SetOnInsert(e => e.EntityId, entityId)
            .SetOnInsert(e => e.SeasonId, seasonId);

        var options = new UpdateOptions { IsUpsert = true };
        var result = await Collection.UpdateOneAsync(filter, update, options, cancellationToken);

        return result.ModifiedCount > 0 || result.UpsertedId != null;
    }

    /// <summary>
    /// 重新计算排名
    /// </summary>
    public async Task RecalculateRanksAsync(
        string leaderboardType,
        string? seasonId = null,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<LeaderboardEntry>.Filter;
        var filter = filterBuilder.Eq(e => e.LeaderboardType, leaderboardType);

        if (!string.IsNullOrEmpty(seasonId))
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(e => e.SeasonId, seasonId));
        }

        // 获取所有条目并按分数降序排序
        var entries = await Collection
            .Find(filter)
            .SortByDescending(e => e.Score)
            .ToListAsync(cancellationToken);

        // 批量更新排名
        var bulkOps = new List<WriteModel<LeaderboardEntry>>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var updateFilter = Builders<LeaderboardEntry>.Filter.Eq(e => e.Id, entry.Id);
            var update = Builders<LeaderboardEntry>.Update.Set(e => e.Rank, i + 1);
            bulkOps.Add(new UpdateOneModel<LeaderboardEntry>(updateFilter, update));
        }

        if (bulkOps.Count > 0)
        {
            await Collection.BulkWriteAsync(bulkOps, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// 获取周围的排名
    /// </summary>
    public async Task<List<LeaderboardEntry>> GetNearbyRanksAsync(
        string leaderboardType,
        int centerRank,
        int range = 5,
        string? seasonId = null,
        CancellationToken cancellationToken = default)
    {
        var startRank = Math.Max(1, centerRank - range);
        var count = range * 2 + 1;

        return await GetLeaderboardAsync(leaderboardType, seasonId, startRank, count, cancellationToken);
    }

    /// <summary>
    /// 删除赛季排行榜
    /// </summary>
    public async Task<long> DeleteSeasonAsync(string leaderboardType, string seasonId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(
            e => e.LeaderboardType == leaderboardType && e.SeasonId == seasonId,
            cancellationToken);
    }
}
