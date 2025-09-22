using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using PulseRPC.Messaging;

namespace PulseRPC.Client.ConnectionPool;

/// <summary>
/// 连接池基类 - 提供公共的连接池管理功能
/// </summary>
public abstract class ConnectionPool : IConnectionPool
{
    protected readonly ILogger _logger;
    protected readonly IConnectionManager _connectionManager;
    internal readonly ConcurrentQueue<PooledConnection> _availableConnections = new();
    internal readonly ConcurrentDictionary<string, ConnectionLease> _activeLeases = new();
    protected readonly SemaphoreSlim _connectionSemaphore;
    protected readonly object _stateLock = new();

    private readonly Timer _cleanupTimer;
    private readonly ConnectionPoolStatistics _statistics;
    private readonly DateTime _createdAt;
    private volatile bool _disposed;
    private volatile ConnectionPoolState _state = ConnectionPoolState.Uninitialized;

    /// <summary>
    /// 连接池名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 连接池状态
    /// </summary>
    public ConnectionPoolState State => _state;

    /// <summary>
    /// 连接描述符
    /// </summary>
    public ConnectionDescriptor Descriptor { get; }

    /// <summary>
    /// 连接池配置选项
    /// </summary>
    public ConnectionPoolOptions Options { get; }

    /// <summary>
    /// 当前连接数
    /// </summary>
    public virtual int CurrentSize => _availableConnections.Count + _activeLeases.Count;

    /// <summary>
    /// 活跃连接数
    /// </summary>
    public virtual int ActiveConnections => _activeLeases.Count;

    /// <summary>
    /// 可用连接数
    /// </summary>
    public virtual int AvailableConnections => _availableConnections.Count;

    /// <summary>
    /// 连接池状态变化事件
    /// </summary>
    public event EventHandler<ConnectionPoolStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 连接获取事件
    /// </summary>
    public event EventHandler<ConnectionAcquiredEventArgs>? ConnectionAcquired;

    /// <summary>
    /// 连接释放事件
    /// </summary>
    public event EventHandler<ConnectionReleasedEventArgs>? ConnectionReleased;

    /// <summary>
    /// 构造函数
    /// </summary>
    protected ConnectionPool(
        string name,
        ConnectionDescriptor descriptor,
        ConnectionPoolOptions options,
        IConnectionManager connectionManager,
        ILogger logger)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? NullLogger.Instance;

        _connectionSemaphore = new SemaphoreSlim(options.MaxSize, options.MaxSize);
        _createdAt = DateTime.UtcNow;
        _statistics = new ConnectionPoolStatistics
        {
            PoolName = name,
            CreatedAt = _createdAt
        };

        // 创建定期清理定时器
        _cleanupTimer = new Timer(PerformCleanup, null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        _logger.LogDebug("连接池已创建: {PoolName}, 策略: {Strategy}, 最小连接数: {MinSize}, 最大连接数: {MaxSize}",
            name, options.Strategy, options.MinSize, options.MaxSize);
    }

