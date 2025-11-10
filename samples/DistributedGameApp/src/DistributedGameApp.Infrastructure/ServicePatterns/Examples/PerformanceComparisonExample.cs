using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.ServicePatterns.Examples;

/// <summary>
/// 性能对比示例 - 展示使用 ServiceBase 模式前后的性能差异
/// </summary>
public class PerformanceComparisonExample
{
    /// <summary>
    /// 错误示例: 直接访问外部资源，没有连接池和 Worker 隔离
    /// </summary>
    public class BadPlayerService
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, PlayerState> _players = new();

        public async Task UpdatePlayerPositionAsync(string playerId, float x, float y, float z)
        {
            lock (_lock)
            {
                var state = _players.GetValueOrDefault(playerId) ?? new PlayerState { PlayerId = playerId };
                state.X = x;
                state.Y = y;
                state.Z = z;
                state.LastUpdateTime = DateTime.UtcNow;
                _players[playerId] = state;
            }

            await Task.CompletedTask;
        }

        public PlayerState? GetPlayerState(string playerId)
        {
            lock (_lock)
            {
                return _players.GetValueOrDefault(playerId);
            }
        }
    }

    /// <summary>
    /// 正确示例: 使用 ShardedServiceBase 模式
    /// </summary>
    public class GoodPlayerService : ShardedServiceBase<PlayerShardContext>
    {
        public GoodPlayerService(ILogger<GoodPlayerService>? logger = null)
            : base(new ShardedServiceOptions { ShardCount = 8 }, logger)
        {
        }

        protected override Task<PlayerShardContext> CreateShardContextAsync(
            int shardId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PlayerShardContext
            {
                ShardId = shardId,
                Players = new System.Collections.Concurrent.ConcurrentDictionary<string, PlayerState>()
            });
        }

        public async Task UpdatePlayerPositionAsync(string playerId, float x, float y, float z)
        {
            await ExecuteAsync(playerId, async (ctx, ct) =>
            {
                var state = ctx.Players.GetOrAdd(playerId, _ => new PlayerState { PlayerId = playerId });
                state.X = x;
                state.Y = y;
                state.Z = z;
                state.LastUpdateTime = DateTime.UtcNow;
                await Task.CompletedTask;
            });
        }

        public async Task<PlayerState?> GetPlayerStateAsync(string playerId)
        {
            return await ExecuteAsync(playerId, async (ctx, ct) =>
            {
                ctx.Players.TryGetValue(playerId, out var state);
                return await Task.FromResult(state);
            });
        }
    }

    /// <summary>
    /// 运行性能对比测试
    /// </summary>
    public static async Task RunComparisonAsync()
    {
        const int playerCount = 1000;
        const int operationsPerPlayer = 100;
        const int concurrentTasks = 100;

        Console.WriteLine("===== 性能对比测试 =====\n");

        // 测试 1: 单线程性能
        Console.WriteLine("测试 1: 单线程性能 (1000个玩家 * 100次操作)");
        await TestBadServiceSingleThreadAsync(playerCount, operationsPerPlayer);
        await TestGoodServiceSingleThreadAsync(playerCount, operationsPerPlayer);

        Console.WriteLine("\n---\n");

        // 测试 2: 并发性能
        Console.WriteLine("测试 2: 高并发性能 (100个并发任务)");
        await TestBadServiceConcurrentAsync(playerCount, operationsPerPlayer, concurrentTasks);
        await TestGoodServiceConcurrentAsync(playerCount, operationsPerPlayer, concurrentTasks);
    }

    private static async Task TestBadServiceSingleThreadAsync(int playerCount, int operations)
    {
        var service = new BadPlayerService();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < playerCount; i++)
        {
            var playerId = $"player_{i}";
            for (int j = 0; j < operations; j++)
            {
                await service.UpdatePlayerPositionAsync(playerId, i, i, i);
            }
        }

        sw.Stop();
        Console.WriteLine($"[BadService 单线程] 总耗时: {sw.ElapsedMilliseconds}ms, " +
                         $"平均延迟: {sw.ElapsedMilliseconds / (double)(playerCount * operations):F3}ms/op");
    }

    private static async Task TestGoodServiceSingleThreadAsync(int playerCount, int operations)
    {
        var service = new GoodPlayerService();
        await service.InitializeAsync();

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < playerCount; i++)
        {
            var playerId = $"player_{i}";
            for (int j = 0; j < operations; j++)
            {
                await service.UpdatePlayerPositionAsync(playerId, i, i, i);
            }
        }

        sw.Stop();
        Console.WriteLine($"[GoodService 单线程] 总耗时: {sw.ElapsedMilliseconds}ms, " +
                         $"平均延迟: {sw.ElapsedMilliseconds / (double)(playerCount * operations):F3}ms/op");

        await service.DisposeAsync();
    }

    private static async Task TestBadServiceConcurrentAsync(int playerCount, int operations, int concurrentTasks)
    {
        var service = new BadPlayerService();
        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>();
        for (int t = 0; t < concurrentTasks; t++)
        {
            var taskId = t;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < playerCount / concurrentTasks; i++)
                {
                    var playerId = $"player_{taskId}_{i}";
                    for (int j = 0; j < operations; j++)
                    {
                        await service.UpdatePlayerPositionAsync(playerId, i, i, i);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        Console.WriteLine($"[BadService 并发] 总耗时: {sw.ElapsedMilliseconds}ms, " +
                         $"QPS: {playerCount * operations / (sw.ElapsedMilliseconds / 1000.0):F0}");
    }

    private static async Task TestGoodServiceConcurrentAsync(int playerCount, int operations, int concurrentTasks)
    {
        var service = new GoodPlayerService();
        await service.InitializeAsync();

        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>();
        for (int t = 0; t < concurrentTasks; t++)
        {
            var taskId = t;
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < playerCount / concurrentTasks; i++)
                {
                    var playerId = $"player_{taskId}_{i}";
                    for (int j = 0; j < operations; j++)
                    {
                        await service.UpdatePlayerPositionAsync(playerId, i, i, i);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        var stats = service.GetStats();
        Console.WriteLine($"[GoodService 并发] 总耗时: {sw.ElapsedMilliseconds}ms, " +
                         $"QPS: {playerCount * operations / (sw.ElapsedMilliseconds / 1000.0):F0}, " +
                         $"处理总数: {stats.TotalProcessedCount}, " +
                         $"错误数: {stats.TotalErrorCount}");

        // 打印分片负载分布
        Console.WriteLine("分片负载分布:");
        foreach (var (shardId, load) in stats.GetLoadDistribution())
        {
            Console.WriteLine($"  Shard {shardId}: {load} tasks");
        }

        await service.DisposeAsync();
    }
}

/// <summary>
/// 运行示例
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        await PerformanceComparisonExample.RunComparisonAsync();

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
}
