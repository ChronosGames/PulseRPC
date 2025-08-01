using StackExchange.Redis;
using GameApp.AuthServer.Models;

namespace GameApp.AuthServer.Services;

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthService : IAuthService
{
    private readonly IUserService _userService;
    private readonly ITokenService _tokenService;
    private readonly IGameTicketService _gameTicketService;
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserService userService,
        ITokenService tokenService,
        IGameTicketService gameTicketService,
        IConnectionMultiplexer redis,
        ILogger<AuthService> logger)
    {
        _userService = userService;
        _tokenService = tokenService;
        _gameTicketService = gameTicketService;
        _redisDatabase = redis.GetDatabase();
        _logger = logger;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    public async Task<LoginResult> LoginAsync(string username, string password, string deviceId, ClientDeviceInfo deviceInfo)
    {
        try
        {
            _logger.LogInformation("User login attempt: {Username}", username);

            // 查找用户
            var user = await _userService.GetUserByUsernameAsync(username);
            if (user == null)
            {
                _logger.LogWarning("Login failed: User not found - {Username}", username);
                return new LoginResult
                {
                    Success = false,
                    Message = "用户名或密码错误"
                };
            }

            // 检查用户状态
            if (user.Status != "active")
            {
                _logger.LogWarning("Login failed: User not active - {Username}, Status: {Status}", username, user.Status);
                return new LoginResult
                {
                    Success = false,
                    Message = user.Status switch
                    {
                        "banned" => "账号已被封禁",
                        "inactive" => "账号未激活",
                        _ => "账号状态异常"
                    }
                };
            }

            // 验证密码
            if (!_userService.VerifyPassword(password, user.PasswordHash, user.Salt))
            {
                _logger.LogWarning("Login failed: Invalid password - {Username}", username);

                // 记录失败的登录尝试
                await RecordFailedLoginAttemptAsync(user.UserId, deviceId);

                return new LoginResult
                {
                    Success = false,
                    Message = "用户名或密码错误"
                };
            }

            // 检查设备锁定
            if (await IsDeviceLockedAsync(deviceId))
            {
                _logger.LogWarning("Login failed: Device locked - {Username}, DeviceId: {DeviceId}", username, deviceId);
                return new LoginResult
                {
                    Success = false,
                    Message = "设备已被锁定，请稍后重试"
                };
            }

            // 生成 JWT Token
            var accessToken = _tokenService.GenerateJwtToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // 存储刷新 Token
            var refreshTokenKey = $"refresh_token:{user.UserId}";
            await _redisDatabase.StringSetAsync(refreshTokenKey, refreshToken, TimeSpan.FromDays(30));

            // 生成游戏票据（默认选择推荐区服）
            var gameTicketResult = await _gameTicketService.GenerateGameTicketAsync(user.UserId, "zone001", "gameserver-dev");

            // 更新用户登录信息
            await _userService.UpdateUserLoginInfoAsync(user.UserId, "127.0.0.1", deviceInfo.Platform);

            // 清除失败的登录尝试记录
            await ClearFailedLoginAttemptsAsync(user.UserId);

            // 创建会话
            await CreateUserSessionAsync(user.UserId, accessToken, deviceId, deviceInfo);

            _logger.LogInformation("User login successful: {Username} (ID: {UserId})", username, user.UserId);

            return new LoginResult
            {
                Success = true,
                Message = "登录成功",
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600, // 1小时
                TokenType = "Bearer",
                User = new UserInfo
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Email = user.Email,
                    Profile = user.Profile
                },
                GameTicket = gameTicketResult.GameTicket,
                GameTicketExpiresIn = gameTicketResult.ExpiresIn
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user: {Username}", username);
            return new LoginResult
            {
                Success = false,
                Message = "系统错误，请稍后重试"
            };
        }
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    public async Task<RegisterResult> RegisterAsync(RegisterRequest request)
    {
        try
        {
            _logger.LogInformation("User registration attempt: {Username}, Email: {Email}", request.Username, request.Email);

            // 验证请求参数
            var validationErrors = ValidateRegisterRequest(request);
            if (validationErrors.Any())
            {
                return new RegisterResult
                {
                    Success = false,
                    Message = "请求参数验证失败",
                    Errors = validationErrors
                };
            }

            // 创建用户
            var user = await _userService.CreateUserAsync(request);

            _logger.LogInformation("User registration successful: {Username} (ID: {UserId})", request.Username, user.UserId);

            return new RegisterResult
            {
                Success = true,
                Message = "注册成功",
                User = new UserInfo
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Email = user.Email,
                    Profile = user.Profile
                }
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Registration failed: {Message}", ex.Message);
            return new RegisterResult
            {
                Success = false,
                Message = ex.Message,
                Errors = new List<string> { ex.Message }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
            return new RegisterResult
            {
                Success = false,
                Message = "系统错误，请稍后重试",
                Errors = new List<string> { "Registration failed due to system error" }
            };
        }
    }

    /// <summary>
    /// 验证 JWT Token
    /// </summary>
    public async Task<TokenValidationResult> ValidateTokenAsync(string token)
    {
        try
        {
            var result = _tokenService.ValidateJwtToken(token);

            if (result.IsValid && result.UserClaims != null)
            {
                // 检查用户会话是否存在
                var sessionExists = await CheckUserSessionAsync(result.UserClaims.UserId, token);
                if (!sessionExists)
                {
                    _logger.LogWarning("Token validation failed: Session not found for user {UserId}", result.UserClaims.UserId);
                    return new TokenValidationResult
                    {
                        IsValid = false,
                        Message = "Session expired or invalid"
                    };
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return new TokenValidationResult
            {
                IsValid = false,
                Message = "Token validation error"
            };
        }
    }

    /// <summary>
    /// 刷新 Token
    /// </summary>
    public async Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken)
    {
        try
        {
            // 查找刷新 Token 对应的用户
            var server = _redisDatabase.Multiplexer.GetServer(_redisDatabase.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: "refresh_token:*");

            foreach (var key in keys)
            {
                var storedToken = await _redisDatabase.StringGetAsync(key);
                if (storedToken == refreshToken)
                {
                    // 提取用户ID
                    var userId = int.Parse(key.ToString().Split(':')[2]);

                    // 获取用户信息
                    var user = await _userService.GetUserByIdAsync(userId);
                    if (user == null || user.Status != "active")
                    {
                        return new RefreshTokenResult
                        {
                            Success = false,
                            Message = "User not found or inactive"
                        };
                    }

                    // 生成新的访问 Token
                    var newAccessToken = _tokenService.GenerateJwtToken(user);

                    _logger.LogInformation("Token refreshed for user: {UserId}", userId);

                    return new RefreshTokenResult
                    {
                        Success = true,
                        AccessToken = newAccessToken,
                        ExpiresIn = 3600,
                        TokenType = "Bearer",
                        Message = "Token refreshed successfully"
                    };
                }
            }

            _logger.LogWarning("Invalid refresh token provided");
            return new RefreshTokenResult
            {
                Success = false,
                Message = "Invalid refresh token"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return new RefreshTokenResult
            {
                Success = false,
                Message = "Token refresh error"
            };
        }
    }

    /// <summary>
    /// 用户登出
    /// </summary>
    public async Task<bool> LogoutAsync(int userId, string token)
    {
        try
        {
            // 删除用户会话
            var sessionKey = $"session:{userId}";
            await _redisDatabase.KeyDeleteAsync(sessionKey);

            // 删除刷新 Token
            var refreshTokenKey = $"refresh_token:{userId}";
            await _redisDatabase.KeyDeleteAsync(refreshTokenKey);

            // 将 Token 加入黑名单
            var blacklistKey = $"token_blacklist:{token}";
            await _redisDatabase.StringSetAsync(blacklistKey, "1", TimeSpan.FromHours(24));

            _logger.LogInformation("User logged out: {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout for user: {UserId}", userId);
            return false;
        }
    }

    #region Private Methods

    /// <summary>
    /// 验证注册请求参数
    /// </summary>
    private List<string> ValidateRegisterRequest(RegisterRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3 || request.Username.Length > 50)
        {
            errors.Add("用户名长度必须在3-50个字符之间");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            errors.Add("密码长度不能少于6个字符");
        }

        if (request.Password != request.ConfirmPassword)
        {
            errors.Add("密码和确认密码不匹配");
        }

        if (!IsValidEmail(request.Email))
        {
            errors.Add("邮箱格式不正确");
        }

        if (!request.AgreementAccepted)
        {
            errors.Add("必须同意用户协议");
        }

        return errors;
    }

    /// <summary>
    /// 验证邮箱格式
    /// </summary>
    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 记录失败的登录尝试
    /// </summary>
    private async Task RecordFailedLoginAttemptAsync(int userId, string deviceId)
    {
        var key = $"failed_login:{deviceId}";
        await _redisDatabase.StringIncrementAsync(key);
        await _redisDatabase.KeyExpireAsync(key, TimeSpan.FromMinutes(15));
    }

    /// <summary>
    /// 检查设备是否被锁定
    /// </summary>
    private async Task<bool> IsDeviceLockedAsync(string deviceId)
    {
        var key = $"failed_login:{deviceId}";
        var failedAttempts = await _redisDatabase.StringGetAsync(key);
        return failedAttempts.HasValue && failedAttempts > 5;
    }

    /// <summary>
    /// 清除失败的登录尝试记录
    /// </summary>
    private async Task ClearFailedLoginAttemptsAsync(int userId)
    {
        // 这里可以根据用户ID相关的设备清除记录
        // 简化实现，实际可能需要存储用户-设备关联关系
    }

    /// <summary>
    /// 创建用户会话
    /// </summary>
    private async Task CreateUserSessionAsync(int userId, string accessToken, string deviceId, ClientDeviceInfo deviceInfo)
    {
        var sessionKey = $"session:{userId}";
        var sessionData = new
        {
            AccessToken = accessToken,
            DeviceId = deviceId,
            DeviceInfo = deviceInfo,
            LoginTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        await _redisDatabase.StringSetAsync(sessionKey,
            System.Text.Json.JsonSerializer.Serialize(sessionData),
            TimeSpan.FromHours(24));
    }

    /// <summary>
    /// 检查用户会话
    /// </summary>
    private async Task<bool> CheckUserSessionAsync(int userId, string token)
    {
        var sessionKey = $"session:{userId}";
        var sessionData = await _redisDatabase.StringGetAsync(sessionKey);

        if (!sessionData.HasValue)
        {
            return false;
        }

        // 检查 Token 是否在黑名单中
        var blacklistKey = $"token_blacklist:{token}";
        var isBlacklisted = await _redisDatabase.KeyExistsAsync(blacklistKey);

        return !isBlacklisted;
    }

    #endregion
}
