using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using GameApp.AuthServer.Models;
using GameApp.AuthServer.Services;

namespace GameApp.AuthServer.Controllers;

/// <summary>
/// 区服管理控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[EnableRateLimiting("ApiPolicy")]
public class ZoneController : ControllerBase
{
    private readonly IZoneService _zoneService;
    private readonly ILogger<ZoneController> _logger;

    public ZoneController(IZoneService zoneService, ILogger<ZoneController> logger)
    {
        _zoneService = zoneService;
        _logger = logger;
    }

    /// <summary>
    /// 获取区服列表
    /// </summary>
    /// <returns>区服列表</returns>
    [HttpGet("list")]
    [ProducesResponseType(typeof(ApiResponse<List<ZoneInfo>>), 200)]
    public async Task<IActionResult> GetZoneListAsync()
    {
        try
        {
            var zones = await _zoneService.GetZoneListAsync();

            return Ok(new ApiResponse<List<ZoneInfo>>
            {
                Success = true,
                Message = "获取区服列表成功",
                Data = zones,
                RequestId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting zone list");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 获取区服详情
    /// </summary>
    /// <param name="zoneId">区服ID</param>
    /// <returns>区服详情</returns>
    [HttpGet("{zoneId}")]
    [ProducesResponseType(typeof(ApiResponse<ZoneInfo>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetZoneAsync([FromRoute] string zoneId)
    {
        try
        {
            if (string.IsNullOrEmpty(zoneId))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "区服ID不能为空",
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            var zone = await _zoneService.GetZoneByIdAsync(zoneId);

            if (zone == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "区服不存在",
                    RequestId = HttpContext.TraceIdentifier
                });
            }

            return Ok(new ApiResponse<ZoneInfo>
            {
                Success = true,
                Message = "获取区服信息成功",
                Data = zone,
                RequestId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting zone: {ZoneId}", zoneId);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 选择区服
    /// </summary>
    /// <param name="request">区服选择请求</param>
    /// <returns>区服选择结果</returns>
    [HttpPost("select")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ZoneSelectionResult>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 401)]
    public async Task<IActionResult> SelectZoneAsync([FromBody] ZoneSelectionRequest request)
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

            // 获取用户ID
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

            var result = await _zoneService.SelectZoneAsync(userId, request.ZoneId);

            if (result.Success)
            {
                _logger.LogInformation("Zone selection successful for user {UserId}: {ZoneId}", userId, request.ZoneId);

                return Ok(new ApiResponse<ZoneSelectionResult>
                {
                    Success = true,
                    Message = result.Message,
                    Data = result,
                    RequestId = HttpContext.TraceIdentifier
                });
            }
            else
            {
                _logger.LogWarning("Zone selection failed for user {UserId}: {ZoneId}, Reason: {Message}",
                    userId, request.ZoneId, result.Message);

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
            _logger.LogError(ex, "Error selecting zone: {ZoneId}", request.ZoneId);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    /// <summary>
    /// 更新区服状态（仅限服务器调用）
    /// </summary>
    /// <param name="zoneId">区服ID</param>
    /// <param name="request">状态更新请求</param>
    /// <returns>更新结果</returns>
    [HttpPut("{zoneId}/status")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 403)]
    public async Task<IActionResult> UpdateZoneStatusAsync(
        [FromRoute] string zoneId,
        [FromBody] ZoneStatusUpdateRequest request)
    {
        try
        {
            // 验证请求来源（这里简化处理，实际项目中应该验证服务器身份）
            var serverToken = HttpContext.Request.Headers["X-Server-Token"].FirstOrDefault();
            if (string.IsNullOrEmpty(serverToken) || !IsValidServerToken(serverToken))
            {
                return Forbid();
            }

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

            await _zoneService.UpdateZoneStatusAsync(zoneId, request.Status, request.PlayerCount);

            _logger.LogInformation("Zone status updated: {ZoneId}, Status: {Status}, Players: {PlayerCount}",
                zoneId, request.Status, request.PlayerCount);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "区服状态更新成功",
                RequestId = HttpContext.TraceIdentifier
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating zone status: {ZoneId}", zoneId);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "系统内部错误",
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }

    #region Private Methods

    /// <summary>
    /// 验证服务器Token（简化实现）
    /// </summary>
    private bool IsValidServerToken(string token)
    {
        // 这里应该实现真正的服务器身份验证
        // 例如：验证JWT签名、检查服务器白名单等
        return token == "server_secret_token_2024"; // 仅用于演示
    }

    #endregion
}

/// <summary>
/// 区服选择请求
/// </summary>
public class ZoneSelectionRequest
{
    /// <summary>
    /// 区服ID
    /// </summary>
    [Required]
    public string ZoneId { get; set; } = string.Empty;
}

/// <summary>
/// 区服状态更新请求
/// </summary>
public class ZoneStatusUpdateRequest
{
    /// <summary>
    /// 区服状态
    /// </summary>
    [Required]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 当前玩家数
    /// </summary>
    [Range(0, int.MaxValue)]
    public int PlayerCount { get; set; }
}
