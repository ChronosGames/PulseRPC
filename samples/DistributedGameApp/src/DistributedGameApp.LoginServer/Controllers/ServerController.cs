using DistributedGameApp.Infrastructure.Consul;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DistributedGameApp.LoginServer.Controllers;

/// <summary>
/// 服务器列表控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServerController : ControllerBase
{
    private readonly ConsulServiceDiscovery _serviceDiscovery;
    private readonly ILogger<ServerController> _logger;

    public ServerController(
        ConsulServiceDiscovery serviceDiscovery,
        ILogger<ServerController> logger)
    {
        _serviceDiscovery = serviceDiscovery;
        _logger = logger;
    }

    /// <summary>
    /// 获取游戏服务器列表
    /// </summary>
    [HttpGet("game-servers")]
    public async Task<ActionResult<List<ServerInfo>>> GetGameServers()
    {
        try
        {
            var services = await _serviceDiscovery.GetServicesAsync("GameServer");

            var serverList = services
                .Where(s => s.Status == "Online")
                .OrderBy(s => (double)s.CurrentLoad / s.MaxCapacity)
                .Select(s => new ServerInfo
                {
                    ServerId = s.ServiceId,
                    ServerName = s.NodeName,
                    Host = s.Host,
                    TcpPort = s.TcpPort,
                    KcpPort = s.KcpPort,
                    CurrentPlayers = s.CurrentLoad,
                    MaxPlayers = s.MaxCapacity,
                    Status = s.Status,
                    LoadPercentage = s.MaxCapacity > 0 ? (int)((double)s.CurrentLoad / s.MaxCapacity * 100) : 0
                })
                .ToList();

            return Ok(serverList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get game servers");
            return StatusCode(500, new { message = "获取服务器列表失败" });
        }
    }

    /// <summary>
    /// 获取战斗服务器列表
    /// </summary>
    [HttpGet("battle-servers")]
    public async Task<ActionResult<List<ServerInfo>>> GetBattleServers()
    {
        try
        {
            var services = await _serviceDiscovery.GetServicesAsync("BattleServer");

            var serverList = services
                .Where(s => s.Status == "Online")
                .OrderBy(s => (double)s.CurrentLoad / s.MaxCapacity)
                .Select(s => new ServerInfo
                {
                    ServerId = s.ServiceId,
                    ServerName = s.NodeName,
                    Host = s.Host,
                    TcpPort = s.TcpPort,
                    KcpPort = s.KcpPort,
                    CurrentPlayers = s.CurrentLoad,
                    MaxPlayers = s.MaxCapacity,
                    Status = s.Status,
                    LoadPercentage = s.MaxCapacity > 0 ? (int)((double)s.CurrentLoad / s.MaxCapacity * 100) : 0
                })
                .ToList();

            return Ok(serverList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get battle servers");
            return StatusCode(500, new { message = "获取战斗服务器列表失败" });
        }
    }

    /// <summary>
    /// 获取后台服务器列表
    /// </summary>
    [HttpGet("backend-servers")]
    public async Task<ActionResult<List<ServerInfo>>> GetBackendServers()
    {
        try
        {
            var services = await _serviceDiscovery.GetServicesAsync("BackendServer");

            var serverList = services
                .Where(s => s.Status == "Online")
                .OrderBy(s => (double)s.CurrentLoad / s.MaxCapacity)
                .Select(s => new ServerInfo
                {
                    ServerId = s.ServiceId,
                    ServerName = s.NodeName,
                    Host = s.Host,
                    TcpPort = s.TcpPort,
                    KcpPort = s.KcpPort,
                    CurrentPlayers = s.CurrentLoad,
                    MaxPlayers = s.MaxCapacity,
                    Status = s.Status,
                    LoadPercentage = s.MaxCapacity > 0 ? (int)((double)s.CurrentLoad / s.MaxCapacity * 100) : 0
                })
                .ToList();

            return Ok(serverList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get backend servers");
            return StatusCode(500, new { message = "获取后台服务器列表失败" });
        }
    }

    /// <summary>
    /// 推荐最佳游戏服务器
    /// </summary>
    [HttpGet("recommend/game-server")]
    public async Task<ActionResult<ServerInfo>> GetRecommendedGameServer()
    {
        try
        {
            var bestServer = await _serviceDiscovery.DiscoverBestServiceAsync("GameServer");

            if (bestServer == null)
            {
                return NotFound(new { message = "当前没有可用的游戏服务器" });
            }

            var serverInfo = new ServerInfo
            {
                ServerId = bestServer.ServiceId,
                ServerName = bestServer.NodeName,
                Host = bestServer.Host,
                TcpPort = bestServer.TcpPort,
                KcpPort = bestServer.KcpPort,
                CurrentPlayers = bestServer.CurrentLoad,
                MaxPlayers = bestServer.MaxCapacity,
                Status = bestServer.Status,
                LoadPercentage = bestServer.MaxCapacity > 0 ? (int)((double)bestServer.CurrentLoad / bestServer.MaxCapacity * 100) : 0
            };

            return Ok(serverInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recommended game server");
            return StatusCode(500, new { message = "获取推荐服务器失败" });
        }
    }

    /// <summary>
    /// 推荐最佳战斗服务器
    /// </summary>
    [HttpGet("recommend/battle-server")]
    public async Task<ActionResult<ServerInfo>> GetRecommendedBattleServer()
    {
        try
        {
            var bestServer = await _serviceDiscovery.DiscoverBestServiceAsync("BattleServer");

            if (bestServer == null)
            {
                return NotFound(new { message = "当前没有可用的战斗服务器" });
            }

            var serverInfo = new ServerInfo
            {
                ServerId = bestServer.ServiceId,
                ServerName = bestServer.NodeName,
                Host = bestServer.Host,
                TcpPort = bestServer.TcpPort,
                KcpPort = bestServer.KcpPort,
                CurrentPlayers = bestServer.CurrentLoad,
                MaxPlayers = bestServer.MaxCapacity,
                Status = bestServer.Status,
                LoadPercentage = bestServer.MaxCapacity > 0 ? (int)((double)bestServer.CurrentLoad / bestServer.MaxCapacity * 100) : 0
            };

            return Ok(serverInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recommended battle server");
            return StatusCode(500, new { message = "获取推荐战斗服务器失败" });
        }
    }
}

/// <summary>
/// 服务器信息
/// </summary>
public class ServerInfo
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int TcpPort { get; set; }
    public int? KcpPort { get; set; }
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string Status { get; set; } = string.Empty;
    public int LoadPercentage { get; set; }
}
