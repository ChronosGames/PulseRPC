using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GameApp.AuthServer.Models;
using GameApp.AuthServer.Services;

namespace GameApp.AuthServer.Controllers;

/// <summary>
/// 认证控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="request">登录请求</param>
    /// <returns>登录结果</returns>
    [HttpPost("login")]
    [EnableRateLimiting("LoginPolicy")]
    [ProducesResponseType(typeof(ApiResponse<LoginResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 429)]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "请求参数无效",
                    Errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList(),
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            var result = await _authService.LoginAsync(
                request.Username,
                request.Password,
                request.DeviceId,
                request.DeviceInfo);

            if (result.Success)
            {
                _logger.LogInformation("User login successful: {Username}", request.Username);

                return Ok(new ApiResponse<LoginResult>
                {
                    Success = true,
                    Message = result.Message,
                    Data = result,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
            else
            {
                _logger.LogWarning("User login failed: {Username}, Reason: {Message}",
                    request.Username, result.Message);

                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.Message,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 用户注册
    /// </summary>
    /// <param name="request">注册请求</param>
    /// <returns>注册结果</returns>
    [HttpPost("register")]
    [EnableRateLimiting("ApiPolicy")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "请求参数无效",
                    Errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .ToList(),
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            var result = await _authService.RegisterAsync(request);

            if (result.Success)
            {
                _logger.LogInformation("User registration successful: {Username}", request.Username);

                return Ok(new ApiResponse<RegisterResult>
                {
                    Success = true,
                    Message = result.Message,
                    Data = result,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
            else
            {
                _logger.LogWarning("User registration failed: {Username}, Errors: {Errors}",
                    request.Username, string.Join(", ", result.Errors));

                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.Message,
                    Errors = result.Errors,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 刷新 Token
    /// </summary>
    /// <param name="request">刷新Token请求</param>
    /// <returns>新的Token</returns>
    [HttpPost("refresh-token")]
    [EnableRateLimiting("ApiPolicy")]
    [ProducesResponseType(typeof(ApiResponse<RefreshTokenResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> RefreshTokenAsync([FromBody] RefreshTokenRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.RefreshToken))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "刷新Token不能为空",
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            var result = await _authService.RefreshTokenAsync(request.RefreshToken);

            if (result.Success)
            {
                return Ok(new ApiResponse<RefreshTokenResult>
                {
                    Success = true,
                    Message = result.Message,
                    Data = result,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
            else
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = result.Message,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 用户登出
    /// </summary>
    /// <returns>登出结果</returns>
    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting("ApiPolicy")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    public async Task<IActionResult> LogoutAsync()
    {
        try
        {
            var userIdClaim = HttpContext.User.FindFirst("user_id");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "无效的用户信息",
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            // 从Authorization头获取Token
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "无效的认证头",
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var success = await _authService.LogoutAsync(userId, token);

            if (success)
            {
                _logger.LogInformation("User logout successful: {UserId}", userId);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "登出成功",
                    RequestId = HttpContext.TraceIdentifier
                });
            }
            else
            {
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "登出失败",
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 验证Token
    /// </summary>
    /// <returns>Token验证结果</returns>
    [HttpGet("validate-token")]
    [Authorize]
    [EnableRateLimiting("ApiPolicy")]
    [ProducesResponseType(typeof(ApiResponse<TokenValidationResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    public async Task<IActionResult> ValidateTokenAsync()
    {
        try
        {
            // 从Authorization头获取Token
            var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "无效的认证头",
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var result = await _authService.ValidateTokenAsync(token);

            return Ok(new ApiResponse<TokenValidationResult>
            {
                Success = result.IsValid,
                Message = result.Message,
                Data = result,
                RequestId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }
}

/// <summary>
/// 刷新Token请求
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
