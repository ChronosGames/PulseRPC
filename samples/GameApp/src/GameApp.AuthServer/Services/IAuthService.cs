using GameApp.AuthServer.Models;

namespace GameApp.AuthServer.Services;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 用户登录
    /// </summary>
    Task<LoginResult> LoginAsync(string username, string password, string deviceId, ClientDeviceInfo deviceInfo);

    /// <summary>
    /// 用户注册
    /// </summary>
    Task<RegisterResult> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// 验证 JWT Token
    /// </summary>
    Task<TokenValidationResult> ValidateTokenAsync(string token);

    /// <summary>
    /// 刷新 Token
    /// </summary>
    Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// 用户登出
    /// </summary>
    Task<bool> LogoutAsync(int userId, string token);
}

/// <summary>
/// 游戏票据服务接口
/// </summary>
public interface IGameTicketService
{
    /// <summary>
    /// 生成游戏票据
    /// </summary>
    Task<GameTicketResult> GenerateGameTicketAsync(int userId, string zoneId, string serverId);

    /// <summary>
    /// 验证游戏票据
    /// </summary>
    Task<GameTicketValidationResult> ValidateGameTicketAsync(string gameTicket, string serverId);

    /// <summary>
    /// 使用游戏票据（一次性使用）
    /// </summary>
    Task<bool> ConsumeGameTicketAsync(string gameTicket);
}

/// <summary>
/// Token 服务接口
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    string GenerateJwtToken(User user);

    /// <summary>
    /// 生成刷新 Token
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// 验证 JWT Token
    /// </summary>
    TokenValidationResult ValidateJwtToken(string token);

    /// <summary>
    /// 从 Token 中提取用户信息
    /// </summary>
    UserClaims? ExtractUserClaims(string token);
}

/// <summary>
/// 用户服务接口
/// </summary>
public interface IUserService
{
    /// <summary>
    /// 根据用户名获取用户
    /// </summary>
    Task<User?> GetUserByUsernameAsync(string username);

    /// <summary>
    /// 根据邮箱获取用户
    /// </summary>
    Task<User?> GetUserByEmailAsync(string email);

    /// <summary>
    /// 根据用户ID获取用户
    /// </summary>
    Task<User?> GetUserByIdAsync(int userId);

    /// <summary>
    /// 创建新用户
    /// </summary>
    Task<User> CreateUserAsync(RegisterRequest request);

    /// <summary>
    /// 更新用户登录信息
    /// </summary>
    Task UpdateUserLoginInfoAsync(int userId, string ip, string userAgent);

    /// <summary>
    /// 验证密码
    /// </summary>
    bool VerifyPassword(string password, string passwordHash, string salt);

    /// <summary>
    /// 生成密码哈希
    /// </summary>
    (string hash, string salt) GeneratePasswordHash(string password);
}

/// <summary>
/// 区服服务接口
/// </summary>
public interface IZoneService
{
    /// <summary>
    /// 获取所有区服列表
    /// </summary>
    Task<List<ZoneInfo>> GetZoneListAsync();

    /// <summary>
    /// 获取区服详情
    /// </summary>
    Task<ZoneInfo?> GetZoneByIdAsync(string zoneId);

    /// <summary>
    /// 选择区服
    /// </summary>
    Task<ZoneSelectionResult> SelectZoneAsync(int userId, string zoneId);

    /// <summary>
    /// 更新区服状态
    /// </summary>
    Task UpdateZoneStatusAsync(string zoneId, string status, int playerCount);
}
