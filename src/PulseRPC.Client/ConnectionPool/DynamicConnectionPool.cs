using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using PulseRPC.Messaging;

namespace PulseRPC.Client.ConnectionPool;

/// <summary>
/// 动态大小连接池 - 根据需求动态调整连接数量
/// </summary>
public sealed class DynamicConnectionPool : ConnectionPool
{
    private readonly int _minSize;
    private readonly int _maxSize;
    private readonly TimeSpan _scaleUpThreshold;
    private readonly TimeSpan _scaleDownThreshold;
    private readonly Timer _scaleTimer;

    private volatile int _currentConnections;
    private volatile int _consecutiveHighLoad;
    private volatile int _consecutiveLowLoad;
    private readonly ConcurrentQueue<DateTime> _recentAcquisitions = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    public DynamicConnectionPool(
        string name,
        ConnectionDescriptor descriptor,
        ConnectionPoolOptions options,
        IConnectionManager connectionManager,
        ILogger<DynamicConnectionPool> logger)
        : base(name, descriptor, options, connectionManager, logger)
    {
        if (options.Strategy != PoolingStrategy.Dynamic)
        {
            throw new ArgumentException($"连接池策略必须是 Dynamic，当前: {options.Strategy}", nameof(options));
        }

        if (options.MinSize > options.MaxSize)
        {
            throw new ArgumentException("最小连接数不能大于最大连接数", nameof(options));
        }

        _minSize = options.MinSize;
        _maxSize = options.MaxSize;
        _scaleUpThreshold = TimeSpan.FromSeconds(5); // 连续5秒高负载则扩容
        _scaleDownThreshold = TimeSpan.FromMinutes(2); // 连续2分钟低负载则缩容

        // 创建扩缩容定时器，每30秒检查一次
        _scaleTimer = new Timer(CheckAndScale, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _logger.LogDebug("动态大小连接池已创建: {PoolName}, 最小: {MinSize}, 最大: {MaxSize}",
            name, _minSize, _maxSize);
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
        _logger.LogInformation("初始化动态大小连接池: {PoolName}, 最小: {MinSize}, 最大: {MaxSize}",
            Name, _minSize, _maxSize);

        // 调用基类初始化
        await base.InitializeAsync(cancellationToken);

        // 确保创建最小连接数
        await EnsureMinimumConnectionsAsync(cancellationToken);

        _logger.LogInformation("动态大小连接池初始化完成: {PoolName}, 当前连接数: {CurrentSize}", Name, CurrentSize);
    }

    /// <summary>
    /// 获取连接（重写以记录获取统计）
    /// </summary>
    public override async Task<IConnectionLease> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // 记录获取时间用于负载统计
        _recentAcquisitions.Enqueue(DateTime.UtcNow);

        // 清理过期的获取记录（保留最近30秒的记录）
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        while (_recentAcquisitions.TryPeek(out var acquisitionTime) && acquisitionTime < cutoff)
        {
            _recentAcquisitions.TryDequeue(out _);
        }

        return await base.AcquireAsync(timeout, cancellationToken);
    }

