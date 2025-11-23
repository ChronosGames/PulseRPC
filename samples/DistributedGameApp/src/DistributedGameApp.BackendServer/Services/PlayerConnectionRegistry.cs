using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 玩家连接注册表 - 维护 PlayerId 到 ConnectionId 的映射
/// </summary>
/// <remarks>
/// 职责：
/// - 记录玩家登录时的连接信息
/// - 提供 PlayerId 到 ConnectionId 的快速查找
/// - 处理玩家断线重连（移除旧连接，添加新连接）
/// - 线程安全的并发访问
/// </remarks>
public class PlayerConnectionRegistry
{
    private readonly ConcurrentDictionary<string, string> _playerToConnection = new();
    private readonly ConcurrentDictionary<string, string> _connectionToPlayer = new();
    private readonly ILogger<PlayerConnectionRegistry> _logger;

    public PlayerConnectionRegistry(ILogger<PlayerConnectionRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 注册玩家连接
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <param name="connectionId">连接ID</param>
    public void RegisterPlayer(string playerId, string connectionId)
    {
        // 检查玩家是否已有连接（处理重连情况）
        if (_playerToConnection.TryGetValue(playerId, out var oldConnectionId))
        {
            // 移除旧连接映射
            _connectionToPlayer.TryRemove(oldConnectionId, out _);
            _logger.LogInformation(
                "玩家重连 - PlayerId: {PlayerId}, OldConnection: {OldConnectionId}, NewConnection: {NewConnectionId}",
                playerId, oldConnectionId, connectionId);
        }

        // 添加新映射
        _playerToConnection[playerId] = connectionId;
        _connectionToPlayer[connectionId] = playerId;

        _logger.LogDebug("注册玩家连接 - PlayerId: {PlayerId}, ConnectionId: {ConnectionId}",
            playerId, connectionId);
    }

    /// <summary>
    /// 注销玩家连接
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    public void UnregisterPlayer(string playerId)
    {
        if (_playerToConnection.TryRemove(playerId, out var connectionId))
        {
            _connectionToPlayer.TryRemove(connectionId, out _);
            _logger.LogDebug("注销玩家连接 - PlayerId: {PlayerId}, ConnectionId: {ConnectionId}",
                playerId, connectionId);
        }
    }

    /// <summary>
    /// 通过连接ID注销玩家
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    public void UnregisterByConnectionId(string connectionId)
    {
        if (_connectionToPlayer.TryRemove(connectionId, out var playerId))
        {
            _playerToConnection.TryRemove(playerId, out _);
            _logger.LogDebug("通过连接注销玩家 - ConnectionId: {ConnectionId}, PlayerId: {PlayerId}",
                connectionId, playerId);
        }
    }

    /// <summary>
    /// 获取玩家的连接ID
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <returns>连接ID，如果不存在则返回 null</returns>
    public string? GetConnectionId(string playerId)
    {
        return _playerToConnection.TryGetValue(playerId, out var connectionId) ? connectionId : null;
    }

    /// <summary>
    /// 获取连接对应的玩家ID
    /// </summary>
    /// <param name="connectionId">连接ID</param>
    /// <returns>玩家ID，如果不存在则返回 null</returns>
    public string? GetPlayerId(string connectionId)
    {
        return _connectionToPlayer.TryGetValue(connectionId, out var playerId) ? playerId : null;
    }

    /// <summary>
    /// 检查玩家是否在线
    /// </summary>
    /// <param name="playerId">玩家ID</param>
    /// <returns>是否在线</returns>
    public bool IsPlayerOnline(string playerId)
    {
        return _playerToConnection.ContainsKey(playerId);
    }

    /// <summary>
    /// 获取在线玩家数量
    /// </summary>
    public int GetOnlinePlayerCount()
    {
        return _playerToConnection.Count;
    }

    /// <summary>
    /// 获取所有在线玩家ID
    /// </summary>
    public IReadOnlyCollection<string> GetOnlinePlayerIds()
    {
        return _playerToConnection.Keys.ToList();
    }
}