    /// <summary>
    /// 初始化连接池
    /// </summary>
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_state != ConnectionPoolState.Uninitialized)
            {
                throw new InvalidOperationException($"连接池已初始化，当前状态: {_state}");
            }
            ChangeState(ConnectionPoolState.Initializing, "开始初始化");
        }

        try
        {
            _logger.LogInformation("开始初始化连接池: {PoolName}", Name);

            // 预热连接池
            if (Options.WarmUp && Options.MinSize > 0)
            {
                await WarmUpAsync(Options.MinSize, cancellationToken);
            }

            ChangeState(ConnectionPoolState.Running, "初始化完成");
            _logger.LogInformation("连接池初始化完成: {PoolName}, 当前连接数: {CurrentSize}", Name, CurrentSize);
        }
        catch (Exception ex)
        {
            ChangeState(ConnectionPoolState.Error, "初始化失败", ex);
            _logger.LogError(ex, "连接池初始化失败: {PoolName}", Name);
            throw;
        }
    }

    /// <summary>
    /// 获取连接（租借）
    /// </summary>
    public virtual async Task<IConnectionLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        return await AcquireAsync(Options.AcquireTimeout, cancellationToken);
    }

    /// <summary>
    /// 获取连接（租借）- 带超时
    /// </summary>
    public virtual async Task<IConnectionLease> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        var stopwatch = Stopwatch.StartNew();
        var lease = await TryAcquireAsync(timeout, cancellationToken);
        stopwatch.Stop();

        if (lease == null)
        {
            _statistics.FailedAcquisitions++;
            throw new TimeoutException($"获取连接超时: {timeout.TotalMilliseconds}ms");
        }

        // 更新统计信息
        _statistics.TotalAcquisitions++;
        _statistics.SuccessfulAcquisitions++;
        UpdateAcquisitionTime(stopwatch.Elapsed);

        // 触发事件
        ConnectionAcquired?.Invoke(this, new ConnectionAcquiredEventArgs
        {
            PoolName = Name,
            LeaseId = lease.LeaseId,
            ConnectionId = lease.Connection.Id,
            AcquisitionTime = stopwatch.Elapsed
        });

        _logger.LogDebug("连接获取成功: {PoolName}, 租借ID: {LeaseId}, 耗时: {Duration}ms",
            Name, lease.LeaseId, stopwatch.ElapsedMilliseconds);

        return lease;
    }

    /// <summary>
    /// 尝试获取连接（非阻塞）
    /// </summary>
    public virtual async Task<IConnectionLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        return await TryAcquireAsync(TimeSpan.Zero, cancellationToken);
    }

    /// <summary>
    /// 尝试获取连接（带超时）
    /// </summary>
    protected virtual async Task<IConnectionLease?> TryAcquireAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureRunning();

        // 首先尝试从可用连接中获取
        if (_availableConnections.TryDequeue(out var pooledConnection))
        {
            if (await ValidateConnectionAsync(pooledConnection, cancellationToken))
            {
                var lease = CreateLease(pooledConnection.Context);
                _activeLeases.TryAdd(lease.LeaseId, (ConnectionLease)lease);
                return lease;
            }
            else
            {
                // 连接无效，销毁它
                await DestroyConnectionAsync(pooledConnection, "连接验证失败");
            }
        }

        // 尝试创建新连接
        if (await CanCreateNewConnectionAsync())
        {
            if (timeout == TimeSpan.Zero)
            {
                // 非阻塞模式，尝试立即获取信号量
                if (await _connectionSemaphore.WaitAsync(0, cancellationToken))
                {
                    try
                    {
                        var newConnection = await CreateConnectionAsync(cancellationToken);
                        var lease = CreateLease(newConnection);
                        _activeLeases.TryAdd(lease.LeaseId, (ConnectionLease)lease);
                        return lease;
                    }
                    catch
                    {
                        _connectionSemaphore.Release();
                        throw;
                    }
                }
            }
            else
            {
                // 阻塞模式，等待信号量
                if (await _connectionSemaphore.WaitAsync(timeout, cancellationToken))
                {
                    try
                    {
                        var newConnection = await CreateConnectionAsync(cancellationToken);
                        var lease = CreateLease(newConnection);
                        _activeLeases.TryAdd(lease.LeaseId, (ConnectionLease)lease);
                        return lease;
                    }
                    catch
                    {
                        _connectionSemaphore.Release();
                        throw;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 释放连接（归还给连接池）
    /// </summary>
    public virtual async Task ReleaseAsync(IConnectionLease lease, CancellationToken cancellationToken = default)
    {
        if (lease == null)
            throw new ArgumentNullException(nameof(lease));

        ThrowIfDisposed();

        if (!_activeLeases.TryRemove(lease.LeaseId, out var activeLease))
        {
            _logger.LogWarning("尝试释放不存在的租借: {LeaseId}", lease.LeaseId);
            return;
        }

        var usageDuration = DateTime.UtcNow - lease.AcquiredAt;

        try
        {
            if (lease.IsValid && _state == ConnectionPoolState.Running)
            {
                // 连接有效，归还到连接池
                var pooledConnection = new PooledConnection(lease.Connection, DateTime.UtcNow);
                _availableConnections.Enqueue(pooledConnection);
                _logger.LogDebug("连接已归还到连接池: {PoolName}, 租借ID: {LeaseId}", Name, lease.LeaseId);
            }
            else
            {
                // 连接无效或连接池已关闭，销毁连接
                await DestroyConnectionAsync(new PooledConnection(lease.Connection, DateTime.UtcNow),
                    lease.IsValid ? "连接池已关闭" : "连接已标记为无效");
                _connectionSemaphore.Release();
            }

            // 触发事件
            ConnectionReleased?.Invoke(this, new ConnectionReleasedEventArgs
            {
                PoolName = Name,
                LeaseId = lease.LeaseId,
                ConnectionId = lease.Connection.Id,
                UsageDuration = usageDuration,
                Reason = lease.IsValid ? "正常释放" : "连接无效"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "释放连接时发生错误: {PoolName}, 租借ID: {LeaseId}", Name, lease.LeaseId);
            // 确保信号量被释放
            _connectionSemaphore.Release();
        }
        finally
        {
            // 释放租借资源
            lease.Dispose();
        }
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public virtual async Task<ConnectionPoolHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var checkStart = DateTime.UtcNow;
        var connectionResults = new List<HealthCheckResult>();

        try
        {
            // 检查所有活跃连接
            var activeTasks = _activeLeases.Values.Select(async lease =>
            {
                try
                {
                    var result = new HealthCheckResult
                    {
                        ConnectionId = lease.Connection.Id,
                        CheckedAt = DateTime.UtcNow
                    };

                    // 简单的健康检查 - 检查连接状态
                    var health = DetermineHealthFromState(lease.Connection.State);
                    result.Health = health;
                    result.Message = $"连接状态: {lease.Connection.State}";

                    return result;
                }
                catch (Exception ex)
                {
                    return new HealthCheckResult
                    {
                        ConnectionId = lease.Connection.Id,
                        Health = ConnectionHealth.Unhealthy,
                        CheckedAt = DateTime.UtcNow,
                        Exception = ex,
                        Message = $"健康检查异常: {ex.Message}"
                    };
                }
            });

            connectionResults.AddRange(await Task.WhenAll(activeTasks));

            var overallHealth = DetermineOverallHealth(connectionResults);

            return new ConnectionPoolHealthResult
            {
                PoolName = Name,
                OverallHealth = overallHealth,
                ConnectionResults = connectionResults.AsReadOnly(),
                CheckedAt = checkStart,
                Duration = DateTime.UtcNow - checkStart
            };
        }
        catch (Exception ex)
        {
            return new ConnectionPoolHealthResult
            {
                PoolName = Name,
                OverallHealth = ConnectionHealth.Unhealthy,
                ConnectionResults = connectionResults.AsReadOnly(),
                CheckedAt = checkStart,
                Duration = DateTime.UtcNow - checkStart,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 清理空闲连接
    /// </summary>
    public virtual async Task<int> CleanupIdleConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var cleanupCount = 0;
        var cutoffTime = DateTime.UtcNow - Options.IdleTimeout;
        var connectionsToRemove = new List<PooledConnection>();

        // 找到需要清理的空闲连接
        var tempConnections = new List<PooledConnection>();
        while (_availableConnections.TryDequeue(out var connection))
        {
            if (connection.LastUsedAt < cutoffTime && CurrentSize > Options.MinSize)
            {
                connectionsToRemove.Add(connection);
            }
            else
            {
                tempConnections.Add(connection);
            }
        }

        // 将有效连接放回队列
        foreach (var connection in tempConnections)
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
            _logger.LogInformation("连接池空闲连接清理完成: {PoolName}, 清理数量: {CleanupCount}", Name, cleanupCount);
        }

        return cleanupCount;
    }

    /// <summary>
    /// 获取连接池统计信息
    /// </summary>
    public virtual ConnectionPoolStatistics GetStatistics()
    {
        _statistics.Uptime = DateTime.UtcNow - _createdAt;
        _statistics.TotalConnections = CurrentSize;
        _statistics.ActiveConnections = ActiveConnections;
        _statistics.IdleConnections = AvailableConnections;
        _statistics.PendingRequests = Options.MaxSize - _connectionSemaphore.CurrentCount;
        _statistics.Timestamp = DateTime.UtcNow;

        return _statistics;
    }

    /// <summary>
    /// 刷新连接池（重新创建所有连接）
    /// </summary>
    public virtual async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _logger.LogInformation("开始刷新连接池: {PoolName}", Name);

        // 清理所有可用连接
        var connectionsToDestroy = new List<PooledConnection>();
        while (_availableConnections.TryDequeue(out var connection))
        {
            connectionsToDestroy.Add(connection);
        }

        // 销毁旧连接
        foreach (var connection in connectionsToDestroy)
        {
            await DestroyConnectionAsync(connection, "连接池刷新");
            _connectionSemaphore.Release();
        }

        // 预热新连接
        if (Options.WarmUp && Options.MinSize > 0)
        {
            await WarmUpAsync(Options.MinSize, cancellationToken);
        }

        _logger.LogInformation("连接池刷新完成: {PoolName}", Name);
    }

    /// <summary>
    /// 预热连接池
    /// </summary>
    protected virtual async Task WarmUpAsync(int connectionCount, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task>();
        for (int i = 0; i < connectionCount; i++)
        {
            tasks.Add(CreateAndAddConnectionAsync(cancellationToken));
        }

        await Task.WhenAll(tasks);
        _logger.LogDebug("连接池预热完成: {PoolName}, 连接数: {ConnectionCount}", Name, connectionCount);
    }

    /// <summary>
    /// 创建并添加连接到连接池
    /// </summary>
    protected virtual async Task CreateAndAddConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            var connection = await CreateConnectionAsync(cancellationToken);
            var pooledConnection = new PooledConnection(connection, DateTime.UtcNow);
            _availableConnections.Enqueue(pooledConnection);
            _statistics.ConnectionsCreated++;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// 创建新连接（抽象方法，由子类实现）
    /// </summary>
    protected abstract Task<IClientChannel> CreateConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否可以创建新连接（抽象方法，由子类实现）
    /// </summary>
    protected abstract Task<bool> CanCreateNewConnectionAsync();

    /// <summary>
    /// 验证连接是否有效
    /// </summary>
    protected virtual async Task<bool> ValidateConnectionAsync(PooledConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            // 检查连接状态
            if (connection.Context.State != ExtendedConnectionState.Connected &&
                connection.Context.State != ExtendedConnectionState.Active &&
                connection.Context.State != ExtendedConnectionState.Idle)
            {
                return false;
            }

            // 检查是否过期
            if (Options.ValidateOnAcquire)
            {
                var age = DateTime.UtcNow - connection.CreatedAt;
                if (age > Options.MaxConnectionAge)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 创建连接租借
    /// </summary>
    protected virtual IConnectionLease CreateLease(IClientChannel connection)
    {
        return new ConnectionLease(connection);
    }

    /// <summary>
    /// 销毁连接
    /// </summary>
    protected virtual async Task DestroyConnectionAsync(PooledConnection connection, string reason)
    {
        try
        {
            await _connectionManager.DisconnectAsync(connection.Context.Id);
            _statistics.ConnectionsDestroyed++;
            _logger.LogDebug("连接已销毁: {ConnectionId}, 原因: {Reason}", connection.Context.Id, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "销毁连接失败: {ConnectionId}", connection.Context.Id);
        }
    }

    /// <summary>
    /// 状态变化处理
    /// </summary>
    protected virtual void ChangeState(ConnectionPoolState newState, string reason, Exception? exception = null)
    {
        var previousState = _state;
        _state = newState;

        StateChanged?.Invoke(this, new ConnectionPoolStateChangedEventArgs
        {
            PoolName = Name,
            PreviousState = previousState,
            CurrentState = newState,
            Reason = reason,
            Exception = exception
        });

        _logger.LogDebug("连接池状态变化: {PoolName}, {PreviousState} -> {CurrentState}, 原因: {Reason}",
            Name, previousState, newState, reason);
    }

    /// <summary>
    /// 定期清理
    /// </summary>
    private async void PerformCleanup(object? state)
    {
        if (_disposed || _state != ConnectionPoolState.Running)
            return;

        try
        {
            await CleanupIdleConnectionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定期清理时发生错误: {PoolName}", Name);
        }
    }

    /// <summary>
    /// 更新获取时间统计
    /// </summary>
    private void UpdateAcquisitionTime(TimeSpan acquisitionTime)
    {
        if (acquisitionTime > _statistics.MaxAcquisitionTime)
        {
            _statistics.MaxAcquisitionTime = acquisitionTime;
        }

        // 简单的移动平均
        var totalTime = (_statistics.AverageAcquisitionTime?.TotalMilliseconds ?? 0.0f) * (_statistics.SuccessfulAcquisitions - 1) + acquisitionTime.TotalMilliseconds;
        _statistics.AverageAcquisitionTime = TimeSpan.FromMilliseconds(totalTime / _statistics.SuccessfulAcquisitions);
    }

    /// <summary>
    /// 根据连接状态确定健康状态
    /// </summary>
    private static ConnectionHealth DetermineHealthFromState(ExtendedConnectionState state)
    {
        return state switch
        {
            ExtendedConnectionState.Connected => ConnectionHealth.Healthy,
            ExtendedConnectionState.Active => ConnectionHealth.Healthy,
            ExtendedConnectionState.Idle => ConnectionHealth.Healthy,
            ExtendedConnectionState.Connecting => ConnectionHealth.Degraded,
            ExtendedConnectionState.Reconnecting => ConnectionHealth.Degraded,
            ExtendedConnectionState.Disconnecting => ConnectionHealth.Degraded,
            ExtendedConnectionState.Failed => ConnectionHealth.Unhealthy,
            ExtendedConnectionState.Disconnected => ConnectionHealth.Unhealthy,
            ExtendedConnectionState.Disposed => ConnectionHealth.Unhealthy,
            _ => ConnectionHealth.Unknown
        };
    }

    /// <summary>
    /// 确定整体健康状态
    /// </summary>
    private static ConnectionHealth DetermineOverallHealth(IReadOnlyList<HealthCheckResult> results)
    {
        if (results.Count == 0)
            return ConnectionHealth.Unknown;

        var healthyCount = results.Count(r => r.Health == ConnectionHealth.Healthy);
        var degradedCount = results.Count(r => r.Health == ConnectionHealth.Degraded);
        var unhealthyCount = results.Count(r => r.Health == ConnectionHealth.Unhealthy);

        if (unhealthyCount == results.Count)
            return ConnectionHealth.Unhealthy;

        if (healthyCount == results.Count)
            return ConnectionHealth.Healthy;

        return ConnectionHealth.Degraded;
    }

    /// <summary>
    /// 确保连接池处于运行状态
    /// </summary>
    protected void EnsureRunning()
    {
        if (_state != ConnectionPoolState.Running)
        {
            throw new InvalidOperationException($"连接池未处于运行状态，当前状态: {_state}");
        }
    }

    /// <summary>
    /// 检查是否已释放
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionPool));
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            ChangeState(ConnectionPoolState.Shutting, "连接池正在关闭");

            // 停止清理定时器
            _cleanupTimer?.Dispose();

            // 清理所有可用连接
            while (_availableConnections.TryDequeue(out var connection))
            {
                DestroyConnectionAsync(connection, "连接池关闭").Wait(TimeSpan.FromSeconds(5));
            }

            // 等待所有活跃租借完成
            var timeout = DateTime.UtcNow.AddSeconds(30);
            while (_activeLeases.Count > 0 && DateTime.UtcNow < timeout)
            {
                Thread.Sleep(100);
            }

            // 强制关闭剩余连接
            foreach (var lease in _activeLeases.Values)
            {
                try
                {
                    lease.MarkInvalid("连接池强制关闭");
                    DestroyConnectionAsync(new PooledConnection(lease.Connection, DateTime.UtcNow), "连接池强制关闭")
                        .Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "强制关闭连接失败: {ConnectionId}", lease.Connection.Id);
                }
            }

            _connectionSemaphore?.Dispose();
            ChangeState(ConnectionPoolState.Shutdown, "连接池已关闭");

            _logger.LogInformation("连接池已关闭: {PoolName}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭连接池时发生错误: {PoolName}", Name);
        }
    }
}

/// <summary>
/// 连接池中的连接包装
/// </summary>
public sealed class PooledConnection
{
    public IClientChannel Context { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastUsedAt { get; set; }

    public PooledConnection(IClientChannel context, DateTime lastUsedAt)
    {
        Context = context;
        CreatedAt = DateTime.UtcNow;
        LastUsedAt = lastUsedAt;
    }
}

/// <summary>
/// 连接租借实现
/// </summary>
internal sealed class ConnectionLease : IConnectionLease
{
    private volatile bool _isValid = true;
    private volatile bool _disposed;

    /// <summary>
    /// 租借ID
    /// </summary>
    public string LeaseId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 连接上下文
    /// </summary>
    public IClientChannel Connection { get; }

    /// <summary>
    /// 租借时间
    /// </summary>
    public DateTime AcquiredAt { get; }

    /// <summary>
    /// 最后使用时间
    /// </summary>
    public DateTime LastUsedAt { get; private set; }

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid => _isValid && !_disposed;

    /// <summary>
    /// 租借标签
    /// </summary>
    public Dictionary<string, object> Tags { get; } = new();

    public ConnectionLease(IClientChannel connection)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        AcquiredAt = DateTime.UtcNow;
        LastUsedAt = AcquiredAt;
    }

    /// <summary>
    /// 更新最后使用时间
    /// </summary>
    public void UpdateLastUsed()
    {
        if (!_disposed)
        {
            LastUsedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 标记为无效
    /// </summary>
    public void MarkInvalid(string reason)
    {
        _isValid = false;
        Tags["invalid_reason"] = reason;
        Tags["invalid_at"] = DateTime.UtcNow;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

    public override string ToString()
    {
        return $"Lease[{LeaseId}]: {Connection.Id} ({(IsValid ? "Valid" : "Invalid")})";
    }
}
