using Microsoft.Extensions.Logging;
using PulseRPC.Network;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server.Handlers;

/// <summary>
/// 获取用户信息请求处理器
/// </summary>
[Handler(false)]
public class GetUserInfoRequestHandler : IRequestHandler<int, GetUserInfoResponse>
{
    private readonly ILogger<GetUserInfoRequestHandler> _logger;

    /// <summary>
    /// 初始化获取用户信息请求处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public GetUserInfoRequestHandler(ILogger<GetUserInfoRequestHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 处理获取用户信息请求
    /// </summary>
    public Task<GetUserInfoResponse> HandleAsync(NetworkSession context, int userId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("收到获取用户信息请求: UserId={UserId}", userId);

        // 检查认证状态
        var isAuthenticated = context.GetItem<bool>("IsAuthenticated");
        if (!isAuthenticated)
        {
            return Task.FromResult(new GetUserInfoResponse
            {
                Status = ResponseStatus.AuthenticationFailed,
                ErrorMessage = "未授权访问，请先登录"
            });
        }

        // 构造响应
        var response = new GetUserInfoResponse
        {
            Status = ResponseStatus.Success
        };

        // 模拟获取用户信息
        if (userId == 1001)
        {
            response.UserId = 1001;
            response.Username = "admin";
            response.Nickname = "管理员";
            response.AvatarUrl = "https://example.com/avatars/admin.png";
            response.UserStatus = UserStatus.Online;
            response.RegisterTime = new DateTime(2021, 1, 1);
            response.LastLoginTime = DateTime.Now;
        }
        else
        {
            // 模拟生成随机用户信息
            response.UserId = userId;
            response.Username = $"user{userId}";
            response.Nickname = $"用户{userId}";
            response.AvatarUrl = $"https://example.com/avatars/user{userId}.png";
            response.UserStatus = (UserStatus)new Random().Next(0, 3);
            response.RegisterTime = DateTime.Now.AddDays(-new Random().Next(1, 365));
            response.LastLoginTime = DateTime.Now.AddHours(-new Random().Next(1, 24));
        }

        _logger.LogInformation("获取用户信息成功: UserId={UserId}, Username={Username}", response.UserId, response.Username);

        return Task.FromResult(response);
    }
}

/// <summary>
/// 更新用户信息请求处理器
/// </summary>
[Handler]
public class UpdateUserInfoRequestHandler : IRequestHandler<UpdateUserInfoRequest, ValueTuple<ResponseStatus, string, int>>
{
    private readonly ILogger<UpdateUserInfoRequestHandler> _logger;

    /// <summary>
    /// 初始化更新用户信息请求处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public UpdateUserInfoRequestHandler(ILogger<UpdateUserInfoRequestHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 处理更新用户信息请求
    /// </summary>
    public async Task<ValueTuple<ResponseStatus, string, int>> HandleAsync(NetworkSession context, UpdateUserInfoRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("收到更新用户信息请求: UserId={UserId}", request.UserId);

        // 检查认证状态
        bool isAuthenticated = context.GetItem<bool>("IsAuthenticated");
        if (!isAuthenticated)
        {
            return ValueTuple.Create(ResponseStatus.AuthenticationFailed, "未授权访问，请先登录", 0);
        }

        // 检查用户ID是否匹配
        int userId = context.GetItem<int>("UserId");
        if (userId != request.UserId)
        {
            return ValueTuple.Create(ResponseStatus.PermissionDenied, "无权修改其他用户的信息", 0);
        }

        // 模拟更新数据
        int updatedCount = 0;
        if (!string.IsNullOrEmpty(request.Nickname))
        {
            updatedCount++;
        }
        if (!string.IsNullOrEmpty(request.AvatarUrl))
        {
            updatedCount++;
        }

        _logger.LogInformation("更新用户信息成功: UserId={UserId}, Nickname={Nickname}, 更新字段数={UpdatedCount}",
            request.UserId, request.Nickname, updatedCount);

        return ValueTuple.Create(ResponseStatus.Success, string.Empty, updatedCount);
    }
}
