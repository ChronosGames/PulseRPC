using System.Collections.Concurrent;

namespace PulseRPC.Server.Services;

/// <summary>
/// 组管理器的默认实现
/// </summary>
/// <remarks>
/// <para>
/// 线程安全的实现，支持动态组成员管理。
/// </para>
/// <para>
/// <strong>内存管理</strong>：
/// </para>
/// <list type="bullet">
/// <item><description>当组的最后一个成员移除时，自动清理组记录</description></item>
/// <item><description>使用 ConcurrentDictionary 确保线程安全</description></item>
/// </list>
/// </remarks>
public class GroupManager : IGroupManager
{
    // groupName -> connectionIds
    private readonly ConcurrentDictionary<string, HashSet<string>> _groupConnections = new();

    // connectionId -> groupNames
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionGroups = new();

    // 用于保护 HashSet 操作的锁
    private readonly ConcurrentDictionary<string, object> _groupLocks = new();
    private readonly ConcurrentDictionary<string, object> _connectionLocks = new();

    /// <inheritdoc/>
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentNullException(nameof(connectionId));
        if (string.IsNullOrEmpty(groupName))
            throw new ArgumentNullException(nameof(groupName));

        // 添加到组
        var groupLock = _groupLocks.GetOrAdd(groupName, _ => new object());
        lock (groupLock)
        {
            var connections = _groupConnections.GetOrAdd(groupName, _ => new HashSet<string>());
            connections.Add(connectionId);
        }

        // 添加到连接的组列表
        var connectionLock = _connectionLocks.GetOrAdd(connectionId, _ => new object());
        lock (connectionLock)
        {
            var groups = _connectionGroups.GetOrAdd(connectionId, _ => new HashSet<string>());
            groups.Add(groupName);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(groupName))
            return Task.CompletedTask;

        // 从组移除
        if (_groupLocks.TryGetValue(groupName, out var groupLock))
        {
            lock (groupLock)
            {
                if (_groupConnections.TryGetValue(groupName, out var connections))
                {
                    connections.Remove(connectionId);

                    // 如果组为空，清理组记录
                    if (connections.Count == 0)
                    {
                        _groupConnections.TryRemove(groupName, out _);
                        _groupLocks.TryRemove(groupName, out _);
                    }
                }
            }
        }

        // 从连接的组列表移除
        if (_connectionLocks.TryGetValue(connectionId, out var connectionLock))
        {
            lock (connectionLock)
            {
                if (_connectionGroups.TryGetValue(connectionId, out var groups))
                {
                    groups.Remove(groupName);

                    // 如果连接没有组了，清理记录
                    if (groups.Count == 0)
                    {
                        _connectionGroups.TryRemove(connectionId, out _);
                        _connectionLocks.TryRemove(connectionId, out _);
                    }
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task RemoveFromAllGroupsAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        // 获取连接所属的所有组
        var groups = GetConnectionGroups(connectionId);

        // 从所有组中移除
        foreach (var groupName in groups)
        {
            await RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetGroupConnections(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
            return Array.Empty<string>();

        if (_groupConnections.TryGetValue(groupName, out var connections))
        {
            if (_groupLocks.TryGetValue(groupName, out var groupLock))
            {
                lock (groupLock)
                {
                    return connections.ToArray();
                }
            }
        }

        return Array.Empty<string>();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetGroupConnections(IEnumerable<string> groupNames)
    {
        var result = new HashSet<string>();

        foreach (var groupName in groupNames)
        {
            var connections = GetGroupConnections(groupName);
            foreach (var connectionId in connections)
            {
                result.Add(connectionId);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetConnectionGroups(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return Array.Empty<string>();

        if (_connectionGroups.TryGetValue(connectionId, out var groups))
        {
            if (_connectionLocks.TryGetValue(connectionId, out var connectionLock))
            {
                lock (connectionLock)
                {
                    return groups.ToArray();
                }
            }
        }

        return Array.Empty<string>();
    }

    /// <inheritdoc/>
    public bool IsInGroup(string connectionId, string groupName)
    {
        if (string.IsNullOrEmpty(connectionId) || string.IsNullOrEmpty(groupName))
            return false;

        if (_groupConnections.TryGetValue(groupName, out var connections))
        {
            if (_groupLocks.TryGetValue(groupName, out var groupLock))
            {
                lock (groupLock)
                {
                    return connections.Contains(connectionId);
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public int GetGroupSize(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
            return 0;

        if (_groupConnections.TryGetValue(groupName, out var connections))
        {
            if (_groupLocks.TryGetValue(groupName, out var groupLock))
            {
                lock (groupLock)
                {
                    return connections.Count;
                }
            }
        }

        return 0;
    }

    /// <inheritdoc/>
    public bool GroupExists(string groupName)
    {
        return !string.IsNullOrEmpty(groupName) && _groupConnections.ContainsKey(groupName);
    }
}

