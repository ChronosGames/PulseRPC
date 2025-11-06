using DistributedGameApp.Shared.Domain.Mail;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 邮件 Repository
/// </summary>
public class MailRepository : MongoRepository<Mail>
{
    public MailRepository(MongoDbContext context)
        : base(context.SocialDatabase, "mails")
    {
    }

    /// <summary>
    /// 根据邮件ID获取邮件
    /// </summary>
    public async Task<Mail?> GetByMailIdAsync(string mailId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(m => m.MailId == mailId && !m.IsDeleted, cancellationToken);
    }

    /// <summary>
    /// 获取玩家的邮件列表（支持分页）
    /// </summary>
    public async Task<List<Mail>> GetPlayerMailsAsync(
        string playerId,
        int page = 1,
        int pageSize = 20,
        bool onlyUnread = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Mail>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(m => m.ReceiverId, playerId),
            filterBuilder.Eq(m => m.IsDeleted, false)
        );

        // 如果只获取未读邮件
        if (onlyUnread)
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(m => m.IsRead, false));
        }

        var skip = (page - 1) * pageSize;

        return await Collection
            .Find(filter)
            .SortByDescending(m => m.SentAt)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取玩家邮件总数
    /// </summary>
    public async Task<long> GetPlayerMailCountAsync(
        string playerId,
        bool onlyUnread = false,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Mail>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(m => m.ReceiverId, playerId),
            filterBuilder.Eq(m => m.IsDeleted, false)
        );

        if (onlyUnread)
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(m => m.IsRead, false));
        }

        return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 获取未读邮件数量
    /// </summary>
    public async Task<long> GetUnreadCountAsync(string playerId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.And(
            Builders<Mail>.Filter.Eq(m => m.ReceiverId, playerId),
            Builders<Mail>.Filter.Eq(m => m.IsRead, false),
            Builders<Mail>.Filter.Eq(m => m.IsDeleted, false)
        );

        return await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 标记邮件为已读
    /// </summary>
    public async Task<bool> MarkAsReadAsync(string mailId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.Eq(m => m.MailId, mailId);
        var update = Builders<Mail>.Update.Set(m => m.IsRead, true);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 标记多封邮件为已读
    /// </summary>
    public async Task<long> MarkMultipleAsReadAsync(List<string> mailIds, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.In(m => m.MailId, mailIds);
        var update = Builders<Mail>.Update.Set(m => m.IsRead, true);

        var result = await Collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }

    /// <summary>
    /// 领取附件
    /// </summary>
    public async Task<bool> ClaimAttachmentAsync(string mailId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.And(
            Builders<Mail>.Filter.Eq(m => m.MailId, mailId),
            Builders<Mail>.Filter.Eq(m => m.IsAttachmentClaimed, false)
        );
        var update = Builders<Mail>.Update.Set(m => m.IsAttachmentClaimed, true);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 删除邮件（软删除）
    /// </summary>
    public async Task<bool> DeleteMailAsync(string mailId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.Eq(m => m.MailId, mailId);
        var update = Builders<Mail>.Update.Set(m => m.IsDeleted, true);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 删除多封邮件（软删除）
    /// </summary>
    public async Task<long> DeleteMultipleMailsAsync(List<string> mailIds, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.In(m => m.MailId, mailIds);
        var update = Builders<Mail>.Update.Set(m => m.IsDeleted, true);

        var result = await Collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }

    /// <summary>
    /// 清理过期邮件（物理删除）
    /// </summary>
    public async Task<long> CleanupExpiredMailsAsync(CancellationToken cancellationToken = default)
    {
        var filter = Builders<Mail>.Filter.And(
            Builders<Mail>.Filter.Lt(m => m.ExpireAt, DateTime.UtcNow),
            Builders<Mail>.Filter.Ne(m => m.ExpireAt, null)
        );

        var result = await Collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount;
    }
}
