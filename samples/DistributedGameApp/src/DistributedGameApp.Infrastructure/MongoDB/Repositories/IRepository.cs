using System.Linq.Expressions;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// Repository 基础接口
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public interface IRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// 根据 ID 获取实体
    /// </summary>
    Task<TEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据条件查找单个实体
    /// </summary>
    Task<TEntity?> FindOneAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据条件查找多个实体
    /// </summary>
    Task<List<TEntity>> FindManyAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有实体
    /// </summary>
    Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页查询
    /// </summary>
    Task<(List<TEntity> Items, long TotalCount)> GetPagedAsync(
        Expression<Func<TEntity, bool>>? filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 插入实体
    /// </summary>
    Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量插入
    /// </summary>
    Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新实体
    /// </summary>
    Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据条件更新多个文档
    /// </summary>
    /// <param name="filter">过滤条件</param>
    /// <param name="update">更新定义</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>更新的文档数量</returns>
    Task<long> UpdateManyAsync(
        Expression<Func<TEntity, bool>> filter,
        UpdateDefinition<TEntity> update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除实体
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据条件删除
    /// </summary>
    Task<long> DeleteManyAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计数
    /// </summary>
    Task<long> CountAsync(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否存在
    /// </summary>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> filter, CancellationToken cancellationToken = default);
}
