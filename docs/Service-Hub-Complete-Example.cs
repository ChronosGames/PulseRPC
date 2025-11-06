// ============================================================================
// PulseRPC.Server 完整示例代码
// ============================================================================
// 这个文件包含三个真实场景的完整实现：
// 1. 聊天室系统（ChatRoom）
// 2. 游戏房间系统（GameRoom）
// 3. 分布式缓存系统（DistributedCache）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.ServiceManagement;

// ============================================================================
// 场景 1：聊天室系统
// ============================================================================

namespace ChatRoomExample
{
    // ------------------------------------------------------------------------
    // 领域模型
    // ------------------------------------------------------------------------

    public record Message
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string UserId { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    }

    public record RoomInfo
    {
        public string RoomId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int ParticipantCount { get; init; }
        public int MessageCount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastActivityAt { get; init; }
    }

    // ------------------------------------------------------------------------
    // 持久化接口
    // ------------------------------------------------------------------------

    public interface IChatRepository
    {
        Task<List<Message>> LoadMessagesAsync(string roomId, CancellationToken cancellationToken = default);
        Task SaveMessagesAsync(string roomId, IEnumerable<Message> messages, CancellationToken cancellationToken = default);
        Task<bool> IsConnectedAsync();
    }

    // 内存实现（示例）
    public class InMemoryChatRepository : IChatRepository
    {
        private readonly Dictionary<string, List<Message>> _storage = new();

        public Task<List<Message>> LoadMessagesAsync(string roomId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_storage.GetValueOrDefault(roomId, new List<Message>()));
        }

        public Task SaveMessagesAsync(string roomId, IEnumerable<Message> messages, CancellationToken cancellationToken = default)
        {
            _storage[roomId] = messages.ToList();
            return Task.CompletedTask;
        }

