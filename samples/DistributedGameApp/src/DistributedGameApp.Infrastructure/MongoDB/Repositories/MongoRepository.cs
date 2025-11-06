using System.Linq.Expressions;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// MongoDB Repository 基类
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public abstract class MongoRepository<TEntity> : IRepository<TEntity> where TEntity : class
{
    protected readonly IMongoCollection<TEntity> Collection;

    protected MongoRepository(IMongoDatabase database, string collectionName)
    {
        Collection = database.GetCollection<TEntity>(collectionName);
    }

    public virtual async Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<TEntity>.Filter.Eq("_id", id);
        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> FindOneAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        return await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<List<TEntity>> FindManyAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        return await Collection.Find(filter).ToListAsync(cancellationToken);
    }

    public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Collection.Find(_ => true).ToListAsync(cancellationToken);
    }

    public virtual async Task<(List<TEntity> Items, long TotalCount)> GetPagedAsync(
        Expression<Func<TEntity, bool>>? filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filterDefinition = filter != null
            ? Builders<TEntity>.Filter.Where(filter)
            : Builders<TEntity>.Filter.Empty;

        var totalCount = await Collection.CountDocumentsAsync(filterDefinition, cancellationToken: cancellationToken);
        var items = await Collection
            .Find(filterDefinition)
            .Skip(pageIndex * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public virtual async Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        await Collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
    }

    public virtual async Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        await Collection.InsertManyAsync(entities, cancellationToken: cancellationToken);
    }

    public virtual async Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        // 假设实体有一个 Id 属性
        var idProperty = typeof(TEntity).GetProperty("Id");
        if (idProperty == null)
        {
            throw new InvalidOperationException($"Entity {typeof(TEntity).Name} does not have an 'Id' property.");
        }

        var id = idProperty.GetValue(entity)?.ToString();
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var filter = Builders<TEntity>.Filter.Eq("_id", id);
        var result = await Collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    public virtual async Task<long> UpdateManyAsync(
        Expression<Func<TEntity, bool>> filter,
        UpdateDefinition<TEntity> update,
        CancellationToken cancellationToken = default)
    {
        var result = await Collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }

    public virtual async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<TEntity>.Filter.Eq("_id", id);
        var result = await Collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount > 0;
    }

    public virtual async Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        var result = await Collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount;
    }

    public virtual async Task<long> CountAsync(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        var filterDefinition = filter != null
            ? Builders<TEntity>.Filter.Where(filter)
            : Builders<TEntity>.Filter.Empty;

        return await Collection.CountDocumentsAsync(filterDefinition, cancellationToken: cancellationToken);
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default)
    {
        var count = await Collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }
}
