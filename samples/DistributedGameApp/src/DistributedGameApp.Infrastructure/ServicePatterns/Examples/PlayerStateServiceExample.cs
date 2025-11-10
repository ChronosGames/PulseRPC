using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DistributedGameApp.Infrastructure.ServicePatterns.Examples;

/// <summary>
/// 玩家状态服务示例 - 使用 ShardedServiceBase
///
/// 配置: 8个分片，每个分片独立状态
/// 场景: 玩家在线状态、位置、血量等实时数据
/// 优势: 避免锁竞争，提高缓存命中率
/// </summary>
public class PlayerStateServiceExample : ShardedServiceBase<PlayerShardContext>
{
    public PlayerStateServiceExample(
        ILogger<PlayerStateServiceExample>? logger = null)
        : base(new ShardedServiceOptions
        {
            ShardCount = 8  // 8个分片
        }, logger)
    {
    }

    /// <summary>
    /// 创建分片上下文
    /// </summary>
    protected override Task<PlayerShardContext> CreateShardContextAsync(
        int shardId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new PlayerShardContext
        {
            ShardId = shardId,
            Players = new ConcurrentDictionary<string, PlayerState>()
        });
    }

    // ==================== 业务方法 ====================

    /// <summary>
    /// 设置玩家在线状态
    /// </summary>
    public async Task SetPlayerOnlineAsync(
        string playerId,
        bool isOnline,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(playerId, async (ctx, ct) =>
        {
            var state = ctx.Players.GetOrAdd(playerId, _ => new PlayerState { PlayerId = playerId });
            state.IsOnline = isOnline;
            state.LastUpdateTime = DateTime.UtcNow;

            await Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    /// 更新玩家位置
    /// </summary>
    public async Task UpdatePlayerPositionAsync(
        string playerId,
        float x,
        float y,
        float z,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(playerId, async (ctx, ct) =>
        {
            var state = ctx.Players.GetOrAdd(playerId, _ => new PlayerState { PlayerId = playerId });
            state.X = x;
            state.Y = y;
            state.Z = z;
            state.LastUpdateTime = DateTime.UtcNow;

            await Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    /// 获取玩家状态
    /// </summary>
    public async Task<PlayerState?> GetPlayerStateAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(playerId, async (ctx, ct) =>
        {
            ctx.Players.TryGetValue(playerId, out var state);
            return await Task.FromResult(state);
        }, cancellationToken);
    }

    /// <summary>
    /// 更新玩家血量
    /// </summary>
    public async Task UpdatePlayerHpAsync(
        string playerId,
        int hp,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(playerId, async (ctx, ct) =>
        {
            var state = ctx.Players.GetOrAdd(playerId, _ => new PlayerState { PlayerId = playerId });
            state.Hp = Math.Max(0, hp);
            state.LastUpdateTime = DateTime.UtcNow;

            await Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>
    /// 获取分片内所有在线玩家
    /// </summary>
    public async Task<List<PlayerState>> GetOnlinePlayersInShardAsync(
        int shardId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(shardId, async (ctx, ct) =>
        {
            return await Task.FromResult(
                ctx.Players.Values
                    .Where(p => p.IsOnline)
                    .ToList()
            );
        }, cancellationToken);
    }

    /// <summary>
    /// 获取所有在线玩家数量（广播到所有分片）
    /// </summary>
    public async Task<int> GetTotalOnlinePlayersAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = new ConcurrentBag<int>();

        await BroadcastAsync(async (ctx, shardId, ct) =>
        {
            var count = ctx.Players.Values.Count(p => p.IsOnline);
            counts.Add(count);
            await Task.CompletedTask;
        }, cancellationToken);

        return counts.Sum();
    }

    /// <summary>
    /// 清理离线超过指定时间的玩家状态
    /// </summary>
    public async Task CleanupInactivePlayersAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await BroadcastAsync(async (ctx, shardId, ct) =>
        {
            var inactivePlayers = ctx.Players
                .Where(kvp => !kvp.Value.IsOnline && (now - kvp.Value.LastUpdateTime) > timeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var playerId in inactivePlayers)
            {
                ctx.Players.TryRemove(playerId, out _);
            }

            await Task.CompletedTask;
        }, cancellationToken);
    }
}

/// <summary>
/// 玩家分片上下文
/// </summary>
public class PlayerShardContext
{
    public required int ShardId { get; set; }
    public required ConcurrentDictionary<string, PlayerState> Players { get; set; }
}

/// <summary>
/// 玩家状态
/// </summary>
public class PlayerState
{
    public required string PlayerId { get; set; }
    public bool IsOnline { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Hp { get; set; } = 100;
    public int MaxHp { get; set; } = 100;
    public DateTime LastUpdateTime { get; set; }
}
