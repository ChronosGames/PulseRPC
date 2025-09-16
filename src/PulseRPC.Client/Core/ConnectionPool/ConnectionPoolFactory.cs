using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace PulseRPC.Client.Core.ConnectionPool;

/// <summary>
/// 连接池工厂实现
/// </summary>
public sealed class ConnectionPoolFactory : IConnectionPoolFactory, IDisposable
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ConnectionPoolFactory> _logger;
    private readonly ConcurrentDictionary<string, IConnectionPool> _pools = new();
    private volatile bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    public ConnectionPoolFactory(
        IConnectionManager connectionManager,
        ILoggerFactory? loggerFactory = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<ConnectionPoolFactory>();

        _logger.LogDebug("连接池工厂已创建");
    }

    /// <summary>
    /// 创建连接池
    /// </summary>
    public IConnectionPool CreatePool(
        string name,
        ConnectionDescriptor descriptor,
        ConnectionPoolOptions options,
        ILoggerFactory? loggerFactory = null)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("连接池名称不能为空", nameof(name));

        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));

        if (options == null)
            throw new ArgumentNullException(nameof(options));

        ThrowIfDisposed();

        // 验证连接描述符
        var validation = descriptor.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException($"连接描述符无效: {validation.GetErrorString()}", nameof(descriptor));
        }

        // 验证连接池选项
        ValidateConnectionPoolOptions(options);

        // 检查是否已存在同名连接池
        if (_pools.ContainsKey(name))
        {
            throw new InvalidOperationException($"连接池已存在: {name}");
        }

        var effectiveLoggerFactory = loggerFactory ?? _loggerFactory;
        IConnectionPool pool;

        try
        {
            pool = options.Strategy switch
            {
                PoolingStrategy.FixedSize => new FixedSizeConnectionPool(
                    name, descriptor, options, _connectionManager,
                    effectiveLoggerFactory.CreateLogger<FixedSizeConnectionPool>()),

                PoolingStrategy.Dynamic => new DynamicConnectionPool(
                    name, descriptor, options, _connectionManager,
                    effectiveLoggerFactory.CreateLogger<DynamicConnectionPool>()),

                _ => throw new NotSupportedException($"不支持的连接池策略: {options.Strategy}")
            };

            // 注册连接池
            if (!_pools.TryAdd(name, pool))
            {
                pool.Dispose();
                throw new InvalidOperationException($"注册连接池失败: {name}");
            }

            // 订阅连接池状态变化事件
            pool.StateChanged += OnPoolStateChanged;

            _logger.LogInformation("连接池已创建: {PoolName}, 策略: {Strategy}, 最小: {MinSize}, 最大: {MaxSize}",
                name, options.Strategy, options.MinSize, options.MaxSize);

            return pool;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建连接池失败: {PoolName}, 策略: {Strategy}", name, options.Strategy);
            throw;
        }
    }

    /// <summary>
    /// 获取连接池
    /// </summary>
    public IConnectionPool? GetPool(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        ThrowIfDisposed();

        return _pools.TryGetValue(name, out var pool) ? pool : null;
    }

    /// <summary>
    /// 移除连接池
    /// </summary>
    public async Task<bool> RemovePoolAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        ThrowIfDisposed();

        if (!_pools.TryRemove(name, out var pool))
        {
            return false;
        }

        try
        {
            // 取消事件订阅
            pool.StateChanged -= OnPoolStateChanged;

            // 关闭连接池
            if (pool.State == ConnectionPoolState.Running)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30秒超时

                // 等待连接池优雅关闭
                var shutdownTask = Task.Run(() => pool.Dispose(), cts.Token);
                await shutdownTask;
            }
            else
            {
                pool.Dispose();
            }

            _logger.LogInformation("连接池已移除: {PoolName}", name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除连接池失败: {PoolName}", name);

            // 确保连接池被释放
            try
            {
                pool.Dispose();
            }
            catch (Exception disposeEx)
            {
                _logger.LogError(disposeEx, "强制释放连接池失败: {PoolName}", name);
            }

            return false;
        }
    }

    /// <summary>
    /// 获取所有连接池
    /// </summary>
    public IReadOnlyList<IConnectionPool> GetAllPools()
    {
        ThrowIfDisposed();
        return _pools.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// 根据策略创建默认连接池选项
    /// </summary>
    public static ConnectionPoolOptions CreateDefaultOptions(PoolingStrategy strategy)
    {
        return strategy switch
        {
            PoolingStrategy.FixedSize => new ConnectionPoolOptions
            {
                Strategy = PoolingStrategy.FixedSize,
                MinSize = 5,
                MaxSize = 5,
                IdleTimeout = TimeSpan.FromMinutes(10),
                AcquisitionTimeout = TimeSpan.FromSeconds(30),
                ValidateOnAcquire = false,
                WarmUp = true,
                MaxConnectionAge = TimeSpan.FromHours(1)
            },

            PoolingStrategy.Dynamic => new ConnectionPoolOptions
            {
                Strategy = PoolingStrategy.Dynamic,
                MinSize = 2,
                MaxSize = 20,
                IdleTimeout = TimeSpan.FromMinutes(5),
                AcquisitionTimeout = TimeSpan.FromSeconds(30),
                ValidateOnAcquire = true,
                WarmUp = true,
                MaxConnectionAge = TimeSpan.FromMinutes(30)
            },

            _ => throw new NotSupportedException($"不支持的连接池策略: {strategy}")
        };
    }

    /// <summary>
    /// 获取工厂统计信息
    /// </summary>
    public ConnectionPoolFactoryStatistics GetStatistics()
    {
        ThrowIfDisposed();

        var pools = _pools.Values.ToList();
        var totalConnections = pools.Sum(p => p.CurrentSize);
        var activeConnections = pools.Sum(p => p.ActiveConnections);

        return new ConnectionPoolFactoryStatistics
        {
            TotalPools = pools.Count,
            TotalConnections = totalConnections,
            ActiveConnections = activeConnections,
            PoolsByStrategy = pools.GroupBy(p => p.Options.Strategy)
                .ToDictionary(g => g.Key, g => g.Count()),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 执行所有连接池的健康检查
    /// </summary>
    public async Task<IReadOnlyList<ConnectionPoolHealthResult>> CheckAllPoolsHealthAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var pools = _pools.Values.ToList();
        var healthCheckTasks = pools.Select(pool => pool.CheckHealthAsync(cancellationToken));

        try
        {
            var results = await Task.WhenAll(healthCheckTasks);
            return results.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行所有连接池健康检查失败");
            throw;
        }
    }

    /// <summary>
    /// 刷新所有连接池
    /// </summary>
    public async Task RefreshAllPoolsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var pools = _pools.Values.ToList();
        var refreshTasks = pools.Select(async pool =>
        {
            try
            {
                await pool.RefreshAsync(cancellationToken);
                _logger.LogDebug("连接池刷新完成: {PoolName}", pool.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新连接池失败: {PoolName}", pool.Name);
            }
        });

        await Task.WhenAll(refreshTasks);
        _logger.LogInformation("所有连接池刷新完成，连接池数量: {PoolCount}", pools.Count);
    }

    /// <summary>
    /// 验证连接池选项
    /// </summary>
    private static void ValidateConnectionPoolOptions(ConnectionPoolOptions options)
    {
        if (options.MinSize < 0)
            throw new ArgumentException("最小连接数不能小于0", nameof(options));

        if (options.MaxSize <= 0)
            throw new ArgumentException("最大连接数必须大于0", nameof(options));

        if (options.MinSize > options.MaxSize)
            throw new ArgumentException("最小连接数不能大于最大连接数", nameof(options));

        if (options.IdleTimeout <= TimeSpan.Zero)
            throw new ArgumentException("空闲超时时间必须大于0", nameof(options));

        if (options.AcquisitionTimeout <= TimeSpan.Zero)
            throw new ArgumentException("获取超时时间必须大于0", nameof(options));

        if (options.MaxConnectionAge <= TimeSpan.Zero)
            throw new ArgumentException("最大连接存活时间必须大于0", nameof(options));
    }

    /// <summary>
    /// 处理连接池状态变化事件
    /// </summary>
    private void OnPoolStateChanged(object? sender, ConnectionPoolStateChangedEventArgs e)
    {
        _logger.LogDebug("连接池状态变化: {PoolName}, {PreviousState} -> {CurrentState}, 原因: {Reason}",
            e.PoolName, e.PreviousState, e.CurrentState, e.Reason);

        // 如果连接池进入错误状态，记录错误
        if (e.CurrentState == ConnectionPoolState.Error && e.Exception != null)
        {
            _logger.LogError(e.Exception, "连接池进入错误状态: {PoolName}, 原因: {Reason}",
                e.PoolName, e.Reason);
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionPoolFactory));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("开始关闭连接池工厂，当前连接池数量: {PoolCount}", _pools.Count);

        // 关闭所有连接池
        var shutdownTasks = _pools.Values.Select(async pool =>
        {
            try
            {
                pool.StateChanged -= OnPoolStateChanged;
                await Task.Run(() => pool.Dispose());
                _logger.LogDebug("连接池已关闭: {PoolName}", pool.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭连接池失败: {PoolName}", pool.Name);
            }
        });

        try
        {
            Task.WaitAll(shutdownTasks.ToArray(), TimeSpan.FromSeconds(60));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待连接池关闭超时");
        }

        _pools.Clear();
        _logger.LogInformation("连接池工厂已关闭");
    }
}

/// <summary>
/// 连接池工厂统计信息
/// </summary>
public sealed class ConnectionPoolFactoryStatistics
{
    /// <summary>
    /// 连接池总数
    /// </summary>
    public int TotalPools { get; set; }

    /// <summary>
    /// 总连接数
    /// </summary>
    public int TotalConnections { get; set; }

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public int ActiveConnections { get; set; }

    /// <summary>
    /// 按策略分类的连接池数量
    /// </summary>
    public Dictionary<PoolingStrategy, int> PoolsByStrategy { get; set; } = new();

    /// <summary>
    /// 统计时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }
}