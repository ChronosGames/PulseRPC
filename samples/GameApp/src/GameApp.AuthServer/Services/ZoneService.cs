using MongoDB.Driver;
using StackExchange.Redis;
using GameApp.AuthServer.Models;

namespace GameApp.AuthServer.Services;

/// <summary>
/// 区服服务实现
/// </summary>
public class ZoneService : IZoneService
{
    private readonly IMongoCollection<ZoneInfo> _zoneCollection;
    private readonly IDatabase _redisDatabase;
    private readonly IGameTicketService _gameTicketService;
    private readonly ILogger<ZoneService> _logger;

    public ZoneService(
        IMongoDatabase database,
        IConnectionMultiplexer redis,
        IGameTicketService gameTicketService,
        ILogger<ZoneService> logger)
    {
        _zoneCollection = database.GetCollection<ZoneInfo>("zones");
        _redisDatabase = redis.GetDatabase();
        _gameTicketService = gameTicketService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有区服列表
    /// </summary>
    public async Task<List<ZoneInfo>> GetZoneListAsync()
    {
        try
        {
            _logger.LogDebug("Getting zone list");

            // 先尝试从缓存获取
            var cacheKey = "zones:list";
            var cachedZones = await _redisDatabase.StringGetAsync(cacheKey);

            if (!cachedZones.IsNull)
            {
                var cachedResult = System.Text.Json.JsonSerializer.Deserialize<List<ZoneInfo>>(cachedZones!);
                if (cachedResult != null)
                {
                    _logger.LogDebug("Returned cached zone list with {Count} zones", cachedResult.Count);
                    return cachedResult;
                }
            }

            // 从数据库获取
            var zones = await _zoneCollection
                .Find(_ => true)
                .SortBy(z => z.ZoneId)
                .ToListAsync();

            // 更新每个区服的实时状态
            foreach (var zone in zones)
            {
                await UpdateZoneRealtimeStatus(zone);
            }

            // 缓存结果（5分钟）
            var zonesJson = System.Text.Json.JsonSerializer.Serialize(zones);
            await _redisDatabase.StringSetAsync(cacheKey, zonesJson, TimeSpan.FromMinutes(5));

            _logger.LogInformation("Retrieved {Count} zones from database", zones.Count);
            return zones;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting zone list");
            throw;
        }
    }

    /// <summary>
    /// 获取区服详情
    /// </summary>
    public async Task<ZoneInfo?> GetZoneByIdAsync(string zoneId)
    {
        try
        {
            _logger.LogDebug("Getting zone by ID: {ZoneId}", zoneId);

            // 先尝试从缓存获取
            var cacheKey = $"zone:{zoneId}";
            var cachedZone = await _redisDatabase.StringGetAsync(cacheKey);

            if (!cachedZone.IsNull)
            {
                var cachedResult = System.Text.Json.JsonSerializer.Deserialize<ZoneInfo>(cachedZone!);
                if (cachedResult != null)
                {
                    await UpdateZoneRealtimeStatus(cachedResult);
                    return cachedResult;
                }
            }

            // 从数据库获取
            var filter = Builders<ZoneInfo>.Filter.Eq(z => z.ZoneId, zoneId);
            var zone = await _zoneCollection.Find(filter).FirstOrDefaultAsync();

            if (zone != null)
            {
                // 更新实时状态
                await UpdateZoneRealtimeStatus(zone);

                // 缓存结果（1分钟）
                var zoneJson = System.Text.Json.JsonSerializer.Serialize(zone);
                await _redisDatabase.StringSetAsync(cacheKey, zoneJson, TimeSpan.FromMinutes(1));

                _logger.LogDebug("Found zone: {ZoneId}, Status: {Status}", zone.ZoneId, zone.Status);
            }

            return zone;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting zone by ID: {ZoneId}", zoneId);
            throw;
        }
    }

    /// <summary>
    /// 选择区服
    /// </summary>
    public async Task<ZoneSelectionResult> SelectZoneAsync(int userId, string zoneId)
    {
        try
        {
            _logger.LogInformation("User {UserId} selecting zone: {ZoneId}", userId, zoneId);

            // 获取区服信息
            var zone = await GetZoneByIdAsync(zoneId);
            if (zone == null)
            {
                return new ZoneSelectionResult
                {
                    Success = false,
                    Message = "区服不存在"
                };
            }

            // 检查区服状态
            if (zone.Status != "online")
            {
                return new ZoneSelectionResult
                {
                    Success = false,
                    Message = zone.Status switch
                    {
                        "maintenance" => "区服正在维护中",
                        "offline" => "区服已下线",
                        _ => "区服状态异常"
                    }
                };
            }

            // 检查是否允许新玩家
            if (!zone.NewPlayerAllowed)
            {
                // 检查用户是否已经在该区服有角色
                var hasCharacter = await CheckUserHasCharacterInZoneAsync(userId, zoneId);
                if (!hasCharacter)
                {
                    return new ZoneSelectionResult
                    {
                        Success = false,
                        Message = "该区服暂时不接受新玩家"
                    };
                }
            }

            // 检查区服容量
            if (zone.CurrentPlayers >= zone.MaxPlayers)
            {
                return new ZoneSelectionResult
                {
                    Success = false,
                    Message = "区服人数已满，请选择其他区服"
                };
            }

            // 获取可用的游戏服务器
            var gameServers = await GetAvailableGameServersAsync(zoneId);
            if (!gameServers.Any())
            {
                return new ZoneSelectionResult
                {
                    Success = false,
                    Message = "暂无可用的游戏服务器"
                };
            }

            // 选择负载最低的服务器
            var selectedServer = gameServers.OrderBy(s => s.Priority).First();

            // 生成游戏票据
            var gameTicketResult = await _gameTicketService.GenerateGameTicketAsync(userId, zoneId, selectedServer.ServerId);
            if (!gameTicketResult.Success)
            {
                return new ZoneSelectionResult
                {
                    Success = false,
                    Message = "生成游戏票据失败"
                };
            }

            // 获取战斗服务器列表
            var battleServers = await GetAvailableBattleServersAsync(zoneId);

            // 更新用户最后选择的区服
            await UpdateUserLastSelectedZoneAsync(userId, zoneId);

            _logger.LogInformation("Zone selection successful for user {UserId}: {ZoneId}", userId, zoneId);

            return new ZoneSelectionResult
            {
                Success = true,
                Message = "区服选择成功",
                ZoneId = zoneId,
                GameTicket = gameTicketResult.GameTicket,
                ExpiresIn = gameTicketResult.ExpiresIn,
                GameServers = gameServers,
                BattleServers = battleServers
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error selecting zone for user {UserId}: {ZoneId}", userId, zoneId);
            return new ZoneSelectionResult
            {
                Success = false,
                Message = "系统错误，请稍后重试"
            };
        }
    }

    /// <summary>
    /// 更新区服状态
    /// </summary>
    public async Task UpdateZoneStatusAsync(string zoneId, string status, int playerCount)
    {
        try
        {
            _logger.LogDebug("Updating zone status: {ZoneId}, Status: {Status}, Players: {PlayerCount}",
                zoneId, status, playerCount);

            // 更新数据库
            var filter = Builders<ZoneInfo>.Filter.Eq(z => z.ZoneId, zoneId);
            var update = Builders<ZoneInfo>.Update
                .Set(z => z.Status, status)
                .Set(z => z.CurrentPlayers, playerCount)
                .Set(z => z.UpdatedAt, DateTime.UtcNow);

            // 根据人数设置负载等级
            var zone = await GetZoneByIdAsync(zoneId);
            if (zone != null)
            {
                var loadLevel = CalculateLoadLevel(playerCount, zone.MaxPlayers);
                update = update.Set(z => z.LoadLevel, loadLevel);
            }

            await _zoneCollection.UpdateOneAsync(filter, update);

            // 清除缓存
            await InvalidateZoneCacheAsync(zoneId);

            _logger.LogInformation("Zone status updated: {ZoneId}", zoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating zone status: {ZoneId}", zoneId);
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// 更新区服实时状态
    /// </summary>
    private async Task UpdateZoneRealtimeStatus(ZoneInfo zone)
    {
        try
        {
            // 从缓存获取实时人数
            var playerCountKey = $"zone_players:{zone.ZoneId}";
            var currentPlayers = await _redisDatabase.StringGetAsync(playerCountKey);

            if (currentPlayers.HasValue)
            {
                zone.CurrentPlayers = currentPlayers;
                zone.LoadLevel = CalculateLoadLevel(zone.CurrentPlayers, zone.MaxPlayers);
            }

            // 检查服务器健康状态
            var healthKey = $"zone_health:{zone.ZoneId}";
            var healthStatus = await _redisDatabase.StringGetAsync(healthKey);

            if (healthStatus.HasValue)
            {
                zone.Status = healthStatus;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating realtime status for zone: {ZoneId}", zone.ZoneId);
        }
    }

    /// <summary>
    /// 计算负载等级
    /// </summary>
    private string CalculateLoadLevel(int currentPlayers, int maxPlayers)
    {
        var ratio = (double)currentPlayers / maxPlayers;
        return ratio switch
        {
            < 0.3 => "low",
            < 0.7 => "medium",
            _ => "high"
        };
    }

    /// <summary>
    /// 检查用户是否在指定区服有角色
    /// </summary>
    private async Task<bool> CheckUserHasCharacterInZoneAsync(int userId, string zoneId)
    {
        try
        {
            // 这里应该查询玩家数据库检查是否有角色
            // 暂时从缓存中检查
            var characterKey = $"user_character:{userId}:{zoneId}";
            var hasCharacter = await _redisDatabase.KeyExistsAsync(characterKey);
            return hasCharacter;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking user character in zone: {UserId}, {ZoneId}", userId, zoneId);
            return false;
        }
    }

    /// <summary>
    /// 获取可用的游戏服务器
    /// </summary>
    private async Task<List<ServerEndpoint>> GetAvailableGameServersAsync(string zoneId)
    {
        try
        {
            // 从 Consul 或缓存中获取可用的游戏服务器
            // 这里使用简化实现
            var servers = new List<ServerEndpoint>
            {
                new ServerEndpoint
                {
                    ServerId = $"gameserver-{zoneId}-01",
                    Address = "gameserver-dev:7000",
                    Priority = 1
                }
            };

            return servers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available game servers for zone: {ZoneId}", zoneId);
            return new List<ServerEndpoint>();
        }
    }

    /// <summary>
    /// 获取可用的战斗服务器
    /// </summary>
    private async Task<List<ServerEndpoint>> GetAvailableBattleServersAsync(string zoneId)
    {
        try
        {
            var servers = new List<ServerEndpoint>
            {
                new ServerEndpoint
                {
                    ServerId = $"battleserver-{zoneId}-01",
                    Address = "battleserver-dev:7000",
                    Priority = 1
                }
            };

            return servers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available battle servers for zone: {ZoneId}", zoneId);
            return new List<ServerEndpoint>();
        }
    }

    /// <summary>
    /// 更新用户最后选择的区服
    /// </summary>
    private async Task UpdateUserLastSelectedZoneAsync(int userId, string zoneId)
    {
        try
        {
            var userCacheKey = $"user_zone:{userId}";
            await _redisDatabase.StringSetAsync(userCacheKey, zoneId, TimeSpan.FromDays(30));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating user last selected zone: {UserId}, {ZoneId}", userId, zoneId);
        }
    }

    /// <summary>
    /// 清除区服缓存
    /// </summary>
    private async Task InvalidateZoneCacheAsync(string zoneId)
    {
        try
        {
            var cacheKeys = new[]
            {
                $"zone:{zoneId}",
                "zones:list"
            };

            foreach (var key in cacheKeys)
            {
                await _redisDatabase.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error invalidating zone cache: {ZoneId}", zoneId);
        }
    }

    #endregion
}
