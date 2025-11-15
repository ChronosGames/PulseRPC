using DistributedGameApp.Shared.Domain.Guilds;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 帮派 Repository
/// </summary>
public class GuildRepository(MongoDbContext context) : MongoRepository<Guild>(context.GuildsDatabase, "guilds")
{
    /// <summary>
    /// 初始化索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // GuildId 唯一索引
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<Guild>(
            Builders<Guild>.IndexKeys.Ascending(g => g.GuildId),
            new CreateIndexOptions { Unique = true }));

        // 公会名称索引（用于搜索）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<Guild>(
            Builders<Guild>.IndexKeys.Ascending(g => g.Name)));

        // 会长ID索引（查询会长的公会）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<Guild>(
            Builders<Guild>.IndexKeys.Ascending(g => g.LeaderId)));

        // Tag 索引（用于搜索）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<Guild>(
            Builders<Guild>.IndexKeys.Ascending(g => g.Tag)));
    }

    /// <summary>
    /// 根据帮派ID获取帮派
    /// </summary>
    public async Task<Guild?> GetByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(g => g.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 根据帮派名称获取帮派
    /// </summary>
    public async Task<Guild?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(g => g.Name == name, cancellationToken);
    }

    /// <summary>
    /// 检查帮派名称是否已存在
    /// </summary>
    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(g => g.Name == name, cancellationToken);
    }

    /// <summary>
    /// 检查帮派标签是否已存在
    /// </summary>
    public async Task<bool> TagExistsAsync(string tag, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(g => g.Tag == tag, cancellationToken);
    }

    /// <summary>
    /// 更新帮派成员数
    /// </summary>
    public async Task<bool> UpdateMemberCountAsync(string guildId, int memberCount, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Guild>.Filter.Eq(g => g.GuildId, guildId);
        var update = Builders<Guild>.Update.Set(g => g.MemberCount, memberCount);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 增加帮派成员数
    /// </summary>
    public async Task<bool> IncrementMemberCountAsync(string guildId, int increment = 1, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Guild>.Filter.Eq(g => g.GuildId, guildId);
        var update = Builders<Guild>.Update.Inc(g => g.MemberCount, increment);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 更新帮派等级和经验
    /// </summary>
    public async Task<bool> UpdateLevelAsync(string guildId, int level, long exp, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Guild>.Filter.Eq(g => g.GuildId, guildId);
        var update = Builders<Guild>.Update
            .Set(g => g.Level, level)
            .Set(g => g.Exp, exp);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 搜索帮派
    /// </summary>
    public async Task<List<Guild>> SearchAsync(string keyword, int limit = 20, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Guild>.Filter.Or(
            Builders<Guild>.Filter.Regex(g => g.Name, new BsonRegularExpression(keyword, "i")),
            Builders<Guild>.Filter.Regex(g => g.Tag, new BsonRegularExpression(keyword, "i"))
        );

        return await Collection
            .Find(filter)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 分页获取帮派列表（带排序）
    /// </summary>
    public async Task<List<Guild>> GetGuildsPagedAsync(int skip, int limit, string sortBy = "CreatedAt", bool descending = true, CancellationToken cancellationToken = default)
    {
        var sortDefinition = descending
            ? Builders<Guild>.Sort.Descending(sortBy)
            : Builders<Guild>.Sort.Ascending(sortBy);

        return await Collection
            .Find(_ => true)
            .Sort(sortDefinition)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除帮派（按 GuildId）
    /// </summary>
    public async Task<bool> DeleteByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        var result = await Collection.DeleteOneAsync(g => g.GuildId == guildId, cancellationToken);
        return result.DeletedCount > 0;
    }
}

/// <summary>
/// 帮派成员 Repository
/// </summary>
public class GuildMemberRepository : MongoRepository<GuildMember>
{
    public GuildMemberRepository(MongoDbContext context)
        : base(context.GuildsDatabase, "guild_members")
    {
    }

    /// <summary>
    /// 初始化索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // GuildId 索引（查询公会的所有成员）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<GuildMember>(
            Builders<GuildMember>.IndexKeys.Ascending(m => m.GuildId)));

        // UserId 唯一索引（一个用户只能加入一个公会）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<GuildMember>(
            Builders<GuildMember>.IndexKeys.Ascending(m => m.UserId),
            new CreateIndexOptions { Unique = true }));
    }

    /// <summary>
    /// 根据帮派ID获取所有成员
    /// </summary>
    public async Task<List<GuildMember>> GetByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await FindManyAsync(m => m.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 根据用户ID获取帮派成员信息
    /// </summary>
    public async Task<GuildMember?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(m => m.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// 根据帮派ID和用户ID获取成员
    /// </summary>
    public async Task<GuildMember?> GetMemberAsync(string guildId, string userId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(m => m.GuildId == guildId && m.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// 检查用户是否在帮派中
    /// </summary>
    public async Task<bool> IsMemberAsync(string guildId, string userId, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(m => m.GuildId == guildId && m.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// 更新成员职位
    /// </summary>
    public async Task<bool> UpdateRoleAsync(string guildId, string userId, string role, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildMember>.Filter.And(
            Builders<GuildMember>.Filter.Eq(m => m.GuildId, guildId),
            Builders<GuildMember>.Filter.Eq(m => m.UserId, userId)
        );
        var update = Builders<GuildMember>.Update.Set(m => m.Role, role);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 增加成员贡献度
    /// </summary>
    public async Task<bool> AddContributionAsync(string guildId, string userId, long amount, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildMember>.Filter.And(
            Builders<GuildMember>.Filter.Eq(m => m.GuildId, guildId),
            Builders<GuildMember>.Filter.Eq(m => m.UserId, userId)
        );
        var update = Builders<GuildMember>.Update.Inc(m => m.Contribution, amount);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 删除帮派的所有成员
    /// </summary>
    public async Task<long> DeleteByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(m => m.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 获取贡献度排行榜
    /// </summary>
    public async Task<List<GuildMember>> GetContributionRankingAsync(string guildId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildMember>.Filter.Eq(m => m.GuildId, guildId);
        var sort = Builders<GuildMember>.Sort.Descending(m => m.Contribution);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除成员（按 UserId）
    /// </summary>
    public async Task<bool> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = await Collection.DeleteOneAsync(m => m.UserId == userId, cancellationToken);
        return result.DeletedCount > 0;
    }
}

/// <summary>
/// 帮派加入申请 Repository
/// </summary>
public class GuildJoinRequestRepository : MongoRepository<JoinRequest>
{
    public GuildJoinRequestRepository(MongoDbContext context)
        : base(context.GuildsDatabase, "guild_join_requests")
    {
    }

    /// <summary>
    /// 初始化索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // GuildId + UserId 复合索引
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<JoinRequest>(
            Builders<JoinRequest>.IndexKeys
                .Ascending(r => r.GuildId)
                .Ascending(r => r.UserId)));

        // GuildId + Status 复合索引（查询待处理申请）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<JoinRequest>(
            Builders<JoinRequest>.IndexKeys
                .Ascending(r => r.GuildId)
                .Ascending(r => r.Status)));
    }

    /// <summary>
    /// 获取帮派的所有待处理申请
    /// </summary>
    public async Task<List<JoinRequest>> GetPendingRequestsAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await FindManyAsync(r => r.GuildId == guildId && r.Status == "Pending", cancellationToken);
    }

    /// <summary>
    /// 获取特定申请
    /// </summary>
    public async Task<JoinRequest?> GetRequestAsync(string guildId, string userId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(r => r.GuildId == guildId && r.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// 检查是否存在待处理的申请
    /// </summary>
    public async Task<bool> HasPendingRequestAsync(string guildId, string userId, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(r => r.GuildId == guildId && r.UserId == userId && r.Status == "Pending", cancellationToken);
    }

    /// <summary>
    /// 更新申请状态
    /// </summary>
    public async Task<bool> UpdateStatusAsync(string guildId, string userId, string status, CancellationToken cancellationToken = default)
    {
        var filter = Builders<JoinRequest>.Filter.And(
            Builders<JoinRequest>.Filter.Eq(r => r.GuildId, guildId),
            Builders<JoinRequest>.Filter.Eq(r => r.UserId, userId)
        );
        var update = Builders<JoinRequest>.Update
            .Set(r => r.Status, status)
            .Set(r => r.UpdatedAt, DateTime.UtcNow);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 删除帮派的所有申请
    /// </summary>
    public async Task<long> DeleteByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(r => r.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 删除用户的所有申请
    /// </summary>
    public async Task<long> DeleteByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(r => r.UserId == userId, cancellationToken);
    }
}

/// <summary>
/// 帮派消息 Repository
/// </summary>
public class GuildMessageRepository : MongoRepository<GuildMessage>
{
    public GuildMessageRepository(MongoDbContext context)
        : base(context.GuildsDatabase, "guild_messages")
    {
    }

    /// <summary>
    /// 初始化索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // GuildId + Timestamp 复合索引（倒序，用于获取最新消息）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<GuildMessage>(
            Builders<GuildMessage>.IndexKeys
                .Ascending(m => m.GuildId)
                .Descending(m => m.Timestamp)));
    }

    /// <summary>
    /// 获取聊天历史
    /// </summary>
    public async Task<List<GuildMessage>> GetChatHistoryAsync(string guildId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildMessage>.Filter.Eq(m => m.GuildId, guildId);
        var sort = Builders<GuildMessage>.Sort.Descending(m => m.Timestamp);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取指定时间之后的消息
    /// </summary>
    public async Task<List<GuildMessage>> GetMessagesAfterAsync(string guildId, DateTime after, int limit = 100, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildMessage>.Filter.And(
            Builders<GuildMessage>.Filter.Eq(m => m.GuildId, guildId),
            Builders<GuildMessage>.Filter.Gt(m => m.Timestamp, after)
        );
        var sort = Builders<GuildMessage>.Sort.Ascending(m => m.Timestamp);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除帮派的所有消息
    /// </summary>
    public async Task<long> DeleteByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(m => m.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 删除旧消息（保留策略）
    /// </summary>
    public async Task<long> DeleteOldMessagesAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(m => m.Timestamp < before, cancellationToken);
    }
}

/// <summary>
/// 帮派活动 Repository
/// </summary>
public class GuildActivityRepository : MongoRepository<GuildActivity>
{
    public GuildActivityRepository(MongoDbContext context)
        : base(context.GuildsDatabase, "guild_activities")
    {
    }

    /// <summary>
    /// 初始化索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // GuildId + Timestamp 复合索引（倒序，用于获取最新活动）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<GuildActivity>(
            Builders<GuildActivity>.IndexKeys
                .Ascending(a => a.GuildId)
                .Descending(a => a.Timestamp)));

        // GuildId + ActivityType 复合索引（按类型查询活动）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<GuildActivity>(
            Builders<GuildActivity>.IndexKeys
                .Ascending(a => a.GuildId)
                .Ascending(a => a.ActivityType)));
    }

    /// <summary>
    /// 获取帮派活动记录
    /// </summary>
    public async Task<List<GuildActivity>> GetActivitiesAsync(string guildId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildActivity>.Filter.Eq(a => a.GuildId, guildId);
        var sort = Builders<GuildActivity>.Sort.Descending(a => a.Timestamp);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取指定类型的活动
    /// </summary>
    public async Task<List<GuildActivity>> GetActivitiesByTypeAsync(string guildId, string activityType, int limit = 50, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildActivity>.Filter.And(
            Builders<GuildActivity>.Filter.Eq(a => a.GuildId, guildId),
            Builders<GuildActivity>.Filter.Eq(a => a.ActivityType, activityType)
        );
        var sort = Builders<GuildActivity>.Sort.Descending(a => a.Timestamp);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取指定时间范围的活动
    /// </summary>
    public async Task<List<GuildActivity>> GetActivitiesInRangeAsync(string guildId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildActivity>.Filter.And(
            Builders<GuildActivity>.Filter.Eq(a => a.GuildId, guildId),
            Builders<GuildActivity>.Filter.Gte(a => a.Timestamp, startTime),
            Builders<GuildActivity>.Filter.Lte(a => a.Timestamp, endTime)
        );
        var sort = Builders<GuildActivity>.Sort.Descending(a => a.Timestamp);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除帮派的所有活动
    /// </summary>
    public async Task<long> DeleteByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(a => a.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 删除旧活动记录（保留策略）
    /// </summary>
    public async Task<long> DeleteOldActivitiesAsync(DateTime before, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(a => a.Timestamp < before, cancellationToken);
    }
}

/// <summary>
/// 帮派公告 Repository
/// </summary>
public class GuildAnnouncementRepository : MongoRepository<GuildAnnouncement>
{
    public GuildAnnouncementRepository(MongoDbContext context)
        : base(context.GuildsDatabase, "guild_announcements")
    {
    }

    /// <summary>
    /// 初始化索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // GuildId + IsPinned + CreatedAt 复合索引（置顶优先，按时间倒序）
        await Collection.Indexes.CreateOneAsync(new CreateIndexModel<GuildAnnouncement>(
            Builders<GuildAnnouncement>.IndexKeys
                .Ascending(a => a.GuildId)
                .Descending(a => a.IsPinned)
                .Descending(a => a.CreatedAt)));
    }

    /// <summary>
    /// 获取帮派公告列表
    /// </summary>
    public async Task<List<GuildAnnouncement>> GetAnnouncementsAsync(string guildId, int limit = 20, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildAnnouncement>.Filter.Eq(a => a.GuildId, guildId);
        var sort = Builders<GuildAnnouncement>.Sort
            .Descending(a => a.IsPinned)
            .Descending(a => a.CreatedAt);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取置顶公告
    /// </summary>
    public async Task<List<GuildAnnouncement>> GetPinnedAnnouncementsAsync(string guildId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildAnnouncement>.Filter.And(
            Builders<GuildAnnouncement>.Filter.Eq(a => a.GuildId, guildId),
            Builders<GuildAnnouncement>.Filter.Eq(a => a.IsPinned, true)
        );
        var sort = Builders<GuildAnnouncement>.Sort.Descending(a => a.CreatedAt);

        return await Collection
            .Find(filter)
            .Sort(sort)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 根据ID获取公告
    /// </summary>
    public async Task<GuildAnnouncement?> GetByAnnouncementIdAsync(string announcementId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(a => a.Id == announcementId, cancellationToken);
    }

    /// <summary>
    /// 设置公告置顶状态
    /// </summary>
    public async Task<bool> SetPinnedAsync(string announcementId, bool isPinned, CancellationToken cancellationToken = default)
    {
        var filter = Builders<GuildAnnouncement>.Filter.Eq(a => a.Id, announcementId);
        var update = Builders<GuildAnnouncement>.Update.Set(a => a.IsPinned, isPinned);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 删除帮派的所有公告
    /// </summary>
    public async Task<long> DeleteByGuildIdAsync(string guildId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(a => a.GuildId == guildId, cancellationToken);
    }

    /// <summary>
    /// 删除指定公告
    /// </summary>
    public Task<bool> DeleteByAnnouncementIdAsync(string announcementId, CancellationToken cancellationToken = default)
    {
        return DeleteAsync(announcementId, cancellationToken);
    }
}
