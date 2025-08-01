using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Bson;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GameApp.Infrastructure.Performance
{
    /// <summary>
    /// 数据库性能优化服务
    /// </summary>
    public interface IDatabasePerformanceService
    {
        /// <summary>
        /// 优化MongoDB索引
        /// </summary>
        Task OptimizeMongoIndexesAsync();

        /// <summary>
        /// 清理过期的Redis数据
        /// </summary>
        Task CleanupExpiredRedisDataAsync();

        /// <summary>
        /// 获取数据库性能统计
        /// </summary>
        Task<DatabasePerformanceStats> GetDatabaseStatsAsync();

        /// <summary>
        /// 执行数据库性能基准测试
        /// </summary>
        Task<DatabaseBenchmarkResult> RunDatabaseBenchmarkAsync();
    }

    /// <summary>
    /// 数据库性能优化服务实现
    /// </summary>
    public class DatabasePerformanceService : IDatabasePerformanceService
    {
        private readonly ILogger<DatabasePerformanceService> _logger;
        private readonly IMongoDatabase? _mongoDatabase;
        private readonly IDatabase? _redisDatabase;
        private readonly IPerformanceService _performanceService;

        public DatabasePerformanceService(
            ILogger<DatabasePerformanceService> logger,
            IMongoDatabase? mongoDatabase,
            IDatabase? redisDatabase,
            IPerformanceService performanceService)
        {
            _logger = logger;
            _mongoDatabase = mongoDatabase;
            _redisDatabase = redisDatabase;
            _performanceService = performanceService;
        }

        public async Task OptimizeMongoIndexesAsync()
        {
            if (_mongoDatabase == null)
            {
                _logger.LogWarning("MongoDB数据库未配置，跳过索引优化");
                return;
            }

            try
            {
                _logger.LogInformation("开始优化MongoDB索引...");

                var stopwatch = Stopwatch.StartNew();

                // 用户集合索引优化
                await OptimizeUserIndexesAsync();

                // 游戏票据集合索引优化
                await OptimizeGameTicketIndexesAsync();

                // 区服信息集合索引优化
                await OptimizeZoneIndexesAsync();

                // 登录历史集合索引优化
                await OptimizeLoginHistoryIndexesAsync();

                stopwatch.Stop();

                await _performanceService.RecordMetricAsync("database.index_optimization", stopwatch.ElapsedMilliseconds);

                _logger.LogInformation("MongoDB索引优化完成，耗时: {Duration}ms", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MongoDB索引优化失败");
                await _performanceService.RecordErrorAsync("database.index_optimization", ex);
            }
        }

        public async Task CleanupExpiredRedisDataAsync()
        {
            if (_redisDatabase == null)
            {
                _logger.LogWarning("Redis数据库未配置，跳过数据清理");
                return;
            }

            try
            {
                _logger.LogInformation("开始清理过期Redis数据...");

                var stopwatch = Stopwatch.StartNew();
                var cleanedKeys = 0;

                // 清理过期的游戏票据
                cleanedKeys += await CleanupExpiredGameTicketsAsync();

                // 清理过期的登录尝试记录
                cleanedKeys += await CleanupExpiredLoginAttemptsAsync();

                // 清理过期的会话数据
                cleanedKeys += await CleanupExpiredSessionsAsync();

                stopwatch.Stop();

                await _performanceService.RecordMetricAsync("redis.cleanup", stopwatch.ElapsedMilliseconds);
                await _performanceService.RecordMetricAsync("redis.cleaned_keys", cleanedKeys);

                _logger.LogInformation("Redis数据清理完成，清理了 {CleanedKeys} 个键，耗时: {Duration}ms",
                    cleanedKeys, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis数据清理失败");
                await _performanceService.RecordErrorAsync("redis.cleanup", ex);
            }
        }

        public async Task<DatabasePerformanceStats> GetDatabaseStatsAsync()
        {
            var stats = new DatabasePerformanceStats();

            try
            {
                // MongoDB统计
                if (_mongoDatabase != null)
                {
                    stats.MongoStats = await GetMongoStatsAsync();
                }

                // Redis统计
                if (_redisDatabase != null)
                {
                    stats.RedisStats = await GetRedisStatsAsync();
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取数据库性能统计失败");
                await _performanceService.RecordErrorAsync("database.stats", ex);
                return stats;
            }
        }

        public async Task<DatabaseBenchmarkResult> RunDatabaseBenchmarkAsync()
        {
            var result = new DatabaseBenchmarkResult
            {
                StartTime = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation("开始数据库性能基准测试...");

                // MongoDB基准测试
                if (_mongoDatabase != null)
                {
                    result.MongoReadScore = await BenchmarkMongoReadAsync();
                    result.MongoWriteScore = await BenchmarkMongoWriteAsync();
                }

                // Redis基准测试
                if (_redisDatabase != null)
                {
                    result.RedisReadScore = await BenchmarkRedisReadAsync();
                    result.RedisWriteScore = await BenchmarkRedisWriteAsync();
                }

                result.EndTime = DateTime.UtcNow;
                result.TotalDuration = result.EndTime - result.StartTime;
                result.OverallScore = (result.MongoReadScore + result.MongoWriteScore +
                                     result.RedisReadScore + result.RedisWriteScore) / 4;

                _logger.LogInformation("数据库基准测试完成，总分: {Score}", result.OverallScore);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库基准测试失败");
                await _performanceService.RecordErrorAsync("database.benchmark", ex);
                result.EndTime = DateTime.UtcNow;
                result.TotalDuration = result.EndTime - result.StartTime;
                return result;
            }
        }

        #region Private Methods

        private async Task OptimizeUserIndexesAsync()
        {
            var collection = _mongoDatabase!.GetCollection<object>("users");
            var indexKeys = new List<CreateIndexModel<object>>
            {
                // 用户名唯一索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("username"),
                    new CreateIndexOptions { Unique = true, Name = "idx_username" }),

                // 邮箱唯一索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("email"),
                    new CreateIndexOptions { Unique = true, Name = "idx_email" }),

                // 创建时间索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Descending("createdAt"),
                    new CreateIndexOptions { Name = "idx_created_at" }),

                // 复合索引：状态 + 创建时间
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("status").Descending("createdAt"),
                    new CreateIndexOptions { Name = "idx_status_created" })
            };

            await collection.Indexes.CreateManyAsync(indexKeys);
        }

        private async Task OptimizeGameTicketIndexesAsync()
        {
            var collection = _mongoDatabase!.GetCollection<object>("game_tickets");
            var indexKeys = new List<CreateIndexModel<object>>
            {
                // 票据ID唯一索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("ticketId"),
                    new CreateIndexOptions { Unique = true, Name = "idx_ticket_id" }),

                // 用户ID索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("userId"),
                    new CreateIndexOptions { Name = "idx_user_id" }),

                // 过期时间索引（用于清理）
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("expiresAt"),
                    new CreateIndexOptions { Name = "idx_expires_at", ExpireAfter = TimeSpan.Zero })
            };

            await collection.Indexes.CreateManyAsync(indexKeys);
        }

        private async Task OptimizeZoneIndexesAsync()
        {
            var collection = _mongoDatabase!.GetCollection<object>("zones");
            var indexKeys = new List<CreateIndexModel<object>>
            {
                // 区服ID唯一索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("zoneId"),
                    new CreateIndexOptions { Unique = true, Name = "idx_zone_id" }),

                // 状态索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("status"),
                    new CreateIndexOptions { Name = "idx_status" }),

                // 复合索引：状态 + 优先级
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("status").Descending("priority"),
                    new CreateIndexOptions { Name = "idx_status_priority" })
            };

            await collection.Indexes.CreateManyAsync(indexKeys);
        }

        private async Task OptimizeLoginHistoryIndexesAsync()
        {
            var collection = _mongoDatabase!.GetCollection<object>("login_history");
            var indexKeys = new List<CreateIndexModel<object>>
            {
                // 用户ID + 时间复合索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("userId").Descending("loginTime"),
                    new CreateIndexOptions { Name = "idx_user_login_time" }),

                // IP地址索引
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Ascending("ipAddress"),
                    new CreateIndexOptions { Name = "idx_ip_address" }),

                // 登录时间索引（用于清理旧数据）
                new CreateIndexModel<object>(
                    Builders<object>.IndexKeys.Descending("loginTime"),
                    new CreateIndexOptions { Name = "idx_login_time" })
            };

            await collection.Indexes.CreateManyAsync(indexKeys);
        }

        private async Task<int> CleanupExpiredGameTicketsAsync()
        {
            var pattern = "game_ticket:*";
            var keys = await GetRedisKeysAsync(pattern);
            var cleanedCount = 0;

            foreach (var key in keys)
            {
                var ttl = await _redisDatabase!.KeyTimeToLiveAsync(key);
                if (!ttl.HasValue || ttl.Value < TimeSpan.Zero)
                {
                    await _redisDatabase.KeyDeleteAsync(key);
                    cleanedCount++;
                }
            }

            return cleanedCount;
        }

        private async Task<int> CleanupExpiredLoginAttemptsAsync()
        {
            var pattern = "login_attempts:*";
            var keys = await GetRedisKeysAsync(pattern);
            var cleanedCount = 0;

            foreach (var key in keys)
            {
                var ttl = await _redisDatabase!.KeyTimeToLiveAsync(key);
                if (!ttl.HasValue || ttl.Value < TimeSpan.Zero)
                {
                    await _redisDatabase.KeyDeleteAsync(key);
                    cleanedCount++;
                }
            }

            return cleanedCount;
        }

        private async Task<int> CleanupExpiredSessionsAsync()
        {
            var pattern = "session:*";
            var keys = await GetRedisKeysAsync(pattern);
            var cleanedCount = 0;

            foreach (var key in keys)
            {
                var ttl = await _redisDatabase!.KeyTimeToLiveAsync(key);
                if (!ttl.HasValue || ttl.Value < TimeSpan.Zero)
                {
                    await _redisDatabase.KeyDeleteAsync(key);
                    cleanedCount++;
                }
            }

            return cleanedCount;
        }

        private Task<List<RedisKey>> GetRedisKeysAsync(string pattern)
        {
            // 注意：在生产环境中，KEYS命令可能影响性能，考虑使用SCAN
            var connection = _redisDatabase!.Multiplexer;
            var server = connection.GetServer(connection.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern).Take(1000).ToList(); // 限制数量
            return Task.FromResult(keys);
        }

        private async Task<MongoPerformanceStats> GetMongoStatsAsync()
        {
            var stats = new MongoPerformanceStats();

            try
            {
                // 获取数据库统计信息
                var command = new BsonDocument("dbStats", 1);
                var result = await _mongoDatabase!.RunCommandAsync<BsonDocument>(command);

                stats.DatabaseSize = result.GetValue("dataSize", 0).ToInt64();
                stats.IndexSize = result.GetValue("indexSize", 0).ToInt64();
                stats.CollectionCount = result.GetValue("collections", 0).ToInt32();
                stats.IndexCount = result.GetValue("indexes", 0).ToInt32();

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取MongoDB统计信息失败");
                return stats;
            }
        }

        private async Task<RedisPerformanceStats> GetRedisStatsAsync()
        {
            var stats = new RedisPerformanceStats();

            try
            {
                var connection = _redisDatabase!.Multiplexer;
                var server = connection.GetServer(connection.GetEndPoints().First());
                var info = await server.InfoAsync();

                foreach (var section in info)
                {
                    foreach (var item in section)
                    {
                        switch (item.Key.ToLowerInvariant())
                        {
                            case "used_memory":
                                if (long.TryParse(item.Value, out var memory))
                                    stats.UsedMemory = memory;
                                break;
                            case "keyspace_hits":
                                if (long.TryParse(item.Value, out var hits))
                                    stats.KeyspaceHits = hits;
                                break;
                            case "keyspace_misses":
                                if (long.TryParse(item.Value, out var misses))
                                    stats.KeyspaceMisses = misses;
                                break;
                            case "connected_clients":
                                if (int.TryParse(item.Value, out var clients))
                                    stats.ConnectedClients = clients;
                                break;
                        }
                    }
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取Redis统计信息失败");
                return stats;
            }
        }

        private async Task<double> BenchmarkMongoReadAsync()
        {
            try
            {
                var collection = _mongoDatabase!.GetCollection<object>("users");
                var stopwatch = Stopwatch.StartNew();

                // 执行多次读取操作
                for (int i = 0; i < 10; i++)
                {
                    await collection.Find(FilterDefinition<object>.Empty).Limit(10).ToListAsync();
                }

                stopwatch.Stop();
                return Math.Max(0, 100 - stopwatch.ElapsedMilliseconds / 10.0);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<double> BenchmarkMongoWriteAsync()
        {
            try
            {
                var collection = _mongoDatabase!.GetCollection<BsonDocument>("benchmark_test");
                var stopwatch = Stopwatch.StartNew();

                // 执行多次写入操作
                var documents = new List<BsonDocument>();
                for (int i = 0; i < 10; i++)
                {
                    documents.Add(new BsonDocument
                    {
                        ["test_id"] = Guid.NewGuid().ToString(),
                        ["timestamp"] = DateTime.UtcNow,
                        ["data"] = $"test_data_{i}"
                    });
                }

                await collection.InsertManyAsync(documents);

                // 清理测试数据
                await collection.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);

                stopwatch.Stop();
                return Math.Max(0, 100 - stopwatch.ElapsedMilliseconds / 10.0);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<double> BenchmarkRedisReadAsync()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // 执行多次读取操作
                for (int i = 0; i < 100; i++)
                {
                    await _redisDatabase!.StringGetAsync($"benchmark_test_{i}");
                }

                stopwatch.Stop();
                return Math.Max(0, 100 - stopwatch.ElapsedMilliseconds / 10.0);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<double> BenchmarkRedisWriteAsync()
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // 执行多次写入操作
                var tasks = new List<Task>();
                for (int i = 0; i < 100; i++)
                {
                    tasks.Add(_redisDatabase!.StringSetAsync($"benchmark_test_{i}", $"test_value_{i}", TimeSpan.FromMinutes(1)));
                }

                await Task.WhenAll(tasks);

                // 清理测试数据
                var deleteKeys = Enumerable.Range(0, 100).Select(i => (RedisKey)$"benchmark_test_{i}").ToArray();
                await _redisDatabase!.KeyDeleteAsync(deleteKeys);

                stopwatch.Stop();
                return Math.Max(0, 100 - stopwatch.ElapsedMilliseconds / 10.0);
            }
            catch
            {
                return 0;
            }
        }

        #endregion
    }

    // 数据模型
    public class DatabasePerformanceStats
    {
        public MongoPerformanceStats? MongoStats { get; set; }
        public RedisPerformanceStats? RedisStats { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class MongoPerformanceStats
    {
        public long DatabaseSize { get; set; }
        public long IndexSize { get; set; }
        public int CollectionCount { get; set; }
        public int IndexCount { get; set; }
    }

    public class RedisPerformanceStats
    {
        public long UsedMemory { get; set; }
        public long KeyspaceHits { get; set; }
        public long KeyspaceMisses { get; set; }
        public int ConnectedClients { get; set; }
        public double HitRatio => KeyspaceHits + KeyspaceMisses > 0 ?
            (double)KeyspaceHits / (KeyspaceHits + KeyspaceMisses) : 0;
    }

    public class DatabaseBenchmarkResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public double MongoReadScore { get; set; }
        public double MongoWriteScore { get; set; }
        public double RedisReadScore { get; set; }
        public double RedisWriteScore { get; set; }
        public double OverallScore { get; set; }
    }
}
