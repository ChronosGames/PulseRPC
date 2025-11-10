using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServicePatterns;

/// <summary>
/// IO 密集型服务基类
/// 适用于: Consul、MongoDB、Redis 等外部服务访问
///
/// 特征:
/// - 连接池: 4-8 个连接对象
/// - WorkerPool: 8-16 个 Worker
/// - 哈希分片: 基于 key 分配 Worker
/// </summary>
/// <typeparam name="TConnection">连接类型</typeparam>
public abstract class IOBoundServiceBase<TConnection> : IAsyncDisposable
    where TConnection : class
{
    private readonly ConnectionPool<TConnection> _connectionPool;
    private readonly WorkerPool<WorkerContext<TConnection>> _workerPool;
    private readonly ILogger? _logger;
    private bool _initialized;

    protected IOBoundServiceBase(
        IOBoundServiceOptions options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;

        // 创建连接池
        _connectionPool = new ConnectionPool<TConnection>(
            connectionFactory: CreateConnectionAsync,
            connectionValidator: ValidateConnectionAsync,
            connectionDisposer: DisposeConnection,
            minSize: options.MinConnections,
            maxSize: options.MaxConnections,
            logger: logger);

        // 创建 WorkerPool，每个 Worker 持有连接池引用
        _workerPool = new WorkerPool<WorkerContext<TConnection>>(
            workerCount: options.WorkerCount,
            contextFactory: workerId => new WorkerContext<TConnection>
            {
                WorkerId = workerId,
                ConnectionPool = _connectionPool
            },
            logger: logger);
    }

    /// <summary>
    /// 初始化服务
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        _logger?.LogInformation("Initializing {ServiceName}", GetType().Name);

        await _connectionPool.InitializeAsync(cancellationToken);
        await _workerPool.InitializeAsync(cancellationToken);

        _initialized = true;
        _logger?.LogInformation("{ServiceName} initialized successfully", GetType().Name);
    }

    /// <summary>
    /// 执行 IO 操作 - 自动负载均衡
    /// </summary>
    protected async Task<TResult> ExecuteAsync<TResult>(
        Func<TConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        return await _workerPool.ExecuteAsync(async (ctx, ct) =>
        {
            using var pooledConn = await ctx.ConnectionPool.AcquireAsync(ct);
            return await operation(pooledConn.Connection, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// 执行 IO 操作 - 基于 ShardKey 哈希分配
    /// </summary>
    protected async Task<TResult> ExecuteAsync<TResult>(
        string shardKey,
        Func<TConnection, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        return await _workerPool.ExecuteAsync(shardKey, async (ctx, ct) =>
        {
            using var pooledConn = await ctx.ConnectionPool.AcquireAsync(ct);
            return await operation(pooledConn.Connection, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// 执行 IO 操作（无返回值）- 基于 ShardKey
    /// </summary>
    protected async Task ExecuteAsync(
        string shardKey,
        Func<TConnection, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        await _workerPool.ExecuteAsync(shardKey, async (ctx, ct) =>
        {
            using var pooledConn = await ctx.ConnectionPool.AcquireAsync(ct);
            await operation(pooledConn.Connection, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// 获取服务统计信息
    /// </summary>
    public IOBoundServiceStats GetStats()
    {
        return new IOBoundServiceStats
        {
            ConnectionPoolStats = _connectionPool.GetStats(),
            WorkerPoolStats = _workerPool.GetStats()
        };
    }

    /// <summary>
    /// 创建连接 - 子类实现
    /// </summary>
    protected abstract Task<TConnection> CreateConnectionAsync();

    /// <summary>
    /// 验证连接是否有效 - 子类实现
    /// </summary>
    protected abstract Task<bool> ValidateConnectionAsync(TConnection connection);

    /// <summary>
    /// 释放连接 - 子类可选实现
    /// </summary>
    protected virtual void DisposeConnection(TConnection connection)
    {
        if (connection is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException($"{GetType().Name} has not been initialized. Call InitializeAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        _workerPool.Dispose();
        _connectionPool.Dispose();

        await Task.CompletedTask;
        _logger?.LogInformation("{ServiceName} disposed", GetType().Name);
    }
}

/// <summary>
/// Worker 上下文 - 持有连接池引用
/// </summary>
public class WorkerContext<TConnection> where TConnection : class
{
    public required int WorkerId { get; set; }
    public required ConnectionPool<TConnection> ConnectionPool { get; set; }
}

/// <summary>
/// IO 密集型服务配置选项
/// </summary>
public class IOBoundServiceOptions
{
    /// <summary>
    /// 最小连接数
    /// </summary>
    public int MinConnections { get; set; } = 2;

    /// <summary>
    /// 最大连接数
    /// </summary>
    public int MaxConnections { get; set; } = 4;

    /// <summary>
    /// Worker 数量
    /// </summary>
    public int WorkerCount { get; set; } = 8;

    /// <summary>
    /// 验证配置
    /// </summary>
    public void Validate()
    {
        if (MinConnections < 1)
            throw new ArgumentException("MinConnections must be at least 1");

        if (MaxConnections < MinConnections)
            throw new ArgumentException("MaxConnections must be >= MinConnections");

        if (WorkerCount < 1)
            throw new ArgumentException("WorkerCount must be at least 1");

        // 建议: Worker 数量应该是连接数的 2-3 倍
        if (WorkerCount < MaxConnections * 2)
        {
            WorkerCount = MaxConnections * 2;
        }
    }
}

/// <summary>
/// IO 密集型服务统计信息
/// </summary>
public class IOBoundServiceStats
{
    public required ConnectionPoolStats ConnectionPoolStats { get; set; }
    public required WorkerPoolStats WorkerPoolStats { get; set; }

    public override string ToString()
    {
        return $"Connections: {ConnectionPoolStats.ActiveConnections}/{ConnectionPoolStats.TotalConnections}, " +
               $"Workers: {WorkerPoolStats.TotalWorkers}, " +
               $"Queued: {WorkerPoolStats.TotalQueuedTasks}, " +
               $"Processed: {WorkerPoolStats.TotalProcessedCount}, " +
               $"Errors: {WorkerPoolStats.TotalErrorCount}";
    }
}
