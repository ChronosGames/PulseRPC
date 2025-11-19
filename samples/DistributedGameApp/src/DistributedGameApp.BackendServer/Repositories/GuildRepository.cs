using DistributedGameApp.Infrastructure.MongoDB;
using DistributedGameApp.Shared.Domain.Guilds;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DistributedGameApp.BackendServer.Repositories;

/// <summary>
/// 公会数据仓储
/// </summary>
public class GuildRepository
{
    private readonly IMongoCollection<Guild> _guilds;
    private readonly IMongoCollection<GuildMember> _members;
    private readonly IMongoCollection<JoinRequest> _joinRequests;
    private readonly IMongoCollection<GuildMessage> _messages;
    private readonly IMongoCollection<GuildActivity> _activities;
    private readonly IMongoCollection<GuildAnnouncement> _announcements;
    private readonly ILogger<GuildRepository> _logger;

    public GuildRepository(MongoDbContext dbContext, ILogger<GuildRepository> logger)
    {
        var database = dbContext.GuildsDatabase;
        _guilds = database.GetCollection<Guild>("guilds");
        _members = database.GetCollection<GuildMember>("guildMembers");
        _joinRequests = database.GetCollection<JoinRequest>("guildJoinRequests");
        _messages = database.GetCollection<GuildMessage>("guildMessages");
        _activities = database.GetCollection<GuildActivity>("guildActivities");
        _announcements = database.GetCollection<GuildAnnouncement>("guildAnnouncements");
        _logger = logger;

        // 索引已在 MongoDB 初始化脚本 (init.js) 中创建
    }

    // ========== 公会操作 ==========

    public async Task<Guild?> GetGuildAsync(string guildId)
    {
        return await _guilds.Find(g => g.GuildId == guildId).FirstOrDefaultAsync();
    }

    public async Task<Guild?> GetGuildByNameAsync(string name)
    {
        return await _guilds.Find(g => g.Name == name).FirstOrDefaultAsync();
    }

    public async Task<List<Guild>> GetGuildsAsync(int skip, int limit, string sortBy, bool descending)
    {
        var sortDefinition = descending
            ? Builders<Guild>.Sort.Descending(sortBy)
            : Builders<Guild>.Sort.Ascending(sortBy);

        return await _guilds.Find(_ => true)
            .Sort(sortDefinition)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<long> GetGuildCountAsync()
    {
        return await _guilds.CountDocumentsAsync(_ => true);
    }

    public async Task<List<Guild>> SearchGuildsAsync(string keyword)
    {
        var filter = Builders<Guild>.Filter.Or(
            Builders<Guild>.Filter.Regex(g => g.Name, new MongoDB.Bson.BsonRegularExpression(keyword, "i")),
            Builders<Guild>.Filter.Regex(g => g.Tag, new MongoDB.Bson.BsonRegularExpression(keyword, "i"))
        );

        return await _guilds.Find(filter).Limit(20).ToListAsync();
    }

    public async Task<Guild> CreateGuildAsync(Guild guild)
    {
        await _guilds.InsertOneAsync(guild);
        return guild;
    }

    public async Task<bool> UpdateGuildAsync(Guild guild)
    {
        var result = await _guilds.ReplaceOneAsync(g => g.GuildId == guild.GuildId, guild);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteGuildAsync(string guildId)
    {
        var result = await _guilds.DeleteOneAsync(g => g.GuildId == guildId);
        return result.DeletedCount > 0;
    }

    // ========== 成员操作 ==========

    public async Task<GuildMember?> GetMemberAsync(string userId)
    {
        return await _members.Find(m => m.UserId == userId).FirstOrDefaultAsync();
    }

    public async Task<List<GuildMember>> GetGuildMembersAsync(string guildId)
    {
        return await _members.Find(m => m.GuildId == guildId).ToListAsync();
    }

    public async Task<long> GetMemberCountAsync(string guildId)
    {
        return await _members.CountDocumentsAsync(m => m.GuildId == guildId);
    }

    public async Task<GuildMember> AddMemberAsync(GuildMember member)
    {
        await _members.InsertOneAsync(member);
        return member;
    }

    public async Task<bool> UpdateMemberAsync(GuildMember member)
    {
        var result = await _members.ReplaceOneAsync(m => m.UserId == member.UserId, member);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> RemoveMemberAsync(string userId)
    {
        var result = await _members.DeleteOneAsync(m => m.UserId == userId);
        return result.DeletedCount > 0;
    }

    public async Task<bool> RemoveAllMembersAsync(string guildId)
    {
        await _members.DeleteManyAsync(m => m.GuildId == guildId);
        return true;
    }

    // ========== 加入申请操作 ==========

    public async Task<JoinRequest> CreateJoinRequestAsync(JoinRequest request)
    {
        await _joinRequests.InsertOneAsync(request);
        return request;
    }

    public async Task<List<JoinRequest>> GetJoinRequestsAsync(string guildId)
    {
        return await _joinRequests
            .Find(r => r.GuildId == guildId && r.Status == "Pending")
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<JoinRequest?> GetJoinRequestAsync(string guildId, string userId)
    {
        return await _joinRequests
            .Find(r => r.GuildId == guildId && r.UserId == userId && r.Status == "Pending")
            .FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateJoinRequestAsync(JoinRequest request)
    {
        var result = await _joinRequests.ReplaceOneAsync(
            r => r.GuildId == request.GuildId && r.UserId == request.UserId,
            request);
        return result.ModifiedCount > 0;
    }

    // ========== 消息操作 ==========

    public async Task<GuildMessage> AddMessageAsync(GuildMessage message)
    {
        await _messages.InsertOneAsync(message);
        return message;
    }

    public async Task<List<GuildMessage>> GetMessagesAsync(string guildId, int limit)
    {
        return await _messages
            .Find(m => m.GuildId == guildId)
            .SortByDescending(m => m.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }

    // ========== 活动操作 ==========

    public async Task<GuildActivity> AddActivityAsync(GuildActivity activity)
    {
        await _activities.InsertOneAsync(activity);
        return activity;
    }

    public async Task<List<GuildActivity>> GetActivitiesAsync(string guildId, int limit)
    {
        return await _activities
            .Find(a => a.GuildId == guildId)
            .SortByDescending(a => a.Timestamp)
            .Limit(limit)
            .ToListAsync();
    }

    // ========== 公告操作 ==========

    public async Task<GuildAnnouncement> AddAnnouncementAsync(GuildAnnouncement announcement)
    {
        await _announcements.InsertOneAsync(announcement);
        return announcement;
    }

    public async Task<List<GuildAnnouncement>> GetAnnouncementsAsync(string guildId)
    {
        return await _announcements
            .Find(a => a.GuildId == guildId)
            .SortByDescending(a => a.IsPinned)
            .ThenByDescending(a => a.CreatedAt)
            .Limit(20)
            .ToListAsync();
    }
}