        public Task<bool> IsConnectedAsync() => Task.FromResult(true);
    }

    // ------------------------------------------------------------------------
    // Service：状态容器
    // ------------------------------------------------------------------------

    public class ChatRoomService : IPulseService, IServiceLifecycle, IDisposable
    {
        public string ServiceName => "ChatRoom";
        public string ServiceId { get; }

        // 业务状态
        private readonly List<Message> _messages = new();
        private readonly HashSet<string> _participants = new();
        private readonly HashSet<string> _bannedUsers = new();
        private DateTimeOffset _createdAt;
        private DateTimeOffset _lastActivityAt;

        // 依赖
        private readonly string _roomId;
        private readonly ILogger<ChatRoomService> _logger;
        private readonly IChatRepository _repository;

        // 配置
        private const int MaxMessages = 10000;
        private const int MaxParticipants = 1000;

        public ChatRoomService(
            string roomId,
            ILogger<ChatRoomService> logger,
            IChatRepository repository)
        {
            _roomId = roomId;
            ServiceId = $"ChatRoom:{roomId}";
            _logger = logger;
            _repository = repository;
            _createdAt = DateTimeOffset.UtcNow;
            _lastActivityAt = _createdAt;

            _logger.LogDebug("ChatRoomService created: {ServiceId}", ServiceId);
        }

        // --------------------------------------------------------------------
        // 公共方法（供 Hub 调用）
        // --------------------------------------------------------------------

        public void Join(string userId, string userName)
        {
            if (_participants.Count >= MaxParticipants)
                throw new InvalidOperationException("Room is full");

            _participants.Add(userId);
            _lastActivityAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "User joined: RoomId={RoomId}, UserId={UserId}, UserName={UserName}",
                _roomId, userId, userName);
        }

        public void Leave(string userId)
        {
            _participants.Remove(userId);
            _lastActivityAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "User left: RoomId={RoomId}, UserId={UserId}",
                _roomId, userId);
        }

        public void AddMessage(Message message)
        {
            if (_bannedUsers.Contains(message.UserId))
                throw new InvalidOperationException($"User {message.UserId} is banned");

            if (_messages.Count >= MaxMessages)
                throw new InvalidOperationException("Room has reached maximum message count");

            _messages.Add(message);
            _lastActivityAt = DateTimeOffset.UtcNow;

            _logger.LogDebug(
                "Message added: RoomId={RoomId}, MessageId={MessageId}, UserId={UserId}",
                _roomId, message.Id, message.UserId);
        }

        public void BanUser(string userId)
        {
            _bannedUsers.Add(userId);
            _participants.Remove(userId); // 同时移除参与者

            _logger.LogWarning(
                "User banned: RoomId={RoomId}, UserId={UserId}",
                _roomId, userId);
        }

        public IReadOnlyList<Message> GetMessages() => _messages;

        public IReadOnlyCollection<string> GetParticipants() => _participants;

        public RoomInfo GetRoomInfo()
        {
            return new RoomInfo
            {
                RoomId = _roomId,
                Name = $"Room {_roomId}",
                ParticipantCount = _participants.Count,
                MessageCount = _messages.Count,
                CreatedAt = _createdAt,
                LastActivityAt = _lastActivityAt
            };
        }

        // --------------------------------------------------------------------
        // 生命周期钩子
        // --------------------------------------------------------------------

        public async Task OnActivateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Activating ChatRoom: {ServiceId}", ServiceId);

            try
            {
                // 从数据库加载历史消息
                var messages = await _repository.LoadMessagesAsync(_roomId, cancellationToken);
                _messages.AddRange(messages);

                _logger.LogInformation(
                    "ChatRoom activated: {ServiceId}, LoadedMessages={MessageCount}",
                    ServiceId, messages.Count);
            }
            catch (Exception ex)
            {
                // 降级处理：加载失败时使用空状态
                _logger.LogError(ex,
                    "Failed to load messages for {ServiceId}, starting with empty state",
                    ServiceId);
                // 不抛出异常，允许实例继续创建
            }
        }

        public async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Deactivating ChatRoom: {ServiceId}", ServiceId);

            try
            {
                // 保存消息到数据库
                await _repository.SaveMessagesAsync(_roomId, _messages, cancellationToken);

                _logger.LogInformation(
                    "ChatRoom deactivated: {ServiceId}, SavedMessages={MessageCount}",
                    ServiceId, _messages.Count);
            }
            catch (Exception ex)
            {
                // 记录错误但不抛出异常
                _logger.LogError(ex,
                    "Failed to save messages for {ServiceId}",
                    ServiceId);
            }
        }

        public async Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // 检查多个维度
                var memoryOk = _messages.Count < MaxMessages;
                var participantsOk = _participants.Count <= MaxParticipants;
                var connectionOk = await _repository.IsConnectedAsync();

                var isHealthy = memoryOk && participantsOk && connectionOk;

                if (!isHealthy)
                {
                    _logger.LogWarning(
                        "Health check failed for {ServiceId}: Memory={MemoryOk}, Participants={ParticipantsOk}, Connection={ConnectionOk}",
                        ServiceId, memoryOk, participantsOk, connectionOk);
                }

                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check exception for {ServiceId}", ServiceId);
                return false;
            }
        }

        public void Dispose()
        {
            _logger.LogDebug("ChatRoomService disposed: {ServiceId}", ServiceId);
        }
    }

    // ------------------------------------------------------------------------
    // Hub 1：用户接口（普通权限）
    // ------------------------------------------------------------------------

    public class ChatRoomUserHub : IPulseHub
    {
        private readonly IPulseServiceFactory<ChatRoomService> _factory;
        private readonly ILogger<ChatRoomUserHub> _logger;

        public ChatRoomUserHub(
            IPulseServiceFactory<ChatRoomService> factory,
            ILogger<ChatRoomUserHub> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task JoinRoomAsync(string roomId, string userId, string userName)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("RoomId is required", nameof(roomId));

            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("UserId is required", nameof(userId));

            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            service.Join(userId, userName);
        }

        public async Task LeaveRoomAsync(string roomId, string userId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("RoomId is required", nameof(roomId));

            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            service.Leave(userId);
        }

        public async Task<Message> SendMessageAsync(string roomId, string text, string userId, string userName)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text is required", nameof(text));

            if (text.Length > 1000)
                throw new ArgumentException("Text too long (max 1000 characters)", nameof(text));

            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");

            var message = new Message
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                UserName = userName,
                Text = text,
                Timestamp = DateTimeOffset.UtcNow
            };

            service.AddMessage(message);
            return message;
        }

        public async Task<Message[]> GetMessagesAsync(string roomId, int limit = 100)
        {
            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            var messages = service.GetMessages();

            // 返回最新的 N 条消息
            return messages
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .OrderBy(m => m.Timestamp)
                .ToArray();
        }

        public async Task<RoomInfo> GetRoomInfoAsync(string roomId)
        {
            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            return service.GetRoomInfo();
        }
    }

    // ------------------------------------------------------------------------
    // Hub 2：管理员接口（高级权限）
    // ------------------------------------------------------------------------

    public class ChatRoomAdminHub : IPulseHub
    {
        private readonly IPulseServiceFactory<ChatRoomService> _factory;
        private readonly ILogger<ChatRoomAdminHub> _logger;

        public ChatRoomAdminHub(
            IPulseServiceFactory<ChatRoomService> factory,
            ILogger<ChatRoomAdminHub> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task BanUserAsync(string roomId, string userId)
        {
            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            service.BanUser(userId);

            _logger.LogWarning(
                "User banned by admin: RoomId={RoomId}, UserId={UserId}",
                roomId, userId);
        }

        public async Task<Message[]> GetAllMessagesAsync(string roomId)
        {
            // 管理员可以看到所有消息（不限制数量）
            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            return service.GetMessages().ToArray();
        }

        public async Task<string[]> GetParticipantsAsync(string roomId)
        {
            var service = await _factory.GetOrCreateAsync($"ChatRoom:{roomId}");
            return service.GetParticipants().ToArray();
        }

        public async Task<bool> RemoveRoomAsync(string roomId)
        {
            // 管理员可以删除房间
            return await _factory.RemoveAsync($"ChatRoom:{roomId}");
        }
    }

    // ------------------------------------------------------------------------
    // DI 注册
    // ------------------------------------------------------------------------

    public static class ChatRoomServiceExtensions
    {
        public static IServiceCollection AddChatRoomServices(this IServiceCollection services)
        {
            // 注册 Repository
            services.AddSingleton<IChatRepository, InMemoryChatRepository>();

            // 注册 ServiceFactory
            services.AddPulseServiceFactory<ChatRoomService>(
                serviceFactory: (sp, serviceId) =>
                {
                    var roomId = serviceId.Split(':')[1];
                    return new ChatRoomService(
                        roomId,
                        sp.GetRequiredService<ILogger<ChatRoomService>>(),
                        sp.GetRequiredService<IChatRepository>());
                },
                configureOptions: options =>
                {
                    options.IdleTimeout = TimeSpan.FromMinutes(10);
                    options.MaxCachedInstances = 5000;
                    options.EnableHealthCheck = true;
                    options.HealthCheckInterval = TimeSpan.FromSeconds(30);
                });

            // 注册 Hub
            services.AddSingleton<ChatRoomUserHub>();
            services.AddSingleton<ChatRoomAdminHub>();

            return services;
        }
    }
}