    /// <summary>
    /// 创建新连接
    /// </summary>
    protected override async Task<IClientChannel> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentConnections >= _maxSize)
        {
            throw new InvalidOperationException($"连接数已达到最大限制: {_maxSize}");
        }

        try
        {
            var connection = await _connectionManager.ConnectAsync(Descriptor, cancellationToken);
            Interlocked.Increment(ref _currentConnections);

            _logger.LogDebug("动态连接池创建连接: {PoolName}, 连接ID: {ConnectionId}, 当前连接数: {CurrentSize}/{MaxSize}",
                Name, connection.Id, _currentConnections, _maxSize);

            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态连接池创建连接失败: {PoolName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 检查是否可以创建新连接
    /// </summary>
    protected override async Task<bool> CanCreateNewConnectionAsync()
    {
        return _currentConnections < _maxSize;
    }

    /// <summary>
    /// 销毁连接
    /// </summary>
    protected override async Task DestroyConnectionAsync(PooledConnection connection, string reason)
    {
        await base.DestroyConnectionAsync(connection, reason);
        Interlocked.Decrement(ref _currentConnections);

        _logger.LogDebug("动态连接池销毁连接: {PoolName}, 连接ID: {ConnectionId}, 当前连接数: {CurrentSize}/{MaxSize}, 原因: {Reason}",
            Name, connection.Context.Id, _currentConnections, _maxSize, reason);
    }

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    public override async Task<int> CleanupIdleConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var cleanupCount = 0;
        var cutoffTime = DateTime.UtcNow - Options.IdleTimeout;
        var connectionsToRemove = new List<PooledConnection>();
        var validConnections = new List<PooledConnection>();

        // 找到需要清理的空闲连接，但要保证不低于最小连接数
        while (_availableConnections.TryDequeue(out var connection))
        {
            var shouldRemove = connection.LastUsedAt < cutoffTime &&
                             _currentConnections > _minSize &&
                             (validConnections.Count + _currentConnections - connectionsToRemove.Count - 1) >= _minSize;

            if (shouldRemove)
            {
                connectionsToRemove.Add(connection);
            }
            else
            {
                validConnections.Add(connection);
            }
        }

        // 将有效连接放回队列
        foreach (var connection in validConnections)
        {
            _availableConnections.Enqueue(connection);
        }

        // 清理空闲连接
        foreach (var connection in connectionsToRemove)
        {
            try
            {
                await DestroyConnectionAsync(connection, "空闲超时清理");
                cleanupCount++;
                _connectionSemaphore.Release();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理空闲连接失败: {ConnectionId}", connection.Context.Id);
            }
        }

        if (cleanupCount > 0)
        {
            _logger.LogInformation("动态连接池空闲连接清理完成: {PoolName}, 清理数量: {CleanupCount}, 当前连接数: {CurrentSize}",
                Name, cleanupCount, CurrentSize);
        }

        return cleanupCount;
    }

    /// <summary>
    /// 确保最小连接数
    /// </summary>
    private async Task EnsureMinimumConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var connectionsNeeded = _minSize - _currentConnections;
        if (connectionsNeeded <= 0)
            return;

        _logger.LogDebug("补充连接到最小数量: {PoolName}, 需要创建: {ConnectionsNeeded}", Name, connectionsNeeded);

        var tasks = new List<Task>();
        for (int i = 0; i < connectionsNeeded; i++)
        {
            tasks.Add(CreateAndAddConnectionAsync(cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogDebug("动态连接池补充完成: {PoolName}, 当前连接数: {CurrentSize}", Name, _currentConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补充连接到最小数量失败: {PoolName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 检查并执行扩缩容
    /// </summary>
    private async void CheckAndScale(object? state)
    {
        if (State != ConnectionPoolState.Running)
            return;

        try
        {
            await PerformScalingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态连接池扩缩容检查失败: {PoolName}", Name);
        }
    }

    /// <summary>
    /// 执行扩缩容检查
    /// </summary>
    private async Task PerformScalingAsync()
    {
        var currentLoad = CalculateCurrentLoad();
        var shouldScaleUp = ShouldScaleUp(currentLoad);
        var shouldScaleDown = ShouldScaleDown(currentLoad);

        if (shouldScaleUp)
        {
            await ScaleUpAsync();
        }
        else if (shouldScaleDown)
        {
            await ScaleDownAsync();
        }
        else
        {
            // 重置计数器
            _consecutiveHighLoad = 0;
            _consecutiveLowLoad = 0;
        }
    }

    /// <summary>
    /// 计算当前负载
    /// </summary>
    private double CalculateCurrentLoad()
    {
        var now = DateTime.UtcNow;
        var recentCount = 0;

        // 计算最近30秒的获取次数
        var cutoff = now.AddSeconds(-30);
        while (_recentAcquisitions.TryPeek(out var acquisitionTime))
        {
            if (acquisitionTime >= cutoff)
            {
                recentCount++;
                break;
            }
            _recentAcquisitions.TryDequeue(out _);
        }

        // 负载 = (获取频率 + 活跃连接比例) / 2
        var acquisitionRate = recentCount / 30.0; // 每秒获取次数
        var connectionUtilization = (double)ActiveConnections / _currentConnections;

        return (acquisitionRate + connectionUtilization) / 2.0;
    }

    /// <summary>
    /// 判断是否应该扩容
    /// </summary>
    private bool ShouldScaleUp(double currentLoad)
    {
        // 负载较高且还有扩容空间
        if (currentLoad > 0.8 && _currentConnections < _maxSize)
        {
            _consecutiveHighLoad++;
            _consecutiveLowLoad = 0;
            return _consecutiveHighLoad >= 3; // 连续3次检查都是高负载
        }

        _consecutiveHighLoad = 0;
        return false;
    }

    /// <summary>
    /// 判断是否应该缩容
    /// </summary>
    private bool ShouldScaleDown(double currentLoad)
    {
        // 负载较低且可以缩容
        if (currentLoad < 0.3 && _currentConnections > _minSize)
        {
            _consecutiveLowLoad++;
            _consecutiveHighLoad = 0;
            return _consecutiveLowLoad >= 6; // 连续6次检查都是低负载（3分钟）
        }

        _consecutiveLowLoad = 0;
        return false;
    }

    /// <summary>
    /// 扩容操作
    /// </summary>
    private async Task ScaleUpAsync()
    {
        var scaleAmount = Math.Min(2, _maxSize - _currentConnections); // 每次最多扩容2个连接
        if (scaleAmount <= 0)
            return;

        _logger.LogInformation("动态连接池开始扩容: {PoolName}, 扩容数量: {ScaleAmount}, 当前连接数: {CurrentSize}",
            Name, scaleAmount, _currentConnections);

        var tasks = new List<Task>();
        for (int i = 0; i < scaleAmount; i++)
        {
            tasks.Add(CreateAndAddConnectionAsync());
        }

        try
        {
            await Task.WhenAll(tasks);
            _consecutiveHighLoad = 0; // 重置计数器
            _logger.LogInformation("动态连接池扩容完成: {PoolName}, 当前连接数: {CurrentSize}", Name, _currentConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态连接池扩容失败: {PoolName}", Name);
        }
    }

    /// <summary>
    /// 缩容操作
    /// </summary>
    private async Task ScaleDownAsync()
    {
        var scaleAmount = Math.Min(1, _currentConnections - _minSize); // 每次最多缩容1个连接
        if (scaleAmount <= 0)
            return;

        _logger.LogInformation("动态连接池开始缩容: {PoolName}, 缩容数量: {ScaleAmount}, 当前连接数: {CurrentSize}",
            Name, scaleAmount, _currentConnections);

        var removedCount = 0;
        var connectionsToRemove = new List<PooledConnection>();

        // 从可用连接中移除最旧的连接
        var tempConnections = new List<PooledConnection>();
        while (_availableConnections.TryDequeue(out var connection) && removedCount < scaleAmount)
        {
            if (_currentConnections - removedCount > _minSize)
            {
                connectionsToRemove.Add(connection);
                removedCount++;
            }
            else
            {
                tempConnections.Add(connection);
            }
        }

        // 将剩余连接放回队列
        foreach (var connection in tempConnections)
        {
            _availableConnections.Enqueue(connection);
        }

        // 销毁选中的连接
        foreach (var connection in connectionsToRemove)
        {
            try
            {
                await DestroyConnectionAsync(connection, "动态缩容");
                _connectionSemaphore.Release();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "缩容时销毁连接失败: {ConnectionId}", connection.Context.Id);
            }
        }

        if (removedCount > 0)
        {
            _consecutiveLowLoad = 0; // 重置计数器
            _logger.LogInformation("动态连接池缩容完成: {PoolName}, 实际缩容: {RemovedCount}, 当前连接数: {CurrentSize}",
                Name, removedCount, _currentConnections);
        }
    }

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    public override ConnectionPoolStatistics GetStatistics()
    {
        var stats = base.GetStatistics();

        // 添加动态连接池特有的统计信息
        stats.TotalConnections = _currentConnections;

        return stats;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public override void Dispose()
    {
        _logger.LogInformation("关闭动态大小连接池: {PoolName}, 连接数: {CurrentSize}", Name, _currentConnections);

        // 停止扩缩容定时器
        _scaleTimer?.Dispose();

        base.Dispose();
    }
}
