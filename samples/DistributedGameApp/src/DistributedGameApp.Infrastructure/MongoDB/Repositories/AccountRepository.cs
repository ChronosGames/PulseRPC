using DistributedGameApp.Shared.Domain.Accounts;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 账户 Repository
/// </summary>
public class AccountRepository : MongoRepository<Account>
{
    public AccountRepository(MongoDbContext context)
        : base(context.AccountsDatabase, "accounts")
    {
    }

    /// <summary>
    /// 根据用户ID获取账户
    /// </summary>
    public async Task<Account?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(a => a.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// 根据邮箱获取账户
    /// </summary>
    public async Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(a => a.Email == email, cancellationToken);
    }

    /// <summary>
    /// 根据第三方提供商信息获取账户
    /// </summary>
    public async Task<Account?> GetByProviderAsync(string provider, string providerUserId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(a => a.Provider == provider && a.ProviderUserId == providerUserId, cancellationToken);
    }

    /// <summary>
    /// 更新最后登录时间
    /// </summary>
    public async Task<bool> UpdateLastLoginAsync(string userId, string lastLoginIp, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Account>.Filter.Eq(a => a.UserId, userId);
        var update = Builders<Account>.Update
            .Set(a => a.LastLoginAt, DateTime.UtcNow)
            .Set(a => a.LastLoginIp, lastLoginIp);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 检查邮箱是否已存在
    /// </summary>
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(a => a.Email == email, cancellationToken);
    }

    /// <summary>
    /// 检查用户ID是否已存在
    /// </summary>
    public async Task<bool> UserIdExistsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(a => a.UserId == userId, cancellationToken);
    }
}