// ============================================================================
// 场景 2：游戏房间系统
// ============================================================================

namespace GameRoomExample
{
    // ------------------------------------------------------------------------
    // 领域模型
    // ------------------------------------------------------------------------

    public enum GamePhase
    {
        Waiting,
        Playing,
        Finished
    }

    public record Player
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Score { get; set; }
        public bool IsReady { get; set; }
    }

    public record GameState
    {
        public string GameId { get; init; } = string.Empty;
        public GamePhase Phase { get; init; }
        public int RoundNumber { get; init; }
        public IReadOnlyList<Player> Players { get; init; } = Array.Empty<Player>();
        public DateTimeOffset StartedAt { get; init; }
    }

    // ------------------------------------------------------------------------
    // Service：游戏状态管理
    // ------------------------------------------------------------------------

    public class GameRoomService : IPulseService, IServiceLifecycle
    {
        public string ServiceName => "GameRoom";
        public string ServiceId { get; }

        private readonly string _gameId;
        private readonly List<Player> _players = new();
        private GamePhase _phase = GamePhase.Waiting;
        private int _roundNumber = 0;
        private DateTimeOffset _startedAt;

        private readonly ILogger<GameRoomService> _logger;

        private const int MinPlayers = 2;
        private const int MaxPlayers = 8;

        public GameRoomService(string gameId, ILogger<GameRoomService> logger)
        {
            _gameId = gameId;
            ServiceId = $"GameRoom:{gameId}";
            _logger = logger;
        }

        // --------------------------------------------------------------------
        // 游戏逻辑
        // --------------------------------------------------------------------

        public void AddPlayer(Player player)
        {
            if (_phase != GamePhase.Waiting)
                throw new InvalidOperationException("Cannot add player after game started");

            if (_players.Count >= MaxPlayers)
                throw new InvalidOperationException("Game is full");

            if (_players.Any(p => p.Id == player.Id))
                throw new InvalidOperationException("Player already in game");

            _players.Add(player);

            _logger.LogInformation(
                "Player added: GameId={GameId}, PlayerId={PlayerId}, PlayerCount={PlayerCount}",
                _gameId, player.Id, _players.Count);
        }

        public void RemovePlayer(string playerId)
        {
            _players.RemoveAll(p => p.Id == playerId);

            _logger.LogInformation(
                "Player removed: GameId={GameId}, PlayerId={PlayerId}",
                _gameId, playerId);

            // 如果游戏进行中，玩家数不足，结束游戏
            if (_phase == GamePhase.Playing && _players.Count < MinPlayers)
            {
                _phase = GamePhase.Finished;
                _logger.LogWarning("Game finished due to insufficient players: GameId={GameId}", _gameId);
            }
        }

        public void SetPlayerReady(string playerId, bool isReady)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
                throw new InvalidOperationException("Player not found");

            player.IsReady = isReady;
        }

        public void StartGame()
        {
            if (_phase != GamePhase.Waiting)
                throw new InvalidOperationException("Game already started");

            if (_players.Count < MinPlayers)
                throw new InvalidOperationException($"Need at least {MinPlayers} players to start");

            if (!_players.All(p => p.IsReady))
                throw new InvalidOperationException("Not all players are ready");

            _phase = GamePhase.Playing;
            _roundNumber = 1;
            _startedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Game started: GameId={GameId}, PlayerCount={PlayerCount}", _gameId, _players.Count);
        }

        public void NextRound()
        {
            if (_phase != GamePhase.Playing)
                throw new InvalidOperationException("Game is not in playing phase");

            _roundNumber++;

            _logger.LogInformation("Round started: GameId={GameId}, Round={Round}", _gameId, _roundNumber);
        }

        public void UpdateScore(string playerId, int score)
        {
            var player = _players.FirstOrDefault(p => p.Id == playerId);
            if (player == null)
                throw new InvalidOperationException("Player not found");

            player.Score += score;
        }

        public void FinishGame()
        {
            if (_phase != GamePhase.Playing)
                throw new InvalidOperationException("Game is not in playing phase");

            _phase = GamePhase.Finished;

            _logger.LogInformation(
                "Game finished: GameId={GameId}, Duration={Duration}, Rounds={Rounds}",
                _gameId, DateTimeOffset.UtcNow - _startedAt, _roundNumber);
        }

        public GameState GetGameState()
        {
            return new GameState
            {
                GameId = _gameId,
                Phase = _phase,
                RoundNumber = _roundNumber,
                Players = _players.ToArray(),
                StartedAt = _startedAt
            };
        }

        // --------------------------------------------------------------------
        // 生命周期钩子
        // --------------------------------------------------------------------

        public Task OnActivateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GameRoom activated: {ServiceId}", ServiceId);
            return Task.CompletedTask;
        }

        public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GameRoom deactivated: {ServiceId}, Phase={Phase}", ServiceId, _phase);
            return Task.CompletedTask;
        }

        public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
        {
            // 如果游戏结束超过 5 分钟，标记为不健康（可以清理）
            if (_phase == GamePhase.Finished)
            {
                var timeSinceFinish = DateTimeOffset.UtcNow - _startedAt;
                return Task.FromResult(timeSinceFinish < TimeSpan.FromMinutes(5));
            }

            return Task.FromResult(true);
        }
    }

    // ------------------------------------------------------------------------
    // Hub：玩家接口
    // ------------------------------------------------------------------------

    public class GameRoomHub : IPulseHub
    {
        private readonly IPulseServiceFactory<GameRoomService> _factory;

        public GameRoomHub(IPulseServiceFactory<GameRoomService> factory)
        {
            _factory = factory;
        }

        public async Task<string> CreateGameAsync()
        {
            var gameId = Guid.NewGuid().ToString("N")[..8];
            // 创建游戏实例
            await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            return gameId;
        }

        public async Task JoinGameAsync(string gameId, Player player)
        {
            var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            service.AddPlayer(player);
        }

        public async Task LeaveGameAsync(string gameId, string playerId)
        {
            var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            service.RemovePlayer(playerId);
        }

        public async Task SetReadyAsync(string gameId, string playerId, bool isReady)
        {
            var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            service.SetPlayerReady(playerId, isReady);
        }

        public async Task StartGameAsync(string gameId)
        {
            var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            service.StartGame();
        }

        public async Task UpdateScoreAsync(string gameId, string playerId, int score)
        {
            var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            service.UpdateScore(playerId, score);
        }

        public async Task<GameState> GetGameStateAsync(string gameId)
        {
            var service = await _factory.GetOrCreateAsync($"GameRoom:{gameId}");
            return service.GetGameState();
        }
    }

    // ------------------------------------------------------------------------
    // DI 注册
    // ------------------------------------------------------------------------

    public static class GameRoomServiceExtensions
    {
        public static IServiceCollection AddGameRoomServices(this IServiceCollection services)
        {
            services.AddPulseServiceFactory<GameRoomService>(
                serviceFactory: (sp, serviceId) =>
                {
                    var gameId = serviceId.Split(':')[1];
                    return new GameRoomService(gameId, sp.GetRequiredService<ILogger<GameRoomService>>());
                },
                configureOptions: options =>
                {
                    options.IdleTimeout = TimeSpan.FromMinutes(15);
                    options.MaxCachedInstances = 1000;
                });

            services.AddSingleton<GameRoomHub>();

            return services;
        }
    }
}

