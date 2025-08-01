using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace GameApp.AuthServer.Models;

/// <summary>
/// 用户模型
/// </summary>
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string Status { get; set; } = "active"; // active, banned, inactive
    public DateTime RegistrationTime { get; set; }
    public DateTime LastLoginTime { get; set; }
    public int LoginCount { get; set; }

    public UserProfile Profile { get; set; } = new();
    public UserSecurity Security { get; set; } = new();
    public UserPreferences Preferences { get; set; } = new();
    public UserGameData GameData { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 用户资料
/// </summary>
public class UserProfile
{
    public string Nickname { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public int Level { get; set; }
    public int VipLevel { get; set; }
    public string Gender { get; set; } = string.Empty;
    public DateTime? Birthday { get; set; }
    public string Region { get; set; } = string.Empty;
    public string Language { get; set; } = "zh-CN";
}

/// <summary>
/// 用户安全设置
/// </summary>
public class UserSecurity
{
    public bool TwoFactorEnabled { get; set; }
    public List<SecurityQuestion> SecurityQuestions { get; set; } = new();
    public List<LoginHistory> LoginHistory { get; set; } = new();
}

/// <summary>
/// 安全问题
/// </summary>
public class SecurityQuestion
{
    public string Question { get; set; } = string.Empty;
    public string AnswerHash { get; set; } = string.Empty;
}

/// <summary>
/// 登录历史
/// </summary>
public class LoginHistory
{
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public bool Success { get; set; }
}

/// <summary>
/// 用户偏好设置
/// </summary>
public class UserPreferences
{
    public string Language { get; set; } = "zh-CN";
    public bool SoundEnabled { get; set; } = true;
    public bool MusicEnabled { get; set; } = true;
    public bool NotificationEnabled { get; set; } = true;
}

/// <summary>
/// 用户游戏数据
/// </summary>
public class UserGameData
{
    public long TotalPlayTime { get; set; }
    public int AchievementPoints { get; set; }
    public string LastSelectedZone { get; set; } = string.Empty;
}

/// <summary>
/// 区服信息
/// </summary>
public class ZoneInfo
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string ZoneId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "online"; // online, maintenance, offline
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }
    public string LoadLevel { get; set; } = "low"; // low, medium, high
    public bool Recommendation { get; set; }
    public bool NewPlayerAllowed { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();

    public ZoneServers Servers { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// 区服服务器配置
/// </summary>
public class ZoneServers
{
    public string AuthServer { get; set; } = string.Empty;
    public string GameServer { get; set; } = string.Empty;
    public string BattleServer { get; set; } = string.Empty;
}

// === 请求/响应模型 ===

/// <summary>
/// 登录请求
/// </summary>
public class LoginRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string DeviceId { get; set; } = string.Empty;

    public ClientDeviceInfo DeviceInfo { get; set; } = new();
}

/// <summary>
/// 客户端设备信息
/// </summary>
public class ClientDeviceInfo
{
    public string Platform { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string UnityVersion { get; set; } = string.Empty;
}

/// <summary>
/// 注册请求
/// </summary>
public class RegisterRequest
{
    [Required]
    [StringLength(50, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare("Password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string InviteCode { get; set; } = string.Empty;

    [Required]
    public bool AgreementAccepted { get; set; }
}

// === 结果模型 ===

/// <summary>
/// 登录结果
/// </summary>
public class LoginResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public UserInfo? User { get; set; }
    public string GameTicket { get; set; } = string.Empty;
    public int GameTicketExpiresIn { get; set; }
}

/// <summary>
/// 注册结果
/// </summary>
public class RegisterResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserInfo? User { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Token 验证结果
/// </summary>
public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public UserClaims? UserClaims { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Token 刷新结果
/// </summary>
public class RefreshTokenResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
}

/// <summary>
/// 游戏票据结果
/// </summary>
public class GameTicketResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string GameTicket { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string ZoneId { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

/// <summary>
/// 游戏票据验证结果
/// </summary>
public class GameTicketValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public UserInfo? PlayerInfo { get; set; }
}

/// <summary>
/// 区服选择结果
/// </summary>
public class ZoneSelectionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string GameTicket { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public List<ServerEndpoint> GameServers { get; set; } = new();
    public List<ServerEndpoint> BattleServers { get; set; } = new();
}

/// <summary>
/// 服务器端点
/// </summary>
public class ServerEndpoint
{
    public string ServerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Priority { get; set; }
}

/// <summary>
/// 用户信息（用于响应）
/// </summary>
public class UserInfo
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserProfile Profile { get; set; } = new();
}

/// <summary>
/// 用户声明
/// </summary>
public class UserClaims
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

/// <summary>
/// API 响应包装器
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
