using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.LoginServer.Services;
using DistributedGameApp.Shared.Domain.Accounts;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;

namespace DistributedGameApp.LoginServer.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly JwtService _jwtService;
    private readonly AccountRepository _accountRepository;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        JwtService jwtService,
        AccountRepository accountRepository,
        ILogger<AuthController> logger)
    {
        _jwtService = jwtService;
        _accountRepository = accountRepository;
        _logger = logger;
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            // 验证输入
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
            {
                return BadRequest(new { message = "用户名至少3个字符" });
            }

            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            {
                return BadRequest(new { message = "密码至少6个字符" });
            }

            if (!IsValidEmail(request.Email))
            {
                return BadRequest(new { message = "邮箱格式不正确" });
            }

            // 检查用户名是否已存在
            var existingUser = await _accountRepository.GetByUserIdAsync(request.Username);
            if (existingUser != null)
            {
                return BadRequest(new { message = "用户名已存在" });
            }

            // 检查邮箱是否已存在
            var existingEmail = await _accountRepository.GetByEmailAsync(request.Email);
            if (existingEmail != null)
            {
                return BadRequest(new { message = "邮箱已被注册" });
            }

            // 创建账户
            var account = new Account
            {
                Id = ObjectId.GenerateNewId(),
                UserId = request.Username,
                Username = request.Username,
                Email = request.Email,
                Provider = "local", // 本地账号
                ProviderUserId = request.Username, // 本地账号使用 Username 作为 ProviderUserId
                PasswordHash = HashPassword(request.Password), // 密码哈希存储在专用字段
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                LastLoginIp = GetClientIp(),
                Status = AccountStatus.Normal
            };

            await _accountRepository.InsertAsync(account);

            // 生成令牌
            var accessToken = _jwtService.GenerateAccessToken(account.UserId, account.Username);
            var refreshToken = _jwtService.GenerateRefreshToken(account.UserId);

            _logger.LogInformation("User registered: {UserId}", account.UserId);

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600,
                TokenType = "Bearer",
                UserId = account.UserId,
                Username = account.Username
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed");
            return StatusCode(500, new { message = "注册失败，请稍后再试" });
        }
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            // 获取账户
            var account = await _accountRepository.GetByUserIdAsync(request.UsernameOrEmail);
            if (account == null)
            {
                // 尝试通过邮箱查找
                account = await _accountRepository.GetByEmailAsync(request.UsernameOrEmail);
            }

            if (account == null)
            {
                return Unauthorized(new { message = "用户名或密码错误" });
            }

            // 验证密码（密码哈希存储在 PasswordHash 字段）
            if (account.Provider != "local" || !VerifyPassword(request.Password, account.PasswordHash))
            {
                return Unauthorized(new { message = "用户名或密码错误" });
            }

            // 检查账户状态
            if (account.Status.Code == AccountStatus.Banned.Code)
            {
                return Unauthorized(new { message = "账户已被封禁" });
            }

            // 更新最后登录信息
            await _accountRepository.UpdateLastLoginAsync(account.UserId, GetClientIp());

            // 生成令牌
            var accessToken = _jwtService.GenerateAccessToken(account.UserId, account.Username);
            var refreshToken = _jwtService.GenerateRefreshToken(account.UserId);

            _logger.LogInformation("User logged in: {UserId}", account.UserId);

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600,
                TokenType = "Bearer",
                UserId = account.UserId,
                Username = account.Username
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed");
            return StatusCode(500, new { message = "登录失败，请稍后再试" });
        }
    }

    /// <summary>
    /// 刷新令牌
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            // 验证刷新令牌
            if (!_jwtService.IsRefreshToken(request.RefreshToken))
            {
                return Unauthorized(new { message = "无效的刷新令牌" });
            }

            var userId = _jwtService.GetUserIdFromToken(request.RefreshToken);
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new { message = "无效的刷新令牌" });
            }

            // 获取账户
            var account = await _accountRepository.GetByUserIdAsync(userId);
            if (account == null || account.Status.Code == AccountStatus.Banned.Code)
            {
                return Unauthorized(new { message = "账户不存在或已被封禁" });
            }

            // 生成新的访问令牌
            var accessToken = _jwtService.GenerateAccessToken(account.UserId, account.Username);
            var newRefreshToken = _jwtService.GenerateRefreshToken(account.UserId);

            _logger.LogInformation("Token refreshed for user: {UserId}", userId);

            return Ok(new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpiresIn = 3600,
                TokenType = "Bearer",
                UserId = account.UserId,
                Username = account.Username
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            return Unauthorized(new { message = "令牌刷新失败" });
        }
    }

    /// <summary>
    /// 哈希密码
    /// </summary>
    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password + "DistributedGameApp_Salt_2025");
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 验证密码
    /// </summary>
    private static bool VerifyPassword(string password, string hash)
    {
        return HashPassword(password) == hash;
    }

    /// <summary>
    /// 验证邮箱格式
    /// </summary>
    private static bool IsValidEmail(string email)
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
    /// 获取客户端IP
    /// </summary>
    private string GetClientIp()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}

/// <summary>
/// 注册请求
/// </summary>
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

/// <summary>
/// 登录请求
/// </summary>
public class LoginRequest
{
    public string UsernameOrEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 刷新令牌请求
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// 认证响应
/// </summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
