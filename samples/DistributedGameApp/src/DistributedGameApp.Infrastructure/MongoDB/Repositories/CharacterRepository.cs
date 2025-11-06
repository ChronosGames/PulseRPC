using DistributedGameApp.Shared.Domain.Characters;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 角色 Repository
/// </summary>
public class CharacterRepository : MongoRepository<Character>
{
    public CharacterRepository(MongoDbContext context)
        : base(context.CharactersDatabase, "characters")
    {
    }

    /// <summary>
    /// 根据角色ID获取角色
    /// </summary>
    public async Task<Character?> GetByCharacterIdAsync(string characterId, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(c => c.CharacterId == characterId, cancellationToken);
    }

    /// <summary>
    /// 根据用户ID获取所有角色
    /// </summary>
    public async Task<List<Character>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await FindManyAsync(c => c.UserId == userId, cancellationToken);
    }

    /// <summary>
    /// 根据角色名称获取角色
    /// </summary>
    public async Task<Character?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await FindOneAsync(c => c.Name == name, cancellationToken);
    }

    /// <summary>
    /// 检查角色名称是否已存在
    /// </summary>
    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(c => c.Name == name, cancellationToken);
    }

    /// <summary>
    /// 更新角色等级
    /// </summary>
    public async Task<bool> UpdateLevelAsync(string characterId, int level, long exp, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Character>.Filter.Eq(c => c.CharacterId, characterId);
        var update = Builders<Character>.Update
            .Set(c => c.Level, level)
            .Set(c => c.Exp, exp);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 更新角色金币
    /// </summary>
    public async Task<bool> UpdateGoldAsync(string characterId, long gold, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Character>.Filter.Eq(c => c.CharacterId, characterId);
        var update = Builders<Character>.Update.Set(c => c.Gold, gold);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 增加金币
    /// </summary>
    public async Task<bool> AddGoldAsync(string characterId, long amount, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Character>.Filter.Eq(c => c.CharacterId, characterId);
        var update = Builders<Character>.Update.Inc(c => c.Gold, amount);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 更新最后上线时间
    /// </summary>
    public async Task<bool> UpdateLastOnlineAsync(string characterId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Character>.Filter.Eq(c => c.CharacterId, characterId);
        var update = Builders<Character>.Update.Set(c => c.LastOnlineAt, DateTime.UtcNow);

        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// 获取等级排行榜
    /// </summary>
    public async Task<List<Character>> GetTopByLevelAsync(int count, CancellationToken cancellationToken = default)
    {
        return await Collection
            .Find(_ => true)
            .SortByDescending(c => c.Level)
            .ThenByDescending(c => c.Exp)
            .Limit(count)
            .ToListAsync(cancellationToken);
    }
}
