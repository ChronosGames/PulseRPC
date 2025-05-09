using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Network;
using PulseRPC.Samples.Server.Services;
using PulseRPC.Samples.Shared.Messages;
using PulseRPC.Server;

namespace PulseRPC.Samples.Server.Handlers;

/// <summary>
/// 登录请求处理器
/// </summary>
public class LoginRequestHandler : RequestHandlerBase<LoginRequest, LoginResponse>
{
    private readonly ILogger<LoginRequestHandler> _logger;
    private readonly NotificationService _notificationService;

    /// <summary>
    /// 初始化登录请求处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="notificationService">通知服务</param>
    public LoginRequestHandler(
        ILogger<LoginRequestHandler> logger,
        NotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
    }

    /// <summary>
    /// 处理登录请求
    /// </summary>
    protected override async Task<LoginResponse> ProcessRequestAsync(NetworkSession context, LoginRequest request)
    {
        _logger.LogInformation("收到登录请求: 用户名={Username}, 密码={Password}", request.Username, "******");

        // 构造响应
        var response = new LoginResponse
        {
            RequestId = request.RequestId
        };

        // 模拟验证
        if (request.Username == "admin" && request.Password == "password")
        {
            // 设置会话状态
            context.SetItem("UserId", 1001);
            context.SetItem("Username", request.Username);
            context.SetItem("IsAuthenticated", true);

            // 设置响应
            response.Success = true;
            response.UserId = 1001;
            response.Username = request.Username;
            response.Token = $"token-{Guid.NewGuid():N}";

            _logger.LogInformation("用户 {Username} 登录成功", request.Username);

            // 发送用户状态通知
            await _notificationService.SendUserStatusNotificationAsync(
                userId: 1001,
                username: request.Username,
                status: 1,
                lastLoginTime: DateTimeOffset.Now.ToUnixTimeMilliseconds());
        }
        else
        {
            // 设置响应
            response.Success = false;
            response.ErrorCode = 1001;
            response.ErrorMessage = "用户名或密码错误";

            _logger.LogWarning("用户 {Username} 登录失败: 密码错误", request.Username);
        }

        return response;
    }
}

/// <summary>
/// 注册请求处理器
/// </summary>
public class RegisterRequestHandler : RequestHandlerBase<RegisterRequest, RegisterResponse>
{
    private readonly ILogger<RegisterRequestHandler> _logger;

    /// <summary>
    /// 初始化注册请求处理器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public RegisterRequestHandler(ILogger<RegisterRequestHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 处理注册请求
    /// </summary>
    protected override async Task<RegisterResponse> ProcessRequestAsync(NetworkSession context, RegisterRequest request)
    {
        _logger.LogInformation("收到注册请求: 用户名={Username}, 邮箱={Email}", request.Username, request.Email);

        // 构造响应
        var response = new RegisterResponse
        {
            RequestId = request.RequestId
        };

        // 模拟注册
        if (request.Username != "admin")
        {
            // 设置响应
            response.Success = true;
            response.UserId = new Random().Next(1000, 9999);
            response.Username = request.Username;

            _logger.LogInformation("用户 {Username} 注册成功，用户ID={UserId}", request.Username, response.UserId);
        }
        else
        {
            // 设置响应
            response.Success = false;
            response.ErrorCode = 1002;
            response.ErrorMessage = "用户名已存在";

            _logger.LogWarning("用户 {Username} 注册失败: 用户名已存在", request.Username);
        }

        return response;
    }
}
