using DistributedGameApp.Shared.Domain.Social;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 好友 Repository
/// </summary>
public class FriendRepository : MongoRepository<Friend>
{
    public FriendRepository(MongoDbContext context)
        : base(context.SocialDatabase, "friends")
    {
    }

    /// <summary>
    /// 获取用户的所有好友
    /// </summary>
    public async Task<List<Friend>> GetFriendsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await FindManyAsync(f => f.UserId == userId && f.Status == "Accepted", cancellationToken);
    }

    /// <summary>
    /// 获取待处理的好友请求
    /// </summary>
    public async Task<List<Friend>> GetPendingRequestsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await FindManyAsync(f => f.FriendUserId == userId && f.Status == "Pending", cancellationToken);
    }

    /// <summary>
    /// 获取已发送的好友请求
    /// </summary>
    public async Task<List<Friend>> GetSentRequestsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await FindManyAsync(f => f.UserId == userId && f.Status == "Pending", cancellationToken);
    }

    /// <summary>
    /// 获取好友关系
    /// </summary>
    public async Task<Friend?> GetFriendshipAsync(string userId, string friendUserId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(f => f.UserId == userId && f.FriendUserId == friendUserId, cancellationToken);
    }

    /// <summary>
    /// 检查是否已经是好友
    /// </summary>
    public async Task<bool> IsFriendAsync(string userId, string friendUserId, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(
            f => f.UserId == userId && f.FriendUserId == friendUserId && f.Status == "Accepted",
            cancellationToken);
    }

    /// <summary>
    /// 检查是否已有好友请求
    /// </summary>
    public async Task<bool> HasPendingRequestAsync(string userId, string friendUserId, CancellationToken cancellationToken = default)
    {
        // 检查双向请求
        return await ExistsAsync(
            f => ((f.UserId == userId && f.FriendUserId == friendUserId) ||
                  (f.UserId == friendUserId && f.FriendUserId == userId)) &&
                 f.Status == "Pending",
            cancellationToken);
    }

    /// <summary>
    /// 接受好友请求
    /// </summary>
    public async Task<bool> AcceptFriendRequestAsync(string userId, string friendUserId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Friend>.Filter.And(
            Builders<Friend>.Filter.Eq(f => f.UserId, friendUserId),
            Builders<Friend>.Filter.Eq(f => f.FriendUserId, userId),
            Builders<Friend>.Filter.Eq(f => f.Status, "Pending")
        );

        var update = Builders<Friend>.Update
            .Set(f => f.Status, "Accepted")
            .Set(f => f.AcceptedAt, DateTime.UtcNow);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 删除好友关系（双向删除）
    /// </summary>
    public async Task<bool> DeleteFriendshipAsync(string userId, string friendUserId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Friend>.Filter.Or(
            Builders<Friend>.Filter.And(
                Builders<Friend>.Filter.Eq(f => f.UserId, userId),
                Builders<Friend>.Filter.Eq(f => f.FriendUserId, friendUserId)
            ),
            Builders<Friend>.Filter.And(
                Builders<Friend>.Filter.Eq(f => f.UserId, friendUserId),
                Builders<Friend>.Filter.Eq(f => f.FriendUserId, userId)
            )
        );

        var result = await Collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount > 0;
    }
}

/// <summary>
/// 聊天消息 Repository
/// </summary>
public class ChatMessageRepository : MongoRepository<ChatMessage>
{
    public ChatMessageRepository(MongoDbContext context)
        : base(context.SocialDatabase, "chat_messages")
    {
    }

    /// <summary>
    /// 获取频道消息
    /// </summary>
    public async Task<List<ChatMessage>> GetChannelMessagesAsync(
        string channelType,
        string channelId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await Collection
            .Find(m => m.ChannelType == channelType && m.ChannelId == channelId)
            .SortByDescending(m => m.SentAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取私聊消息
    /// </summary>
    public async Task<List<ChatMessage>> GetPrivateMessagesAsync(
        string userId1,
        string userId2,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ChatMessage>.Filter.And(
            Builders<ChatMessage>.Filter.Eq(m => m.ChannelType, "Private"),
            Builders<ChatMessage>.Filter.Or(
                Builders<ChatMessage>.Filter.And(
                    Builders<ChatMessage>.Filter.Eq(m => m.SenderId, userId1),
                    Builders<ChatMessage>.Filter.Eq(m => m.ChannelId, userId2)
                ),
                Builders<ChatMessage>.Filter.And(
                    Builders<ChatMessage>.Filter.Eq(m => m.SenderId, userId2),
                    Builders<ChatMessage>.Filter.Eq(m => m.ChannelId, userId1)
                )
            )
        );

        return await Collection
            .Find(filter)
            .SortByDescending(m => m.SentAt)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 删除频道的所有消息
    /// </summary>
    public async Task<long> DeleteChannelMessagesAsync(string channelType, string channelId, CancellationToken cancellationToken = default)
    {
        return await DeleteManyAsync(m => m.ChannelType == channelType && m.ChannelId == channelId, cancellationToken);
    }
}
