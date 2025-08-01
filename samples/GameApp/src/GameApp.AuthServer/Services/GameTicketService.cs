using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;
using GameApp.AuthServer.Models;

namespace GameApp.AuthServer.Services;

/// <summary>
/// 游戏票据服务实现
/// </summary>
public class GameTicketService : IGameTicketService
{
    private readonly IDatabase _redisDatabase;
    private readonly IUserService _userService;
    private readonly ILogger<GameTicketService> _logger;

    // 票据签名密钥（实际项目中应该从配置中读取）
    private readonly string _signatureKey = "GameApp_GameTicket_SignatureKey_2024";

    public GameTicketService(
        IConnectionMultiplexer redis,
        IUserService userService,
        ILogger<GameTicketService> logger)
    {
        _redisDatabase = redis.GetDatabase();
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// 生成游戏票据
    /// </summary>
    public async Task<GameTicketResult> GenerateGameTicketAsync(int userId, string zoneId, string serverId)
    {
        try
        {
            _logger.LogInformation("Generating game ticket for user {UserId}, zone {ZoneId}, server {ServerId}",
                userId, zoneId, serverId);

            // 获取用户信息
            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null)
            {
                return new GameTicketResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            // 生成票据ID
            var ticketId = Guid.NewGuid().ToString("N");
            var expirationTime = DateTime.UtcNow.AddMinutes(10); // 票据10分钟有效

            // 创建票据数据
            var ticketData = new GameTicketData
            {
                TicketId = ticketId,
                UserId = userId,
                Username = user.Username,
                ZoneId = zoneId,
                ServerId = serverId,
                Permissions = GetUserPermissions(user),
                ExpirationTime = expirationTime,
                CreatedTime = DateTime.UtcNow
            };

            // 序列化票据数据
            var ticketJson = JsonSerializer.Serialize(ticketData);
            var ticketBytes = Encoding.UTF8.GetBytes(ticketJson);

            // 生成签名
            var signature = GenerateSignature(ticketBytes);

            // 创建完整的票据
            var gameTicket = Convert.ToBase64String(ticketBytes) + "." + signature;

            // 存储到 Redis
            var ticketKey = $"game_ticket:{ticketId}";
            await _redisDatabase.StringSetAsync(ticketKey, ticketJson, TimeSpan.FromMinutes(10));

            // 存储用户票据映射
            var userTicketKey = $"user_ticket:{userId}";
            await _redisDatabase.StringSetAsync(userTicketKey, ticketId, TimeSpan.FromMinutes(10));

            _logger.LogInformation("Game ticket generated successfully for user {UserId}: {TicketId}", userId, ticketId);

            return new GameTicketResult
            {
                Success = true,
                Message = "Game ticket generated successfully",
                GameTicket = gameTicket,
                ExpiresIn = 600, // 10分钟
                ZoneId = zoneId,
                ServerId = serverId,
                Permissions = ticketData.Permissions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating game ticket for user {UserId}", userId);
            return new GameTicketResult
            {
                Success = false,
                Message = "Failed to generate game ticket"
            };
        }
    }

    /// <summary>
    /// 验证游戏票据
    /// </summary>
    public async Task<GameTicketValidationResult> ValidateGameTicketAsync(string gameTicket, string serverId)
    {
        try
        {
            _logger.LogDebug("Validating game ticket for server {ServerId}", serverId);

            // 分离票据和签名
            var parts = gameTicket.Split('.');
            if (parts.Length != 2)
            {
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "Invalid ticket format"
                };
            }

            var ticketBase64 = parts[0];
            var signature = parts[1];

            // 验证签名
            var ticketBytes = Convert.FromBase64String(ticketBase64);
            if (!VerifySignature(ticketBytes, signature))
            {
                _logger.LogWarning("Game ticket signature verification failed");
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "Invalid ticket signature"
                };
            }

            // 解析票据数据
            var ticketJson = Encoding.UTF8.GetString(ticketBytes);
            var ticketData = JsonSerializer.Deserialize<GameTicketData>(ticketJson);

            if (ticketData == null)
            {
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "Invalid ticket data"
                };
            }

            // 检查票据是否过期
            if (DateTime.UtcNow > ticketData.ExpirationTime)
            {
                _logger.LogWarning("Game ticket expired for user {UserId}", ticketData.UserId);
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "Ticket expired"
                };
            }

