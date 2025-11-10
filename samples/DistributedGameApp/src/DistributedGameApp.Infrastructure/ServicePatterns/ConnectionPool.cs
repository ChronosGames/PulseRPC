using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServicePatterns;

/// <summary>
/// 连接池 - 管理可复用的连接对象
/// </summary>
/// <typeparam name="T">连接对象类型</typeparam>
public class ConnectionPool<T> : IDisposable where T : class
{
    private readonly Func<Task<T>> _connectionFactory;
    private readonly Func<T, Task<bool>> _connectionValidator;
    private readonly Action<T>? _connectionDisposer;
    private readonly int _minSize;
    private readonly int _maxSize;
    private readonly ILogger? _logger;

    private readonly ConcurrentBag<ConnectionWrapper> _availableConnections = new();
    private readonly ConcurrentDictionary<int, ConnectionWrapper> _allConnections = new();
    private int _currentSize;
    private bool _disposed;

    public ConnectionPool(
        Func<Task<T>> connectionFactory,
        Func<T, Task<bool>> connectionValidator,
        Action<T>? connectionDisposer = null,
        int minSize = 2,
        int maxSize = 8,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(connectionValidator);

        if (minSize < 1 || maxSize < minSize)
            throw new ArgumentException("Invalid pool size configuration");

        _connectionFactory = connectionFactory;
        _connectionValidator = connectionValidator;
        _connectionDisposer = connectionDisposer;
        _minSize = minSize;
        _maxSize = maxSize;
        _logger = logger;
    }

    /// <summary>
    /// 初始化连接池
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Initializing connection pool with min size: {MinSize}", _minSize);

        var tasks = new List<Task>(_minSize);
        for (int i = 0; i < _minSize; i++)
        {
            tasks.Add(CreateConnectionAsync(cancellationToken));
        }

        await Task.WhenAll(tasks);
        _logger?.LogInformation("Connection pool initialized with {Count} connections", _currentSize);
    }

    /// <summary>
    /// 获取连接
    /// </summary>
    public async Task<PooledConnection<T>> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ConnectionPool<T>));

        var startTime = Environment.TickCount64;

        while (true)
        {
            // 尝试从可用连接中获取
            if (_availableConnections.TryTake(out var wrapper))
            {
                // 验证连接是否仍然有效
                if (await _connectionValidator(wrapper.Connection))
                {
                    wrapper.LastUsedTime = DateTime.UtcNow;
                    wrapper.TotalUsageCount++;

                    var acquisitionTime = Environment.TickCount64 - startTime;
                    _logger?.LogDebug("Connection acquired in {Time}ms", acquisitionTime);

                    return new PooledConnection<T>(wrapper.Connection, () => Release(wrapper));
                }
                else
                {
                    // 连接无效，销毁它
                    _logger?.LogWarning("Connection validation failed, disposing connection");
                    await DestroyConnectionAsync(wrapper);
                    continue;
                }
            }

            // 如果没有可用连接且未达到最大数量，创建新连接
            if (_currentSize < _maxSize)
            {
                try
                {
                    await CreateConnectionAsync(cancellationToken);
                    continue;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to create new connection");
                }
            }

            // 等待连接释放
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>
    /// 释放连接回池中
    /// </summary>
    private void Release(ConnectionWrapper wrapper)
    {
        if (_disposed)
        {
            _ = DestroyConnectionAsync(wrapper);
            return;
        }

        wrapper.LastReleasedTime = DateTime.UtcNow;
        _availableConnections.Add(wrapper);
        _logger?.LogDebug("Connection released back to pool");
    }

    /// <summary>
    /// 创建新连接
    /// </summary>
    private async Task CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var currentSize = Interlocked.Increment(ref _currentSize);

        try
        {
            var connection = await _connectionFactory();
            var wrapper = new ConnectionWrapper
            {
                Id = currentSize,
                Connection = connection,
                CreatedTime = DateTime.UtcNow,
                LastUsedTime = DateTime.UtcNow
            };

            _allConnections[wrapper.Id] = wrapper;
            _availableConnections.Add(wrapper);

            _logger?.LogInformation("Connection created, pool size: {Size}/{Max}", currentSize, _maxSize);
        }
        catch
        {
            Interlocked.Decrement(ref _currentSize);
            throw;
        }
    }

    /// <summary>
    /// 销毁连接
    /// </summary>
    private async Task DestroyConnectionAsync(ConnectionWrapper wrapper)
    {
        if (_allConnections.TryRemove(wrapper.Id, out _))
        {
            try
            {
                _connectionDisposer?.Invoke(wrapper.Connection);

                if (wrapper.Connection is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (wrapper.Connection is IDisposable disposable)
                    disposable.Dispose();

                Interlocked.Decrement(ref _currentSize);
                _logger?.LogInformation("Connection destroyed, pool size: {Size}", _currentSize);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disposing connection");
            }
        }
    }

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    public ConnectionPoolStats GetStats()
    {
        return new ConnectionPoolStats
        {
            TotalConnections = _currentSize,
            AvailableConnections = _availableConnections.Count,
            ActiveConnections = _currentSize - _availableConnections.Count,
            MaxConnections = _maxSize,
            MinConnections = _minSize
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var wrapper in _allConnections.Values)
        {
            _ = DestroyConnectionAsync(wrapper);
        }

        _allConnections.Clear();
        _availableConnections.Clear();

        _logger?.LogInformation("Connection pool disposed");
    }

    /// <summary>
    /// 连接包装器
    /// </summary>
    private class ConnectionWrapper
    {
        public int Id { get; set; }
        public required T Connection { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastUsedTime { get; set; }
        public DateTime LastReleasedTime { get; set; }
        public long TotalUsageCount { get; set; }
    }
}

/// <summary>
/// 池化连接包装器 - 自动归还连接
/// </summary>
public class PooledConnection<T> : IDisposable where T : class
{
    private readonly T _connection;
    private readonly Action _releaseAction;
    private bool _disposed;

    public PooledConnection(T connection, Action releaseAction)
    {
        _connection = connection;
        _releaseAction = releaseAction;
    }

    public T Connection => _disposed
        ? throw new ObjectDisposedException(nameof(PooledConnection<T>))
        : _connection;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _releaseAction();
    }
}

/// <summary>
/// 连接池统计信息
/// </summary>
public class ConnectionPoolStats
{
    public int TotalConnections { get; set; }
    public int AvailableConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int MaxConnections { get; set; }
    public int MinConnections { get; set; }

    public double UtilizationRate => TotalConnections > 0
        ? (double)ActiveConnections / TotalConnections
        : 0;
}
