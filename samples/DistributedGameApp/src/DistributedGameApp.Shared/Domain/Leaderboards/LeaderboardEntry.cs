using System;
using MemoryPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedGameApp.Shared.Domain.Leaderboards;

/// <summary>
/// 排行榜条目
/// </summary>
[MemoryPackable]
public partial class LeaderboardEntry
{
    /// <summary>
    /// MongoDB 主键
    /// </summary>
    [BsonId]
    // [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 排行榜类型（Level, PvP, Guild）
    /// </summary>
    [BsonElement("leaderboardType")]
    public string LeaderboardType { get; set; } = string.Empty;

    /// <summary>
    /// 赛季ID（可选，用于赛季排行榜）
    /// </summary>
    [BsonElement("seasonId")]
    public string? SeasonId { get; set; }

    /// <summary>
    /// 用户ID 或 帮派ID
    /// </summary>
    [BsonElement("entityId")]
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称
    /// </summary>
    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 分数
    /// </summary>
    [BsonElement("score")]
    public long Score { get; set; }

    /// <summary>
    /// 排名
    /// </summary>
    [BsonElement("rank")]
    public int Rank { get; set; }

    /// <summary>
    /// 额外数据（JSON）
    /// </summary>
    [BsonElement("extraData")]
    public string ExtraData { get; set; } = string.Empty;

    /// <summary>
    /// 更新时间
    /// </summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 获取排行榜请求
/// </summary>
[MemoryPackable]
public partial class GetLeaderboardRequest
{
    /// <summary>
    /// 排行榜类型
    /// </summary>
    public string LeaderboardType { get; set; } = string.Empty;

    /// <summary>
    /// 赛季ID（可选）
    /// </summary>
    public string? SeasonId { get; set; }

    /// <summary>
    /// 起始排名
    /// </summary>
    public int StartRank { get; set; } = 1;

    /// <summary>
    /// 数量
    /// </summary>
    public int Count { get; set; } = 100;
}
