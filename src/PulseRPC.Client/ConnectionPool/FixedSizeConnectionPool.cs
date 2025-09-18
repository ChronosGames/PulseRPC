using Microsoft.Extensions.Logging;

namespace PulseRPC.Client.Core.ConnectionPool;

/// <summary>
/// 固定大小连接池 - 维护固定数量的连接
/// </summary>
public sealed class FixedSizeConnectionPool : ConnectionPool
{
    private readonly int _fixedSize;
    private volatile int _currentConnections;

    /// <summary>
    /// 构造函数
    /// </summary>
    public FixedSizeConnectionPool(
        string name,
        ConnectionDescriptor descriptor,
        ConnectionPoolOptions options,
        IConnectionManager connectionManager,
        ILogger<FixedSizeConnectionPool> logger)
        : base(name, descriptor, options, connectionManager, logger)
    {
        if (options.Strategy != PoolingStrategy.FixedSize)
        {
            throw new ArgumentException($"连接池策略必须是 FixedSize，当前: {options.Strategy}", nameof(options));
        }

        if (options.MinSize != options.MaxSize)
        {
            throw new ArgumentException("固定大小连接池的最小和最大连接数必须相等", nameof(options));
        }

        _fixedSize = options.MaxSize;
        _logger.LogDebug("固定大小连接池已创建: {PoolName}, 大小: {Size}", name, _fixedSize);
    }

    /// <summary>
    /// 当前连接数
    /// </summary>
    public override int CurrentSize => _currentConnections;

    /// <summary>
    /// 初始化连接池
    /// </summary>
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("初始化固定大小连接池: {PoolName}, 大小: {Size}", Name, _fixedSize);

        // 调用基类初始化
        await base.InitializeAsync(cancellationToken);

        // 确保创建所有连接
        await EnsureMinimumConnectionsAsync(cancellationToken);

        _logger.LogInformation("固定大小连接池初始化完成: {PoolName}, 当前连接数: {CurrentSize}", Name, CurrentSize);
    }

    /// <summary>
    /// 创建新连接
    /// </summary>
    protected override async Task<IConnectionContext> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentConnections >= _fixedSize)
        {
            throw new InvalidOperationException($"连接数已达到固定大小限制: {_fixedSize}");
        }

        try
        {
            var connection = await _connectionManager.ConnectAsync(Descriptor, cancellationToken);
            Interlocked.Increment(ref _currentConnections);

            _logger.LogDebug("固定大小连接池创建连接: {PoolName}, 连接ID: {ConnectionId}, 当前连接数: {CurrentSize}",
                Name, connection.Id, _currentConnections);

            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "固定大小连接池创建连接失败: {PoolName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 检查是否可以创建新连接
    /// </summary>
    protected override Task<bool> CanCreateNewConnectionAsync()
    {
        return Task.FromResult(_currentConnections < _fixedSize);
    }

    /// <summary>
    /// 销毁连接
    /// </summary>
    protected override async Task DestroyConnectionAsync(PooledConnection connection, string reason)
    {
        await base.DestroyConnectionAsync(connection, reason);
        Interlocked.Decrement(ref _currentConnections);

        _logger.LogDebug("固定大小连接池销毁连接: {PoolName}, 连接ID: {ConnectionId}, 当前连接数: {CurrentSize}, 原因: {Reason}",
            Name, connection.Context.Id, _currentConnections, reason);

        // 固定大小连接池需要立即补充连接
        if (State == ConnectionPoolState.Running)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureMinimumConnectionsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "补充连接失败: {PoolName}", Name);
                }
            });
        }
    }

    /// <summary>
    /// 确保最小连接数
    /// </summary>
    private async Task EnsureMinimumConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var connectionsNeeded = _fixedSize - _currentConnections;
        if (connectionsNeeded <= 0)
            return;

        _logger.LogDebug("补充连接到固定大小: {PoolName}, 需要创建: {ConnectionsNeeded}", Name, connectionsNeeded);

        var tasks = new List<Task>();
        for (int i = 0; i < connectionsNeeded; i++)
        {
            tasks.Add(CreateAndAddConnectionAsync(cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogDebug("固定大小连接池补充完成: {PoolName}, 当前连接数: {CurrentSize}", Name, _currentConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补充连接到固定大小失败: {PoolName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 清理空闲连接 - 固定大小连接池不进行空闲清理
    /// </summary>
    public override async Task<int> CleanupIdleConnectionsAsync(CancellationToken cancellationToken = default)
    {
        // 固定大小连接池不清理空闲连接，只进行健康检查
        _logger.LogDebug("固定大小连接池跳过空闲连接清理: {PoolName}", Name);

        // 但是会清理无效连接
        var cleanupCount = 0;
        var connectionsToRemove = new List<PooledConnection>();
        var validConnections = new List<PooledConnection>();

        // 检查所有可用连接的健康状态
        while (_availableConnections.TryDequeue(out var connection))
        {
            if (await ValidateConnectionAsync(connection, cancellationToken))
            {
                validConnections.Add(connection);
            }
            else
            {
                connectionsToRemove.Add(connection);
            }
        }

        // 将有效连接放回队列
        foreach (var connection in validConnections)
        {
            _availableConnections.Enqueue(connection);
        }

        // 清理无效连接并补充新连接
        foreach (var connection in connectionsToRemove)
        {
            try
            {
                await DestroyConnectionAsync(connection, "健康检查失败");
                cleanupCount++;
                _connectionSemaphore.Release();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理无效连接失败: {ConnectionId}", connection.Context.Id);
            }
        }

        if (cleanupCount > 0)
        {
            _logger.LogInformation("固定大小连接池清理无效连接: {PoolName}, 清理数量: {CleanupCount}", Name, cleanupCount);

            // 补充连接到固定大小
            await EnsureMinimumConnectionsAsync(cancellationToken);
        }

        return cleanupCount;
    }

    /// <summary>
    /// 刷新连接池
    /// </summary>
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始刷新固定大小连接池: {PoolName}", Name);

        // 先调用基类的刷新逻辑
        await base.RefreshAsync(cancellationToken);

        // 确保连接数达到固定大小
        await EnsureMinimumConnectionsAsync(cancellationToken);

        _logger.LogInformation("固定大小连接池刷新完成: {PoolName}, 连接数: {CurrentSize}", Name, CurrentSize);
    }

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    public override ConnectionPoolStatistics GetStatistics()
    {
        var stats = base.GetStatistics();

        // 添加固定大小连接池特有的统计信息
        stats.TotalConnections = _currentConnections;

        return stats;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        _logger.LogInformation("关闭固定大小连接池: {PoolName}, 连接数: {CurrentSize}", Name, _currentConnections);
        base.Dispose();
    }
}
