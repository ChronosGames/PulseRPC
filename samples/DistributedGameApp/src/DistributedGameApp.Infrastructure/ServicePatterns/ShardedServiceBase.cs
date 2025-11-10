using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DistributedGameApp.Infrastructure.ServicePatterns;

/// <summary>
/// 分片服务基类
/// 适用于: 玩家状态管理、房间管理、聊天室等有状态服务
///
/// 特征:
/// - 严格的分片隔离
/// - 每个分片独立的状态和资源
/// - 基于 ShardId/PlayerId/RoomId 哈希
/// - 保证亲和性调度
/// </summary>
/// <typeparam name="TShardContext">分片上下文类型</typeparam>
public abstract class ShardedServiceBase<TShardContext> : IAsyncDisposable
    where TShardContext : class
{
    private readonly int _shardCount;
    private readonly Shard[] _shards;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _initialized;

    protected ShardedServiceBase(
        ShardedServiceOptions options,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _shardCount = options.ShardCount;
        _logger = logger;
        _shards = new Shard[_shardCount];
    }

    /// <summary>
    /// 初始化服务
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        _logger?.LogInformation("Initializing {ServiceName} with {ShardCount} shards", GetType().Name, _shardCount);

        // 并行初始化所有分片
        var tasks = new Task[_shardCount];
        for (int i = 0; i < _shardCount; i++)
        {
            var shardId = i;
            tasks[i] = Task.Run(async () =>
            {
                var context = await CreateShardContextAsync(shardId, cancellationToken);
                _shards[shardId] = new Shard(shardId, context, _logger);
                await _shards[shardId].StartAsync(_shutdownCts.Token);
            }, cancellationToken);
        }

        await Task.WhenAll(tasks);

        _initialized = true;
        _logger?.LogInformation("{ServiceName} initialized successfully", GetType().Name);
    }

    /// <summary>
    /// 执行操作 - 基于字符串 Key 哈希
    /// </summary>
    protected async Task<TResult> ExecuteAsync<TResult>(
        string shardKey,
        Func<TShardContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var shardId = GetShardId(shardKey);
        var shard = _shards[shardId];

        return await shard.EnqueueAsync(operation, cancellationToken);
    }

    /// <summary>
    /// 执行操作 - 基于数字 ShardId
    /// </summary>
    protected async Task<TResult> ExecuteAsync<TResult>(
        int shardId,
        Func<TShardContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        if (shardId < 0 || shardId >= _shardCount)
            throw new ArgumentOutOfRangeException(nameof(shardId));

        var shard = _shards[shardId];
        return await shard.EnqueueAsync(operation, cancellationToken);
    }

    /// <summary>
    /// 执行操作（无返回值）- 基于字符串 Key
    /// </summary>
    protected async Task ExecuteAsync(
        string shardKey,
        Func<TShardContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var shardId = GetShardId(shardKey);
        var shard = _shards[shardId];

        await shard.EnqueueAsync(operation, cancellationToken);
    }

    /// <summary>
    /// 广播操作到所有分片
    /// </summary>
    protected async Task BroadcastAsync(
        Func<TShardContext, int, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var tasks = new Task[_shardCount];
        for (int i = 0; i < _shardCount; i++)
        {
            var shardId = i;
            tasks[i] = _shards[shardId].EnqueueAsync((ctx, ct) => operation(ctx, shardId, ct), cancellationToken);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 获取服务统计信息
    /// </summary>
    public ShardedServiceStats GetStats()
    {
        return new ShardedServiceStats
        {
            TotalShards = _shardCount,
            ShardStats = _shards.Select(s => s.GetStats()).ToArray()
        };
    }

    /// <summary>
    /// 获取 ShardId - 使用一致性哈希
    /// </summary>
    private int GetShardId(string key)
    {
        var hash = key.GetHashCode();
        if (hash < 0) hash = -hash;
        return hash % _shardCount;
    }

    /// <summary>
    /// 创建分片上下文 - 子类实现
    /// </summary>
    protected abstract Task<TShardContext> CreateShardContextAsync(
        int shardId,
        CancellationToken cancellationToken);

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException($"{GetType().Name} has not been initialized. Call InitializeAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();

        foreach (var shard in _shards)
        {
            shard?.Dispose();
        }

        _shutdownCts.Dispose();
        await Task.CompletedTask;

        _logger?.LogInformation("{ServiceName} disposed", GetType().Name);
    }

    /// <summary>
    /// 单个分片
    /// </summary>
    private class Shard : IDisposable
    {
        private readonly int _id;
        private readonly TShardContext _context;
        private readonly ILogger? _logger;
        private readonly Channel<WorkItem> _channel;
        private Task? _workerTask;
        private long _processedCount;
        private long _errorCount;

        public Shard(int id, TShardContext context, ILogger? logger)
        {
            _id = id;
            _context = context;
            _logger = logger;
            _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public int QueuedTaskCount => _channel.Reader.Count;

        public async Task StartAsync(CancellationToken shutdownToken)
        {
            _workerTask = Task.Run(async () =>
            {
                _logger?.LogDebug("Shard {Id} started", _id);

                await foreach (var item in _channel.Reader.ReadAllAsync(shutdownToken))
                {
                    try
                    {
                        await item.ExecuteAsync(_context, item.CancellationToken);
                        Interlocked.Increment(ref _processedCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _errorCount);
                        _logger?.LogError(ex, "Shard {Id} task execution failed", _id);
                        item.SetException(ex);
                    }
                }

                _logger?.LogDebug("Shard {Id} stopped", _id);
            }, shutdownToken);
        }

        public async Task<TResult> EnqueueAsync<TResult>(
            Func<TShardContext, CancellationToken, Task<TResult>> taskFunc,
            CancellationToken cancellationToken)
        {
            var workItem = new WorkItem<TResult>(taskFunc, cancellationToken);
            await _channel.Writer.WriteAsync(workItem, cancellationToken);
            return await workItem.Task;
        }

        public async Task EnqueueAsync(
            Func<TShardContext, CancellationToken, Task> taskFunc,
            CancellationToken cancellationToken)
        {
            var workItem = new WorkItemVoid(taskFunc, cancellationToken);
            await _channel.Writer.WriteAsync(workItem, cancellationToken);
            await workItem.Task;
        }

        public ShardStats GetStats()
        {
            return new ShardStats
            {
                ShardId = _id,
                QueuedTasks = QueuedTaskCount,
                ProcessedCount = _processedCount,
                ErrorCount = _errorCount
            };
        }

        public void Dispose()
        {
            _channel.Writer.Complete();

            if (_context is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private abstract class WorkItem
        {
            public CancellationToken CancellationToken { get; }

            protected WorkItem(CancellationToken cancellationToken)
            {
                CancellationToken = cancellationToken;
            }

            public abstract Task ExecuteAsync(TShardContext context, CancellationToken cancellationToken);
            public abstract void SetException(Exception exception);
        }

        private class WorkItem<TResult> : WorkItem
        {
            private readonly Func<TShardContext, CancellationToken, Task<TResult>> _taskFunc;
            private readonly TaskCompletionSource<TResult> _tcs = new();

            public WorkItem(
                Func<TShardContext, CancellationToken, Task<TResult>> taskFunc,
                CancellationToken cancellationToken)
                : base(cancellationToken)
            {
                _taskFunc = taskFunc;
            }

            public Task<TResult> Task => _tcs.Task;

            public override async Task ExecuteAsync(TShardContext context, CancellationToken cancellationToken)
            {
                try
                {
                    var result = await _taskFunc(context, cancellationToken);
                    _tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    _tcs.SetException(ex);
                    throw;
                }
            }

            public override void SetException(Exception exception)
            {
                _tcs.TrySetException(exception);
            }
        }

        private class WorkItemVoid : WorkItem
        {
            private readonly Func<TShardContext, CancellationToken, Task> _taskFunc;
            private readonly TaskCompletionSource _tcs = new();

            public WorkItemVoid(
                Func<TShardContext, CancellationToken, Task> taskFunc,
                CancellationToken cancellationToken)
                : base(cancellationToken)
            {
                _taskFunc = taskFunc;
            }

            public Task Task => _tcs.Task;

            public override async Task ExecuteAsync(TShardContext context, CancellationToken cancellationToken)
            {
                try
                {
                    await _taskFunc(context, cancellationToken);
                    _tcs.SetResult();
                }
                catch (Exception ex)
                {
                    _tcs.SetException(ex);
                    throw;
                }
            }

            public override void SetException(Exception exception)
            {
                _tcs.TrySetException(exception);
            }
        }
    }
}

/// <summary>
/// 分片服务配置选项
/// </summary>
public class ShardedServiceOptions
{
    /// <summary>
    /// 分片数量 (建议使用 2 的幂次: 4, 8, 16, 32, 64)
    /// </summary>
    public int ShardCount { get; set; } = 8;

    public void Validate()
    {
        if (ShardCount < 1)
            throw new ArgumentException("ShardCount must be at least 1");

        // 建议使用 2 的幂次，便于位运算优化
        if (!IsPowerOfTwo(ShardCount))
        {
            var nextPowerOfTwo = (int)Math.Pow(2, Math.Ceiling(Math.Log2(ShardCount)));
            throw new ArgumentException(
                $"ShardCount should be a power of 2 for optimal performance. " +
                $"Consider using {nextPowerOfTwo} instead of {ShardCount}");
        }
    }

    private static bool IsPowerOfTwo(int n)
    {
        return n > 0 && (n & (n - 1)) == 0;
    }
}

/// <summary>
/// 分片服务统计信息
/// </summary>
public class ShardedServiceStats
{
    public int TotalShards { get; set; }
    public required ShardStats[] ShardStats { get; set; }

    public int TotalQueuedTasks => ShardStats.Sum(s => s.QueuedTasks);
    public long TotalProcessedCount => ShardStats.Sum(s => s.ProcessedCount);
    public long TotalErrorCount => ShardStats.Sum(s => s.ErrorCount);

    /// <summary>
    /// 获取负载分布（每个分片的队列长度）
    /// </summary>
    public Dictionary<int, int> GetLoadDistribution()
    {
        return ShardStats.ToDictionary(s => s.ShardId, s => s.QueuedTasks);
    }

    public override string ToString()
    {
        return $"Shards: {TotalShards}, " +
               $"Queued: {TotalQueuedTasks}, " +
               $"Processed: {TotalProcessedCount}, " +
               $"Errors: {TotalErrorCount}";
    }
}

/// <summary>
/// 单个分片的统计信息
/// </summary>
public class ShardStats
{
    public int ShardId { get; set; }
    public int QueuedTasks { get; set; }
    public long ProcessedCount { get; set; }
    public long ErrorCount { get; set; }
}
