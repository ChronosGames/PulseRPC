using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Handlers;
using PulseRPC.Protocol.Network;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Server.Handlers;

/// <summary>
/// 获取用户信息请求处理器
/// </summary>
public class GetUserInfoRequestHandler : RequestHandlerBase<GetUserInfoRequest, GetUserInfoResponse>
{
    private readonly ILogger<GetUserInfoRequestHandler> _logger;

    /// <summary>
    /// 创建获取用户信息请求处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public GetUserInfoRequestHandler(ILogger<GetUserInfoRequestHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理获取用户信息请求
    /// </summary>
    /// <param name="context">会话上下文</param>
    /// <param name="request">请求</param>
    /// <returns>响应</returns>
    protected override async Task<GetUserInfoResponse> ProcessRequestAsync(SessionContext context, GetUserInfoRequest request)
    {
        _logger.LogInformation("处理获取用户信息请求: 用户ID={UserId}", request.UserId);

        // 模拟处理延迟
        await Task.Delay(150);

        // 检查用户ID
        if (request.UserId <= 0)
        {
            _logger.LogWarning("获取用户信息失败: 无效的用户ID: {UserId}", request.UserId);
            return new GetUserInfoResponse
            {
                Status = ResponseStatus.InvalidParameter,
                ErrorMessage = "无效的用户ID"
            };
        }

        // 模拟查询用户信息
        // 在实际应用中应从数据库查询
        if (request.UserId == 1001)
        {
            _logger.LogInformation("找到用户: UserId={UserId}", request.UserId);
            return new GetUserInfoResponse
            {
                Status = ResponseStatus.Success,
                ErrorMessage = string.Empty,
                UserId = 1001,
                Username = "admin",
                Nickname = "管理员",
                AvatarUrl = "https://example.com/avatar/admin.png",
                UserStatus = UserStatus.Online,
                RegisterTime = new DateTime(2023, 1, 1),
                LastLoginTime = DateTime.Now.AddHours(-1)
            };
        }
        else
        {
            _logger.LogWarning("未找到用户: UserId={UserId}", request.UserId);
            return new GetUserInfoResponse
            {
                Status = ResponseStatus.NotFound,
                ErrorMessage = "用户不存在"
            };
        }
    }
}

/// <summary>
/// 更新用户信息请求处理器
/// </summary>
public class UpdateUserInfoRequestHandler : RequestHandlerBase<UpdateUserInfoRequest, UpdateUserInfoResponse>
{
    private readonly ILogger<UpdateUserInfoRequestHandler> _logger;

    /// <summary>
    /// 创建更新用户信息请求处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public UpdateUserInfoRequestHandler(ILogger<UpdateUserInfoRequestHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理更新用户信息请求
    /// </summary>
    /// <param name="context">会话上下文</param>
    /// <param name="request">请求</param>
    /// <returns>响应</returns>
    protected override async Task<UpdateUserInfoResponse> ProcessRequestAsync(SessionContext context, UpdateUserInfoRequest request)
    {
        _logger.LogInformation("处理更新用户信息请求: 用户ID={UserId}", request.UserId);

        // 模拟处理延迟
        await Task.Delay(180);

        // 检查用户ID
        if (request.UserId <= 0)
        {
            _logger.LogWarning("更新用户信息失败: 无效的用户ID: {UserId}", request.UserId);
            return new UpdateUserInfoResponse
            {
                Status = ResponseStatus.InvalidParameter,
                ErrorMessage = "无效的用户ID"
            };
        }

        // 检查是否有权限
        // 在实际应用中应检查用户身份验证和授权
        if (request.UserId != 1001) // 假设只有ID为1001的用户有权限
        {
            _logger.LogWarning("更新用户信息失败: 无权限修改用户 {UserId}", request.UserId);
            return new UpdateUserInfoResponse
            {
                Status = ResponseStatus.PermissionDenied,
                ErrorMessage = "无权修改此用户信息"
            };
        }

        // 模拟更新用户信息
        // 在实际应用中应更新数据库
        int updatedCount = 0;

        // 计算更新字段数量
        if (!string.IsNullOrEmpty(request.Nickname))
            updatedCount++;

        if (!string.IsNullOrEmpty(request.AvatarUrl))
            updatedCount++;

        _logger.LogInformation("已更新用户信息: UserId={UserId}, 更新字段数量={Count}", request.UserId, updatedCount);

        return new UpdateUserInfoResponse
        {
            Status = ResponseStatus.Success,
            ErrorMessage = string.Empty,
            UpdatedCount = updatedCount
        };
    }
}