// ============================================================================
// 场景 3：分布式缓存系统
// ============================================================================

namespace DistributedCacheExample
{
    // ------------------------------------------------------------------------
    // 领域模型
    // ------------------------------------------------------------------------

    public record CacheEntry<T>
    {
        public string Key { get; init; } = string.Empty;
        public T? Value { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }

    // ------------------------------------------------------------------------
    // Service：缓存分片
    // ------------------------------------------------------------------------

    public class CacheShardService : IPulseService
    {
        public string ServiceName => "CacheShard";
        public string ServiceId { get; }

        private readonly Dictionary<string, CacheEntry<object>> _cache = new();
        private readonly ILogger<CacheShardService> _logger;

        public CacheShardService(string shardId, ILogger<CacheShardService> logger)
        {
            ServiceId = $"CacheShard:{shardId}";
            _logger = logger;
        }

        public void Set<T>(string key, T value, TimeSpan expiration)
        {
            var entry = new CacheEntry<object>
            {
                Key = key,
                Value = value,
                ExpiresAt = DateTimeOffset.UtcNow + expiration
            };

            _cache[key] = entry;

            _logger.LogDebug("Cache set: Key={Key}, Expiration={Expiration}", key, expiration);
        }

        public bool TryGet<T>(string key, out T? value)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                value = (T?)entry.Value;
                return true;
            }

