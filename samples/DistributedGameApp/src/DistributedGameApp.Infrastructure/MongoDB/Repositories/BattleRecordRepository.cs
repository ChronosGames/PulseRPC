using DistributedGameApp.Shared.Domain.Battles;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 战斗记录（用于数据库存储）
/// </summary>
public class BattleRecord
{
    [BsonId]
    // [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("roomId")]
    public string RoomId { get; set; } = string.Empty;

    [BsonElement("battleType")]
    public string BattleType { get; set; } = string.Empty;

    [BsonElement("startedAt")]
    public DateTime StartedAt { get; set; }

    [BsonElement("finishedAt")]
    public DateTime FinishedAt { get; set; }

    [BsonElement("durationSeconds")]
    public int DurationSeconds { get; set; }

    [BsonElement("winnerTeamId")]
    public int WinnerTeamId { get; set; }

    [BsonElement("players")]
    public List<BattlePlayerRecord> Players { get; set; } = new();
}

/// <summary>
/// 战斗玩家记录
/// </summary>
public class BattlePlayerRecord
{
    [BsonElement("playerId")]
    public string PlayerId { get; set; } = string.Empty;

    [BsonElement("characterId")]
    public string CharacterId { get; set; } = string.Empty;

    [BsonElement("characterName")]
    public string CharacterName { get; set; } = string.Empty;

    [BsonElement("teamId")]
    public int TeamId { get; set; }

    [BsonElement("isWinner")]
    public bool IsWinner { get; set; }

    [BsonElement("damageDealt")]
    public long DamageDealt { get; set; }

    [BsonElement("damageTaken")]
    public long DamageTaken { get; set; }

    [BsonElement("kills")]
    public int Kills { get; set; }

    [BsonElement("deaths")]
    public int Deaths { get; set; }

    [BsonElement("expGained")]
    public long ExpGained { get; set; }

    [BsonElement("goldGained")]
    public long GoldGained { get; set; }
}

/// <summary>
/// 战斗记录 Repository
/// </summary>
public class BattleRecordRepository : MongoRepository<BattleRecord>
{
    public BattleRecordRepository(MongoDbContext context)
        : base(context.BattlesDatabase, "battle_records")
    {
    }

    /// <summary>
    /// 根据房间ID获取战斗记录
    /// </summary>
    public async Task<BattleRecord?> GetByRoomIdAsync(string roomId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(r => r.RoomId == roomId, cancellationToken);
    }

    /// <summary>
    /// 获取玩家的战斗记录
    /// </summary>
    public async Task<List<BattleRecord>> GetPlayerRecordsAsync(
        string playerId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BattleRecord>.Filter.ElemMatch(
            r => r.Players,
            p => p.PlayerId == playerId
        );

        return await Collection
            .Find(filter)
            .SortByDescending(r => r.FinishedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取玩家的胜率统计
    /// </summary>
    public async Task<(int TotalGames, int Wins, int Losses, double WinRate)> GetPlayerStatsAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BattleRecord>.Filter.ElemMatch(
            r => r.Players,
            p => p.PlayerId == playerId
        );

        var records = await Collection.Find(filter).ToListAsync(cancellationToken);
        var totalGames = records.Count;
        var wins = records.Count(r => r.Players.Any(p => p.PlayerId == playerId && p.IsWinner));
        var losses = totalGames - wins;
        var winRate = totalGames > 0 ? (double)wins / totalGames : 0;

        return (totalGames, wins, losses, winRate);
    }

    /// <summary>
    /// 保存战斗结果
    /// </summary>
    public async Task SaveBattleResultAsync(BattleResult result, string battleType, CancellationToken cancellationToken = default)
    {
        var record = new BattleRecord
        {
            RoomId = result.RoomId,
            BattleType = battleType,
            StartedAt = DateTime.UtcNow.AddSeconds(-result.DurationSeconds),
            FinishedAt = DateTime.UtcNow,
            DurationSeconds = result.DurationSeconds,
            WinnerTeamId = result.WinnerTeamId,
            Players = result.PlayerResults.Select(p => new BattlePlayerRecord
            {
                PlayerId = p.PlayerId,
                CharacterId = p.CharacterId,
                TeamId = p.TeamId,
                IsWinner = p.IsWinner,
                DamageDealt = p.DamageDealt,
                DamageTaken = p.DamageTaken,
                Kills = p.Kills,
                Deaths = p.Deaths,
                ExpGained = p.ExpGained,
                GoldGained = p.GoldGained
            }).ToList()
        };

        await InsertAsync(record, cancellationToken);
    }

    /// <summary>
    /// 获取最近的战斗记录
    /// </summary>
    public async Task<List<BattleRecord>> GetRecentRecordsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await Collection
            .Find(_ => true)
            .SortByDescending(r => r.FinishedAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除旧的战斗记录
    /// </summary>
    public async Task<long> DeleteOldRecordsAsync(DateTime beforeDate, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(r => r.FinishedAt < beforeDate, cancellationToken);
    }
}
