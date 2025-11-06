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
}