            value = default;
            return false;
        }

        public bool Remove(string key)
        {
            return _cache.Remove(key);
        }

        public void Clear()
        {
            _cache.Clear();
        }

        public void RemoveExpired()
        {
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.IsExpired)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.Remove(key);
            }

            if (expiredKeys.Count > 0)
            {
                _logger.LogInformation("Removed {Count} expired cache entries", expiredKeys.Count);
            }
        }

        public int Count => _cache.Count;
    }

    // ------------------------------------------------------------------------
    // Hub：缓存接口
    // ------------------------------------------------------------------------

    public class DistributedCacheHub : IPulseHub
    {
        private readonly IPulseServiceFactory<CacheShardService> _factory;
        private const int ShardCount = 16;

        public DistributedCacheHub(IPulseServiceFactory<CacheShardService> factory)
        {
            _factory = factory;
        }

        private string GetShardId(string key)
        {
            var hash = key.GetHashCode();
            var shardIndex = Math.Abs(hash % ShardCount);
            return $"shard-{shardIndex}";
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan expiration)
        {
            var shardId = GetShardId(key);
            var service = await _factory.GetOrCreateAsync($"CacheShard:{shardId}");
            service.Set(key, value, expiration);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var shardId = GetShardId(key);
            var service = await _factory.GetOrCreateAsync($"CacheShard:{shardId}");
            service.TryGet<T>(key, out var value);
            return value;
        }

        public async Task<bool> RemoveAsync(string key)
        {
            var shardId = GetShardId(key);
            var service = await _factory.GetOrCreateAsync($"CacheShard:{shardId}");
            return service.Remove(key);
        }

        public async Task ClearAllAsync()
        {
            var tasks = Enumerable.Range(0, ShardCount)
                .Select(async i =>
                {
                    var service = await _factory.GetOrCreateAsync($"CacheShard:shard-{i}");
                    service.Clear();
                });

            await Task.WhenAll(tasks);
        }
    }

    // ------------------------------------------------------------------------
    // DI 注册
    // ------------------------------------------------------------------------

    public static class DistributedCacheServiceExtensions
    {
        public static IServiceCollection AddDistributedCacheServices(this IServiceCollection services)
        {
            services.AddPulseServiceFactory<CacheShardService>();
            services.AddSingleton<DistributedCacheHub>();

            return services;
        }
    }
}