            // 检查服务器ID匹配
            if (ticketData.ServerId != serverId)
            {
                _logger.LogWarning("Game ticket server mismatch: expected {ExpectedServerId}, got {ActualServerId}",
                    serverId, ticketData.ServerId);
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "Server mismatch"
                };
            }

            // 从 Redis 验证票据是否存在且未被使用
            var ticketKey = $"game_ticket:{ticketData.TicketId}";
            var storedTicket = await _redisDatabase.StringGetAsync(ticketKey);
            if (!storedTicket.HasValue)
            {
                _logger.LogWarning("Game ticket not found in cache: {TicketId}", ticketData.TicketId);
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "Ticket not found or already used"
                };
            }

            // 获取用户信息
            var user = await _userService.GetUserByIdAsync(ticketData.UserId);
            if (user == null || user.Status != "active")
            {
                return new GameTicketValidationResult
                {
                    IsValid = false,
                    Message = "User not found or inactive"
                };
            }

            _logger.LogInformation("Game ticket validated successfully for user {UserId}", ticketData.UserId);

            return new GameTicketValidationResult
            {
                IsValid = true,
                Message = "Ticket is valid",
                UserId = ticketData.UserId,
                Username = ticketData.Username,
                ZoneId = ticketData.ZoneId,
                Permissions = ticketData.Permissions,
                PlayerInfo = new UserInfo
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Email = user.Email,
                    Profile = user.Profile
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating game ticket");
            return new GameTicketValidationResult
            {
                IsValid = false,
                Message = "Ticket validation error"
            };
        }
    }

    /// <summary>
    /// 使用游戏票据（一次性使用）
    /// </summary>
    public async Task<bool> ConsumeGameTicketAsync(string gameTicket)
    {
        try
        {
            // 先验证票据
            var validationResult = await ValidateGameTicketAsync(gameTicket, ""); // 不检查serverId

            if (!validationResult.IsValid)
            {
                return false;
            }

            // 分离票据数据
            var parts = gameTicket.Split('.');
            var ticketBytes = Convert.FromBase64String(parts[0]);
            var ticketJson = Encoding.UTF8.GetString(ticketBytes);
            var ticketData = JsonSerializer.Deserialize<GameTicketData>(ticketJson);

            if (ticketData == null)
            {
                return false;
            }

            // 删除票据（标记为已使用）
            var ticketKey = $"game_ticket:{ticketData.TicketId}";
            var userTicketKey = $"user_ticket:{ticketData.UserId}";

            await _redisDatabase.KeyDeleteAsync(ticketKey);
            await _redisDatabase.KeyDeleteAsync(userTicketKey);

            _logger.LogInformation("Game ticket consumed for user {UserId}: {TicketId}",
                ticketData.UserId, ticketData.TicketId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming game ticket");
            return false;
        }
    }

    #region Private Methods

    /// <summary>
    /// 获取用户权限
    /// </summary>
    private List<string> GetUserPermissions(User user)
    {
        var permissions = new List<string> { "player.basic" };

        // 根据用户等级和VIP等级添加权限
        if (user.Profile.Level >= 10)
        {
            permissions.Add("player.advanced");
        }

        if (user.Profile.VipLevel > 0)
        {
            permissions.Add("player.vip");
            permissions.Add($"player.vip.level{user.Profile.VipLevel}");
        }

        // 管理员权限（可以根据用户角色字段判断）
        if (user.Status == "admin")
        {
            permissions.Add("admin.basic");
        }

        return permissions;
    }

    /// <summary>
    /// 生成签名
    /// </summary>
    private string GenerateSignature(byte[] data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_signatureKey));
        var hash = hmac.ComputeHash(data);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 验证签名
    /// </summary>
    private bool VerifySignature(byte[] data, string signature)
    {
        var expectedSignature = GenerateSignature(data);
        return signature == expectedSignature;
    }

    #endregion

    /// <summary>
    /// 游戏票据数据结构
    /// </summary>
    private class GameTicketData
    {
        public string TicketId { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string ServerId { get; set; } = string.Empty;
        public List<string> Permissions { get; set; } = new();
        public DateTime ExpirationTime { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}
