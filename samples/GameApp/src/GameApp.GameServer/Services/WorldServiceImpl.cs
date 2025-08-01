using Microsoft.Extensions.Logging;
using PulseRPC;
using GameApp.Shared.Services;
using GameApp.GameServer.Repositories;
using GameApp.GameServer.Services.Cache;

namespace GameApp.GameServer.Services;

/// <summary>
/// 世界服务实现
/// </summary>
[Channel("TcpChannel")]
public class WorldServiceImpl : IPulseService, IWorldService, IWorldServiceImpl
{
    private readonly IWorldRepository _worldRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IWorldCacheService _worldCacheService;
    private readonly IPlayerCacheService _playerCacheService;
    private readonly IWorldEventPublisher _worldEventPublisher;
    private readonly ILogger<WorldServiceImpl> _logger;

    public WorldServiceImpl(
        IWorldRepository worldRepository,
        IPlayerRepository playerRepository,
        IWorldCacheService worldCacheService,
        IPlayerCacheService playerCacheService,
        IWorldEventPublisher worldEventPublisher,
        ILogger<WorldServiceImpl> logger)
    {
        _worldRepository = worldRepository;
        _playerRepository = playerRepository;
        _worldCacheService = worldCacheService;
        _playerCacheService = playerCacheService;
        _worldEventPublisher = worldEventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// 加入世界
    /// </summary>
    public async ValueTask<JoinWorldResponse> JoinWorldAsync(JoinWorldRequest request)
    {
        try
        {
            var playerId = request.PlayerId;
            var worldId = request.WorldId;

            _logger.LogInformation("Player {PlayerId} attempting to join world {WorldId}", playerId, worldId);

            // 1. 验证世界是否存在
            var world = await _worldCacheService.GetWorldStateAsync(worldId);
            if (world == null)
            {
                world = await _worldRepository.GetWorldByIdAsync(worldId);
                if (world == null)
                {
                    return new JoinWorldResponse
                    {
                        Success = false,
                        Message = "世界不存在"
                    };
                }
                await _worldCacheService.CacheWorldStateAsync(world);
            }

            // 2. 检查世界容量
            if (world.CurrentPlayers >= world.MaxPlayers)
            {
                return new JoinWorldResponse
                {
                    Success = false,
                    Message = "世界人数已满"
                };
            }

            // 3. 获取玩家信息
            var player = await _playerCacheService.GetPlayerInfoAsync(playerId);
            if (player == null)
            {
                player = await _playerRepository.GetPlayerByIdAsync(playerId);
                if (player == null)
                {
                    return new JoinWorldResponse
                    {
                        Success = false,
                        Message = "玩家不存在"
                    };
                }
            }

            // 4. 检查玩家是否已在其他世界
            var currentWorldId = player.Position.WorldId;
            if (!string.IsNullOrEmpty(currentWorldId) && currentWorldId != worldId)
            {
                // 先离开当前世界
                await LeaveWorldInternalAsync(playerId, currentWorldId, "switching_world");
            }

            // 5. 更新玩家位置
            player.Position.WorldId = worldId;
            player.Position.MapId = request.SpawnPosition.MapId ?? "map_001";
            player.Position.X = request.SpawnPosition.X;
            player.Position.Y = request.SpawnPosition.Y;
            player.Position.Z = request.SpawnPosition.Z;
            player.Position.LastUpdate = DateTime.UtcNow;

            await _playerRepository.UpdatePlayerPositionAsync(playerId, player.Position);
            await _playerCacheService.UpdatePlayerPositionAsync(playerId, player.Position);

            // 6. 将玩家添加到世界
            await _worldCacheService.AddPlayerToWorldAsync(worldId, playerId);
            world.CurrentPlayers++;
            await _worldCacheService.CacheWorldStateAsync(world);

            // 7. 获取附近玩家
            var nearbyPlayers = await GetNearbyPlayersInternalAsync(playerId, player.Position, 500.0f);

            // 8. 发布玩家加入世界事件
            var joinedEvent = new PlayerJoinedEvent
            {
                WorldId = worldId,
                Player = player,
                Position = player.Position,
                Timestamp = DateTime.UtcNow
            };
            await _worldEventPublisher.PublishPlayerJoinedAsync(worldId, joinedEvent);

            _logger.LogInformation("Player {PlayerId} successfully joined world {WorldId}", playerId, worldId);

            return new JoinWorldResponse
            {
                Success = true,
                Message = "成功加入世界",
                WorldState = world,
                NearbyPlayers = nearbyPlayers
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining world: {PlayerId} -> {WorldId}", request.PlayerId, request.WorldId);
            return new JoinWorldResponse
            {
                Success = false,
                Message = "加入世界失败，请稍后重试"
            };
        }
    }

    /// <summary>
    /// 离开世界
    /// </summary>
    public async ValueTask LeaveWorldAsync(LeaveWorldRequest request)
    {
        try
        {
            await LeaveWorldInternalAsync(request.PlayerId, "", request.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving world: {PlayerId}", request.PlayerId);
        }
    }

    /// <summary>
    /// 获取世界状态
    /// </summary>
    public async ValueTask<WorldState> GetWorldStateAsync(GetWorldStateRequest request)
    {
        try
        {
            var worldId = request.WorldId;

            // 先从缓存获取
            var world = await _worldCacheService.GetWorldStateAsync(worldId);
            if (world != null)
            {
                return world;
            }

            // 从数据库获取
            world = await _worldRepository.GetWorldByIdAsync(worldId);
            if (world == null)
            {
                return new WorldState { WorldId = worldId, Status = "not_found" };
            }

            // 缓存世界状态
            await _worldCacheService.CacheWorldStateAsync(world);

            return world;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting world state: {WorldId}", request.WorldId);
            return new WorldState { WorldId = request.WorldId, Status = "error" };
        }
    }

    /// <summary>
    /// 世界聊天
    /// </summary>
    public async ValueTask SendWorldChatAsync(WorldChatRequest request)
    {
        try
        {
            var playerId = request.PlayerId;
            var message = request.Message;
            var chatType = request.ChatType;

            _logger.LogDebug("World chat from player {PlayerId}: {Message}", playerId, message);

            // 1. 验证玩家信息
            var player = await _playerCacheService.GetPlayerInfoAsync(playerId);
            if (player == null)
            {
                _logger.LogWarning("Chat failed: Player not found: {PlayerId}", playerId);
                return;
            }

            var worldId = player.Position.WorldId;
            if (string.IsNullOrEmpty(worldId))
            {
                _logger.LogWarning("Chat failed: Player not in any world: {PlayerId}", playerId);
                return;
            }

            // 2. 验证消息内容
            if (string.IsNullOrWhiteSpace(message) || message.Length > 500)
            {
                _logger.LogWarning("Chat failed: Invalid message from player {PlayerId}", playerId);
                return;
            }

            // 3. 检查聊天频率限制
            if (await IsPlayerChatRateLimitedAsync(playerId))
            {
                _logger.LogWarning("Chat failed: Rate limited for player {PlayerId}", playerId);
                return;
            }

            // 4. 发布聊天消息事件
            var chatEvent = new WorldChatMessageEvent
            {
                WorldId = worldId,
                PlayerId = playerId,
                PlayerName = player.CharacterName,
                Message = message,
                ChatType = chatType,
                Timestamp = DateTime.UtcNow
            };

            await _worldEventPublisher.PublishWorldChatMessageAsync(worldId, chatEvent);

            // 5. 记录聊天日志
            await RecordChatMessageAsync(playerId, worldId, message, chatType);

            _logger.LogDebug("World chat message sent successfully: {PlayerId} in {WorldId}", playerId, worldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending world chat: {PlayerId}", request.PlayerId);
        }
    }

    /// <summary>
    /// 获取附近玩家
    /// </summary>
    public async ValueTask<NearbyPlayersResponse> GetNearbyPlayersAsync(NearbyPlayersRequest request)
    {
        try
        {
            var playerId = request.PlayerId;
            var position = request.Position;
            var radius = request.Radius;

            var nearbyPlayers = await GetNearbyPlayersInternalAsync(playerId, position, radius);

            return new NearbyPlayersResponse
            {
                Players = nearbyPlayers,
                TotalCount = nearbyPlayers.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting nearby players: {PlayerId}", request.PlayerId);
            return new NearbyPlayersResponse
            {
                Players = new List<PlayerInfo>(),
                TotalCount = 0
            };
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// 内部离开世界实现
    /// </summary>
    private async Task LeaveWorldInternalAsync(int playerId, string worldId, string reason)
    {
        try
        {
            // 获取玩家信息
            var player = await _playerCacheService.GetPlayerInfoAsync(playerId);
            if (player == null)
            {
                player = await _playerRepository.GetPlayerByIdAsync(playerId);
                if (player == null)
                {
                    return;
                }
            }

            var currentWorldId = worldId;
            if (string.IsNullOrEmpty(currentWorldId))
            {
                currentWorldId = player.Position.WorldId;
            }

            if (string.IsNullOrEmpty(currentWorldId))
            {
                return; // 玩家不在任何世界中
            }

            _logger.LogInformation("Player {PlayerId} leaving world {WorldId}, reason: {Reason}",
                playerId, currentWorldId, reason);

            // 从世界中移除玩家
            await _worldCacheService.RemovePlayerFromWorldAsync(currentWorldId, playerId);

            // 更新世界玩家数量
            var world = await _worldCacheService.GetWorldStateAsync(currentWorldId);
            if (world != null)
            {
                world.CurrentPlayers = Math.Max(0, world.CurrentPlayers - 1);
                await _worldCacheService.CacheWorldStateAsync(world);
            }

            // 发布玩家离开世界事件
            var leftEvent = new PlayerLeftEvent
            {
                WorldId = currentWorldId,
                PlayerId = playerId,
                PlayerName = player.CharacterName,
                Reason = reason,
                Timestamp = DateTime.UtcNow
            };
            await _worldEventPublisher.PublishPlayerLeftAsync(currentWorldId, leftEvent);

            _logger.LogInformation("Player {PlayerId} left world {WorldId}", playerId, currentWorldId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in LeaveWorldInternalAsync: {PlayerId}", playerId);
        }
    }

    /// <summary>
    /// 获取附近玩家（内部方法）
    /// </summary>
    private async Task<List<PlayerInfo>> GetNearbyPlayersInternalAsync(int playerId, PlayerPosition position, float radius)
    {
        try
        {
            // 从缓存中获取同一世界的所有在线玩家
            var worldPlayers = await _worldCacheService.GetWorldPlayersAsync(position.WorldId);
            var nearbyPlayers = new List<PlayerInfo>();

            foreach (var otherPlayerId in worldPlayers)
            {
                if (otherPlayerId == playerId) // 排除自己
                    continue;

                var otherPlayer = await _playerCacheService.GetPlayerInfoAsync(otherPlayerId);
                if (otherPlayer == null)
                    continue;

                // 计算距离
                var distance = CalculateDistance(position, otherPlayer.Position);
                if (distance <= radius)
                {
                    nearbyPlayers.Add(otherPlayer);
                }
            }

            return nearbyPlayers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting nearby players");
            return new List<PlayerInfo>();
        }
    }

    /// <summary>
    /// 计算两个位置之间的距离
    /// </summary>
    private float CalculateDistance(PlayerPosition pos1, PlayerPosition pos2)
    {
        if (pos1.WorldId != pos2.WorldId || pos1.MapId != pos2.MapId)
        {
            return float.MaxValue; // 不在同一地图
        }

        var dx = pos1.X - pos2.X;
        var dz = pos1.Z - pos2.Z;
        var dy = pos1.Y - pos2.Y;

        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// 检查玩家聊天频率限制
    /// </summary>
    private async Task<bool> IsPlayerChatRateLimitedAsync(int playerId)
    {
        // 简化实现：每秒最多1条消息
        // 实际项目中可以使用更复杂的限流算法
        return false;
    }

    /// <summary>
    /// 记录聊天消息
    /// </summary>
    private async Task RecordChatMessageAsync(int playerId, string worldId, string message, string chatType)
    {
        try
        {
            // 这里可以记录聊天日志到数据库
            // 简化实现
            _logger.LogInformation("Chat [{ChatType}] Player {PlayerId} in {WorldId}: {Message}",
                chatType, playerId, worldId, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording chat message");
        }
    }

    #endregion
}
