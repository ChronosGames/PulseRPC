using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

public interface IPlayerRoutingService
{
    Task RegisterPlayer(string playerId, string serverId);

    Task UnregisterPlayer(string playerId);

    Task UpdatePlayerHeartbeat(string playerId);

    Task<string> LocatePlayer(string playerId);
}

// 中央玩家路由服务
public class PlayerRoutingService : IPlayerRoutingService {
    private readonly IDistributedCache _cache;
    private readonly ILogger _logger;

    // 构造函数
    public PlayerRoutingService(IDistributedCache cache, ILogger logger) {
        _cache = cache;
        _logger = logger;
    }

    // 注册玩家所在服务器
    public async Task RegisterPlayer(string playerId, string serverId) {
        string cacheKey = GetPlayerRouteKey(playerId);
        await _cache.SetStringAsync(cacheKey, serverId, new DistributedCacheEntryOptions {
            SlidingExpiration = TimeSpan.FromMinutes(30)
        });
        _logger.Info($"Player {playerId} registered on server {serverId}");
    }

    // 查找玩家所在服务器
    public async Task<string> LocatePlayer(string playerId) {
        string cacheKey = GetPlayerRouteKey(playerId);
        string serverId = await _cache.GetStringAsync(cacheKey);

        if (string.IsNullOrEmpty(serverId)) {
            _logger.Warning($"Player {playerId} not found in routing table");
            throw new PlayerNotFoundException(playerId);
        }

        return serverId;
    }

    // 更新玩家心跳
    public async Task UpdatePlayerHeartbeat(string playerId) {
        string cacheKey = GetPlayerRouteKey(playerId);
        // 刷新TTL
        await _cache.RefreshAsync(cacheKey);
    }

    // 玩家登出
    public async Task UnregisterPlayer(string playerId) {
        string cacheKey = GetPlayerRouteKey(playerId);
        await _cache.RemoveAsync(cacheKey);
        _logger.Info($"Player {playerId} unregistered");
    }

    private string GetPlayerRouteKey(string playerId) {
        return $"player:route:{playerId}";
    }
}