// ============================================================================
// 使用示例
// ============================================================================

namespace Examples
{
    public class UsageExamples
    {
        public static async Task ChatRoomExampleAsync(IServiceProvider serviceProvider)
        {
            var userHub = serviceProvider.GetRequiredService<ChatRoomExample.ChatRoomUserHub>();
            var adminHub = serviceProvider.GetRequiredService<ChatRoomExample.ChatRoomAdminHub>();

            // 用户加入房间
            await userHub.JoinRoomAsync("room-1", "user-1", "Alice");
            await userHub.JoinRoomAsync("room-1", "user-2", "Bob");

            // 发送消息
            await userHub.SendMessageAsync("room-1", "Hello, Bob!", "user-1", "Alice");
            await userHub.SendMessageAsync("room-1", "Hi, Alice!", "user-2", "Bob");

            // 查询消息
            var messages = await userHub.GetMessagesAsync("room-1");
            Console.WriteLine($"Total messages: {messages.Length}");

            // 管理员封禁用户
            await adminHub.BanUserAsync("room-1", "user-2");

            // Bob 尝试发送消息（会失败）
            try
            {
                await userHub.SendMessageAsync("room-1", "Can I still talk?", "user-2", "Bob");
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static async Task GameRoomExampleAsync(IServiceProvider serviceProvider)
        {
            var gameHub = serviceProvider.GetRequiredService<GameRoomExample.GameRoomHub>();

            // 创建游戏
            var gameId = await gameHub.CreateGameAsync();

            // 玩家加入
            await gameHub.JoinGameAsync(gameId, new GameRoomExample.Player { Id = "player-1", Name = "Alice" });
            await gameHub.JoinGameAsync(gameId, new GameRoomExample.Player { Id = "player-2", Name = "Bob" });

            // 设置准备状态
            await gameHub.SetReadyAsync(gameId, "player-1", true);
            await gameHub.SetReadyAsync(gameId, "player-2", true);

            // 开始游戏
            await gameHub.StartGameAsync(gameId);

            // 更新分数
            await gameHub.UpdateScoreAsync(gameId, "player-1", 100);
            await gameHub.UpdateScoreAsync(gameId, "player-2", 80);

            // 查询游戏状态
            var state = await gameHub.GetGameStateAsync(gameId);
            Console.WriteLine($"Game Phase: {state.Phase}, Round: {state.RoundNumber}");
        }

        public static async Task CacheExampleAsync(IServiceProvider serviceProvider)
        {
            var cacheHub = serviceProvider.GetRequiredService<DistributedCacheExample.DistributedCacheHub>();

            // 设置缓存
            await cacheHub.SetAsync("user:1", new { Id = 1, Name = "Alice" }, TimeSpan.FromMinutes(5));
            await cacheHub.SetAsync("user:2", new { Id = 2, Name = "Bob" }, TimeSpan.FromMinutes(5));

            // 获取缓存
            var user = await cacheHub.GetAsync<dynamic>("user:1");
            Console.WriteLine($"User: {user?.Name}");

            // 删除缓存
            await cacheHub.RemoveAsync("user:1");
        }
    }
}
