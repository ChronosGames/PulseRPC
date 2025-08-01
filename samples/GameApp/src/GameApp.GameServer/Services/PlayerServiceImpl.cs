using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using PulseRPC;
using GameApp.Shared.Services;
using GameApp.GameServer.Repositories;
using GameApp.GameServer.Services.Cache;

namespace GameApp.GameServer.Services;

/// <summary>
/// 玩家服务实现
/// </summary>
[Channel("TcpChannel")]
public class PlayerServiceImpl : IPulseService, IPlayerService, IPlayerServiceImpl
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IPlayerCacheService _playerCacheService;
    private readonly IPlayerEventPublisher _playerEventPublisher;
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<PlayerServiceImpl> _logger;

    public PlayerServiceImpl(
        IPlayerRepository playerRepository,
        IPlayerCacheService playerCacheService,
        IPlayerEventPublisher playerEventPublisher,
        IConnectionMultiplexer redis,
        ILogger<PlayerServiceImpl> logger)
    {
        _playerRepository = playerRepository;
        _playerCacheService = playerCacheService;
        _playerEventPublisher = playerEventPublisher;
        _redisDatabase = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// 玩家登录游戏服务器
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            _logger.LogInformation("Player login attempt with GameTicket: {GameTicket}",
                request.GameTicket[..Math.Min(20, request.GameTicket.Length)] + "...");

            // 1. 验证游戏票据
            var ticketValidation = await ValidateGameTicketAsync(request.GameTicket);
            if (!ticketValidation.IsValid)
            {
                _logger.LogWarning("Invalid game ticket provided");
                return new LoginResponse
                {
                    Success = false,
                    Message = "游戏票据无效或已过期"
                };
            }

            var userId = ticketValidation.UserId;

            // 2. 查找或创建玩家角色
            var player = await _playerRepository.GetPlayerByUserIdAsync(userId);
            if (player == null)
            {
                _logger.LogInformation("Creating new player for user: {UserId}", userId);
                player = await CreateNewPlayerAsync(userId, ticketValidation.Username);
            }

            // 3. 检查玩家状态
            if (player.Status?.Health <= 0)
            {
                // 玩家已死亡，复活到安全区域
                await RespawnPlayerAsync(player);
            }

            // 4. 更新玩家在线状态
            await _playerCacheService.SetPlayerOnlineAsync(player.PlayerId, true);
            await _playerRepository.UpdateLastActiveTimeAsync(player.PlayerId, DateTime.UtcNow);

            // 5. 创建会话
            var sessionId = Guid.NewGuid().ToString();
            await CreatePlayerSessionAsync(player.PlayerId, sessionId, request.DeviceId);

            _logger.LogInformation("Player login successful: {CharacterName} (ID: {PlayerId})",
                player.CharacterName, player.PlayerId);

            return new LoginResponse
            {
                Success = true,
                Message = "登录成功",
                SessionId = sessionId,
                PlayerInfo = player,
                WorldInfo = await GetPlayerWorldInfoAsync(player.Position.WorldId),
                ServerInfo = new ServerInfo
                {
                    ServerId = "gameserver-dev",
                    ServerName = "GameServer Development",
                    Version = "1.0.0",
                    ServerTime = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during player login");
            return new LoginResponse
            {
                Success = false,
                Message = "登录失败，请稍后重试"
            };
        }
    }

    /// <summary>
    /// 获取玩家信息
    /// </summary>
    public async Task<PlayerInfo> GetPlayerInfoAsync(GetPlayerInfoRequest request)
    {
        try
        {
            _logger.LogDebug("Getting player info for: {PlayerId}", request.PlayerId);

            // 先从缓存获取
            var cachedPlayer = await _playerCacheService.GetPlayerInfoAsync(request.PlayerId);
            if (cachedPlayer != null)
            {
                return cachedPlayer;
            }

            // 从数据库获取
            var player = await _playerRepository.GetPlayerByIdAsync(request.PlayerId);
            if (player == null)
            {
                _logger.LogWarning("Player not found: {PlayerId}", request.PlayerId);
                return new PlayerInfo(); // 返回空对象而不是 null
            }

            // 缓存玩家信息
            await _playerCacheService.CachePlayerInfoAsync(player);

            return player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player info: {PlayerId}", request.PlayerId);
            return new PlayerInfo();
        }
    }

    /// <summary>
    /// 更新玩家位置 - 使用 KCP 通道
    /// </summary>
    [Channel("KcpChannel")]
    public async Task UpdatePositionAsync(UpdatePositionRequest request)
    {
        try
        {
            var playerId = request.PlayerId;
            var position = request.Position;

            // 验证位置数据
            if (!IsValidPosition(position))
            {
                _logger.LogWarning("Invalid position data for player: {PlayerId}", playerId);
                return;
            }

            // 更新缓存中的位置
            await _playerCacheService.UpdatePlayerPositionAsync(playerId, position);

            // 定期同步到数据库（不是每次都写）
            var lastDbSync = await _playerCacheService.GetLastPositionSyncAsync(playerId);
            if (DateTime.UtcNow - lastDbSync > TimeSpan.FromSeconds(10))
            {
                await _playerRepository.UpdatePlayerPositionAsync(playerId, position);
                await _playerCacheService.SetLastPositionSyncAsync(playerId, DateTime.UtcNow);
            }

            // 发布位置更新事件给附近玩家
            var moveEvent = new PlayerMovedEvent
            {
                PlayerId = playerId,
                Position = position,
                Speed = CalculatePlayerSpeed(playerId, position),
                IsRunning = position.Rotation > 0, // 简化判断
                Timestamp = DateTime.UtcNow
            };

            await _playerEventPublisher.PublishPlayerMovedAsync(position.WorldId, moveEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating player position: {PlayerId}", request.PlayerId);
        }
    }

    /// <summary>
    /// 玩家登出
    /// </summary>
    public async Task LogoutAsync(LogoutRequest request)
    {
        try
        {
            var playerId = request.PlayerId;
            _logger.LogInformation("Player logout: {PlayerId}, Reason: {Reason}", playerId, request.Reason);

            // 保存玩家数据到数据库
            var player = await _playerCacheService.GetPlayerInfoAsync(playerId);
            if (player != null)
            {
                await _playerRepository.SavePlayerDataAsync(player);
            }

            // 更新离线状态
            await _playerCacheService.SetPlayerOnlineAsync(playerId, false);
            await _playerRepository.UpdateLastActiveTimeAsync(playerId, DateTime.UtcNow);

            // 清理会话
            await ClearPlayerSessionAsync(playerId);

            // 通知其他玩家该玩家离开
            if (player != null)
            {
                var leftEvent = new PlayerLeftEvent
                {
                    WorldId = player.Position.WorldId,
                    PlayerId = playerId,
                    PlayerName = player.CharacterName,
                    Reason = request.Reason,
                    Timestamp = DateTime.UtcNow
                };

                // 这里需要通过世界服务发布事件
                // await _worldEventPublisher.PublishPlayerLeftAsync(player.Position.WorldId, leftEvent);
            }

            _logger.LogInformation("Player logout completed: {PlayerId}", playerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during player logout: {PlayerId}", request.PlayerId);
        }
    }

    /// <summary>
    /// 获取玩家统计信息
    /// </summary>
    public async Task<PlayerStatistics> GetStatisticsAsync(GetStatisticsRequest request)
    {
        try
        {
            var statistics = await _playerRepository.GetPlayerStatisticsAsync(request.PlayerId);
            if (statistics == null)
            {
                return new PlayerStatistics
                {
                    PlayerId = request.PlayerId,
                    TotalPlayTime = 0,
                    MonstersKilled = 0,
                    QuestsCompleted = 0,
                    DeathCount = 0,
                    LoginDays = 0,
                    LastActiveTime = DateTime.UtcNow
                };
            }

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting player statistics: {PlayerId}", request.PlayerId);
            return new PlayerStatistics { PlayerId = request.PlayerId };
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 验证游戏票据
    /// </summary>
    private async Task<GameTicketValidationResult> ValidateGameTicketAsync(string gameTicket)
    {
        try
        {
            // 这里应该调用 AuthServer 的票据验证服务
            // 简化实现：从 Redis 中验证票据
            var ticketKey = $"game_ticket:{gameTicket}";
            var ticketData = await _redisDatabase.StringGetAsync(ticketKey);

            if (!ticketData.HasValue)
            {
                return new GameTicketValidationResult { IsValid = false };
            }

            // 解析票据数据（简化实现）
            // 实际应该解析 JWT 或加密票据
            return new GameTicketValidationResult
            {
                IsValid = true,
                UserId = 1001, // 从票据中解析
                Username = "testuser" // 从票据中解析
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating game ticket");
            return new GameTicketValidationResult { IsValid = false };
        }
    }

    /// <summary>
    /// 创建新玩家
    /// </summary>
    private async Task<PlayerInfo> CreateNewPlayerAsync(int userId, string username)
    {
        var player = new PlayerInfo
        {
            PlayerId = await GenerateNewPlayerIdAsync(),
            UserId = userId,
            CharacterName = username + "_" + Random.Shared.Next(1000, 9999),
            Class = "warrior", // 默认职业
            Level = 1,
            Experience = 0,
            Attributes = new PlayerAttributes
            {
                Strength = 10,
                Agility = 10,
                Intelligence = 10,
                Constitution = 10,
                Luck = 10
            },
            Status = new PlayerStatus
            {
                Health = 100,
                MaxHealth = 100,
                Mana = 50,
                MaxMana = 50,
                Stamina = 100,
                MaxStamina = 100
            },
            Position = new PlayerPosition
            {
                WorldId = "world001", // 默认新手村
                MapId = "map_001",
                X = 1000,
                Y = 0,
                Z = 1000,
                Rotation = 0,
                LastUpdate = DateTime.UtcNow
            }
        };

        await _playerRepository.CreatePlayerAsync(player);
        await _playerCacheService.CachePlayerInfoAsync(player);

        _logger.LogInformation("New player created: {CharacterName} (ID: {PlayerId})",
            player.CharacterName, player.PlayerId);

        return player;
    }

    /// <summary>
    /// 复活玩家
    /// </summary>
    private async Task RespawnPlayerAsync(PlayerInfo player)
    {
        player.Status!.Health = player.Status.MaxHealth;
        player.Status.Mana = player.Status.MaxMana;

        // 传送到安全区域
        player.Position.WorldId = "world001";
        player.Position.MapId = "map_001";
        player.Position.X = 1000;
        player.Position.Y = 0;
        player.Position.Z = 1000;
        player.Position.LastUpdate = DateTime.UtcNow;

        await _playerRepository.UpdatePlayerStatusAsync(player.PlayerId, player.Status);
        await _playerRepository.UpdatePlayerPositionAsync(player.PlayerId, player.Position);

        _logger.LogInformation("Player respawned: {PlayerId}", player.PlayerId);
    }

    /// <summary>
    /// 创建玩家会话
    /// </summary>
    private async Task CreatePlayerSessionAsync(int playerId, string sessionId, string deviceId)
    {
        var sessionKey = $"player_session:{playerId}";
        var sessionData = new
        {
            SessionId = sessionId,
            DeviceId = deviceId,
            LoginTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        await _redisDatabase.StringSetAsync(sessionKey,
            System.Text.Json.JsonSerializer.Serialize(sessionData),
            TimeSpan.FromHours(24));
    }

    /// <summary>
    /// 清理玩家会话
    /// </summary>
    private async Task ClearPlayerSessionAsync(int playerId)
    {
        var sessionKey = $"player_session:{playerId}";
        await _redisDatabase.KeyDeleteAsync(sessionKey);
    }

    /// <summary>
    /// 获取玩家世界信息
    /// </summary>
    private async Task<WorldInfo> GetPlayerWorldInfoAsync(string worldId)
    {
        // 这里应该调用世界服务获取世界信息
        // 简化实现
        return new WorldInfo
        {
            WorldId = worldId,
            Name = "新手村",
            CurrentPlayers = 10,
            MaxPlayers = 1000,
            Status = "active"
        };
    }

    /// <summary>
    /// 生成新的玩家ID
    /// </summary>
    private async Task<int> GenerateNewPlayerIdAsync()
    {
        var counterKey = "player_id_counter";
        var newId = await _redisDatabase.StringIncrementAsync(counterKey);
        return (int)newId + 10000; // 从 10001 开始
    }

    /// <summary>
    /// 验证位置数据是否有效
    /// </summary>
    private bool IsValidPosition(PlayerPosition position)
    {
        // 简单的位置验证
        return !string.IsNullOrEmpty(position.WorldId) &&
               !string.IsNullOrEmpty(position.MapId) &&
               Math.Abs(position.X) < 10000 &&
               Math.Abs(position.Z) < 10000;
    }

    /// <summary>
    /// 计算玩家移动速度
    /// </summary>
    private float CalculatePlayerSpeed(int playerId, PlayerPosition currentPosition)
    {
        // 这里可以实现基于上次位置的速度计算
        // 简化实现
        return 5.0f;
    }

    #endregion
}

/// <summary>
/// 游戏票据验证结果
/// </summary>
public class GameTicketValidationResult
{
    public bool IsValid { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
}
