using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServicePatterns;

/// <summary>
/// WorkerPool - 管理多个 Worker 并发处理任务
/// </summary>
/// <typeparam name="TContext">Worker 上下文类型</typeparam>
public class WorkerPool<TContext> : IDisposable where TContext : class
{
    private readonly int _workerCount;
    private readonly Func<int, TContext> _contextFactory;
    private readonly ILogger? _logger;
    private readonly Worker[] _workers;
    private readonly CancellationTokenSource _shutdownCts = new();
    private bool _disposed;

    public WorkerPool(
        int workerCount,
        Func<int, TContext> contextFactory,
        ILogger? logger = null)
    {
        if (workerCount < 1)
            throw new ArgumentException("Worker count must be at least 1", nameof(workerCount));

        ArgumentNullException.ThrowIfNull(contextFactory);

        _workerCount = workerCount;
        _contextFactory = contextFactory;
        _logger = logger;
        _workers = new Worker[workerCount];
    }

    /// <summary>
    /// 初始化 WorkerPool
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Initializing worker pool with {Count} workers", _workerCount);

        for (int i = 0; i < _workerCount; i++)
        {
            var context = _contextFactory(i);
            _workers[i] = new Worker(i, context, _logger);
            await _workers[i].StartAsync(_shutdownCts.Token);
        }

        _logger?.LogInformation("Worker pool initialized successfully");
    }

    /// <summary>
    /// 执行任务 - 自动选择最空闲的 Worker
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        Func<TContext, CancellationToken, Task<TResult>> taskFunc,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerPool<TContext>));

        // 选择队列最短的 Worker
        var worker = _workers.MinBy(w => w.QueuedTaskCount) ?? _workers[0];

        return await worker.EnqueueAsync(taskFunc, cancellationToken);
    }

    /// <summary>
    /// 执行任务 - 基于 ShardId 哈希分配到固定 Worker
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        string shardKey,
        Func<TContext, CancellationToken, Task<TResult>> taskFunc,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerPool<TContext>));

        var workerIndex = GetWorkerIndex(shardKey);
        var worker = _workers[workerIndex];

        return await worker.EnqueueAsync(taskFunc, cancellationToken);
    }

    /// <summary>
    /// 执行任务 - 基于数字 ShardId
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        int shardId,
        Func<TContext, CancellationToken, Task<TResult>> taskFunc,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerPool<TContext>));

        var workerIndex = shardId % _workerCount;
        var worker = _workers[workerIndex];

        return await worker.EnqueueAsync(taskFunc, cancellationToken);
    }

    /// <summary>
    /// 执行任务（无返回值）- 基于 ShardId
    /// </summary>
    public async Task ExecuteAsync(
        string shardKey,
        Func<TContext, CancellationToken, Task> taskFunc,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerPool<TContext>));

        var workerIndex = GetWorkerIndex(shardKey);
        var worker = _workers[workerIndex];

        await worker.EnqueueAsync(taskFunc, cancellationToken);
    }

    /// <summary>
    /// 获取 WorkerPool 统计信息
    /// </summary>
    public WorkerPoolStats GetStats()
    {
        return new WorkerPoolStats
        {
            TotalWorkers = _workerCount,
            WorkerStats = _workers.Select(w => w.GetStats()).ToArray()
        };
    }

    /// <summary>
    /// 计算 Worker 索引
    /// </summary>
    private int GetWorkerIndex(string key)
    {
        // 使用 xxHash 或者简单的 GetHashCode
        var hash = key.GetHashCode();
        if (hash < 0) hash = -hash;
        return hash % _workerCount;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shutdownCts.Cancel();

        foreach (var worker in _workers)
        {
            worker.Dispose();
        }

        _shutdownCts.Dispose();
        _logger?.LogInformation("Worker pool disposed");
    }

    /// <summary>
    /// Worker - 单个工作线程
    /// </summary>
    private class Worker : IDisposable
    {
        private readonly int _id;
        private readonly TContext _context;
        private readonly ILogger? _logger;
        private readonly Channel<WorkItem> _channel;
        private Task? _workerTask;
        private long _processedCount;
        private long _errorCount;

        public Worker(int id, TContext context, ILogger? logger)
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
                _logger?.LogDebug("Worker {Id} started", _id);

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
                        _logger?.LogError(ex, "Worker {Id} task execution failed", _id);
                        item.SetException(ex);
                    }
                }

                _logger?.LogDebug("Worker {Id} stopped", _id);
            }, shutdownToken);
        }

        public async Task<TResult> EnqueueAsync<TResult>(
            Func<TContext, CancellationToken, Task<TResult>> taskFunc,
            CancellationToken cancellationToken)
        {
            var workItem = new WorkItem<TResult>(taskFunc, cancellationToken);
            await _channel.Writer.WriteAsync(workItem, cancellationToken);
            return await workItem.Task;
        }

        public async Task EnqueueAsync(
            Func<TContext, CancellationToken, Task> taskFunc,
            CancellationToken cancellationToken)
        {
            var workItem = new WorkItemVoid(taskFunc, cancellationToken);
            await _channel.Writer.WriteAsync(workItem, cancellationToken);
            await workItem.Task;
        }

        public WorkerStats GetStats()
        {
            return new WorkerStats
            {
                WorkerId = _id,
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

            public abstract Task ExecuteAsync(TContext context, CancellationToken cancellationToken);
            public abstract void SetException(Exception exception);
        }

        private class WorkItem<TResult> : WorkItem
        {
            private readonly Func<TContext, CancellationToken, Task<TResult>> _taskFunc;
            private readonly TaskCompletionSource<TResult> _tcs = new();

            public WorkItem(
                Func<TContext, CancellationToken, Task<TResult>> taskFunc,
                CancellationToken cancellationToken)
                : base(cancellationToken)
            {
                _taskFunc = taskFunc;
            }

            public Task<TResult> Task => _tcs.Task;

            public override async Task ExecuteAsync(TContext context, CancellationToken cancellationToken)
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
            private readonly Func<TContext, CancellationToken, Task> _taskFunc;
            private readonly TaskCompletionSource _tcs = new();

            public WorkItemVoid(
                Func<TContext, CancellationToken, Task> taskFunc,
                CancellationToken cancellationToken)
                : base(cancellationToken)
            {
                _taskFunc = taskFunc;
            }

            public Task Task => _tcs.Task;

            public override async Task ExecuteAsync(TContext context, CancellationToken cancellationToken)
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
/// WorkerPool 统计信息
/// </summary>
public class WorkerPoolStats
{
    public int TotalWorkers { get; set; }
    public required WorkerStats[] WorkerStats { get; set; }

    public int TotalQueuedTasks => WorkerStats.Sum(w => w.QueuedTasks);
    public long TotalProcessedCount => WorkerStats.Sum(w => w.ProcessedCount);
    public long TotalErrorCount => WorkerStats.Sum(w => w.ErrorCount);
}

/// <summary>
/// 单个 Worker 的统计信息
/// </summary>
public class WorkerStats
{
    public int WorkerId { get; set; }
    public int QueuedTasks { get; set; }
    public long ProcessedCount { get; set; }
    public long ErrorCount { get; set; }
}
