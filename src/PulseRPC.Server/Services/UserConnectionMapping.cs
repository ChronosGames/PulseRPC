using System.Collections.Concurrent;

namespace PulseRPC.Server.Services;

/// <summary>
/// 用户-连接映射的默认实现
/// </summary>
/// <remarks>
/// <para>
/// 线程安全的实现，支持多设备登录场景（一个用户多个连接）。
/// </para>
/// <para>
/// <strong>内存管理</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>当用户的最后一个连接移除时，自动清理用户记录</description></item>
/// <item><description>使用 ConcurrentDictionary 确保线程安全</description></item>
/// </list>
/// </remarks>
public class UserConnectionMapping : IUserConnectionMapping
{
    // userId -> connectionIds
    private readonly ConcurrentDictionary<string, HashSet<string>> _userConnections = new();

    // connectionId -> userId
    private readonly ConcurrentDictionary<string, string> _connectionUsers = new();

    // 用于保护 HashSet 操作的锁
    private readonly ConcurrentDictionary<string, object> _userLocks = new();

    /// <inheritdoc/>
    public void Add(string userId, string connectionId)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentNullException(nameof(userId));
        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentNullException(nameof(connectionId));

        // 获取或创建用户锁
        var lockObj = _userLocks.GetOrAdd(userId, _ => new object());

        lock (lockObj)
        {
            // 获取或创建连接集合
            var connections = _userConnections.GetOrAdd(userId, _ => new HashSet<string>());
            connections.Add(connectionId);
        }

        // 记录反向映射
        _connectionUsers[connectionId] = userId;
    }

    /// <inheritdoc/>
    public void Remove(string userId, string connectionId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(connectionId))
            return;

        // 移除反向映射
        _connectionUsers.TryRemove(connectionId, out _);

        // 获取用户锁
        if (!_userLocks.TryGetValue(userId, out var lockObj))
            return;

        lock (lockObj)
        {
            if (_userConnections.TryGetValue(userId, out var connections))
            {
                connections.Remove(connectionId);

                // 如果用户没有连接了，清理记录
                if (connections.Count == 0)
                {
                    _userConnections.TryRemove(userId, out _);
                    _userLocks.TryRemove(userId, out _);
                }
            }
        }
    }

    /// <inheritdoc/>
    public string? RemoveByConnection(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return null;

        if (_connectionUsers.TryRemove(connectionId, out var userId))
        {
            Remove(userId, connectionId);
            return userId;
        }

        return null;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetConnections(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return Array.Empty<string>();

        if (_userConnections.TryGetValue(userId, out var connections))
        {
            // 获取用户锁以确保线程安全
            if (_userLocks.TryGetValue(userId, out var lockObj))
            {
                lock (lockObj)
                {
                    return connections.ToArray();
                }
            }
        }

        return Array.Empty<string>();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetConnections(IEnumerable<string> userIds)
    {
        var result = new HashSet<string>();

        foreach (var userId in userIds)
        {
            var connections = GetConnections(userId);
            foreach (var connectionId in connections)
            {
                result.Add(connectionId);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public string? GetUserId(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return null;

        return _connectionUsers.TryGetValue(connectionId, out var userId) ? userId : null;
    }

    /// <inheritdoc/>
    public bool IsUserOnline(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        if (_userConnections.TryGetValue(userId, out var connections))
        {
            if (_userLocks.TryGetValue(userId, out var lockObj))
            {
                lock (lockObj)
                {
                    return connections.Count > 0;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public int OnlineUserCount => _userConnections.Count;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetOnlineUsers()
    {
        return _userConnections.Keys.ToArray();
    }
}

