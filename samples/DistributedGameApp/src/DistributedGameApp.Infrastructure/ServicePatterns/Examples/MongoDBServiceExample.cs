using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace DistributedGameApp.Infrastructure.ServicePatterns.Examples;

/// <summary>
/// MongoDB 服务示例 - 使用 IOBoundServiceBase
///
/// 配置: 4个连接 + 8个Worker
/// 场景: 数据库读写、聚合查询
/// </summary>
public class MongoDBServiceExample : IOBoundServiceBase<IMongoDatabase>
{
    private readonly string _connectionString;
    private readonly string _databaseName;

    public MongoDBServiceExample(
        string connectionString,
        string databaseName,
        ILogger<MongoDBServiceExample>? logger = null)
        : base(new IOBoundServiceOptions
        {
            MinConnections = 2,
            MaxConnections = 4,
            WorkerCount = 8
        }, logger)
    {
        _connectionString = connectionString;
        _databaseName = databaseName;
    }

    /// <summary>
    /// 创建 MongoDB 连接
    /// </summary>
    protected override Task<IMongoDatabase> CreateConnectionAsync()
    {
        var client = new MongoClient(_connectionString);
        var database = client.GetDatabase(_databaseName);
        return Task.FromResult(database);
    }

    /// <summary>
    /// 验证连接是否有效
    /// </summary>
    protected override async Task<bool> ValidateConnectionAsync(IMongoDatabase connection)
    {
        try
        {
            // 尝试列出集合来验证连接
            await connection.ListCollectionNamesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ==================== 业务方法 ====================

    /// <summary>
    /// 插入文档 - 基于文档ID哈希
    /// </summary>
    public async Task<bool> InsertDocumentAsync<T>(
        string collectionName,
        string documentId,
        T document,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(documentId, async (db, ct) =>
        {
            var collection = db.GetCollection<T>(collectionName);
            await collection.InsertOneAsync(document, cancellationToken: ct);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// 查询文档 - 基于查询Key哈希
    /// </summary>
    public async Task<T?> FindDocumentAsync<T>(
        string collectionName,
        string queryKey,
        FilterDefinition<T> filter,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(queryKey, async (db, ct) =>
        {
            var collection = db.GetCollection<T>(collectionName);
            var cursor = await collection.FindAsync(filter, cancellationToken: ct);
            return await cursor.FirstOrDefaultAsync(ct);
        }, cancellationToken);
    }

    /// <summary>
    /// 更新文档 - 基于文档ID哈希
    /// </summary>
    public async Task<bool> UpdateDocumentAsync<T>(
        string collectionName,
        string documentId,
        FilterDefinition<T> filter,
        UpdateDefinition<T> update,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(documentId, async (db, ct) =>
        {
            var collection = db.GetCollection<T>(collectionName);
            var result = await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
            return result.ModifiedCount > 0;
        }, cancellationToken);
    }

    /// <summary>
    /// 批量插入 - 自动负载均衡
    /// </summary>
    public async Task<int> BulkInsertAsync<T>(
        string collectionName,
        IEnumerable<T> documents,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async (db, ct) =>
        {
            var collection = db.GetCollection<T>(collectionName);
            await collection.InsertManyAsync(documents, cancellationToken: ct);
            return documents.Count();
        }, cancellationToken);
    }

    /// <summary>
    /// 聚合查询 - 自动负载均衡
    /// </summary>
    public async Task<List<TResult>> AggregateAsync<T, TResult>(
        string collectionName,
        PipelineDefinition<T, TResult> pipeline,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async (db, ct) =>
        {
            var collection = db.GetCollection<T>(collectionName);
            var cursor = await collection.AggregateAsync(pipeline, cancellationToken: ct);
            return await cursor.ToListAsync(ct);
        }, cancellationToken);
    }
}

/// <summary>
/// 示例文档类型
/// </summary>
public class PlayerDocument
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public long Gold { get; set; }
    public DateTime CreatedAt { get; set; }
}
