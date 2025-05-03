using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol.Handlers;
using PulseRPC.Protocol.Network;
using PulseRPC.Samples.Shared;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Server.Handlers
{
    /// <summary>
    /// 登录请求处理器
    /// </summary>
    public class LoginRequestHandler : RequestHandlerBase<LoginRequest, LoginResponse>
    {
        private readonly ILogger<LoginRequestHandler> _logger;

        /// <summary>
        /// 创建登录请求处理器
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public LoginRequestHandler(ILogger<LoginRequestHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 处理登录请求
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="request">登录请求</param>
        /// <returns>登录响应</returns>
        protected override async Task<LoginResponse> ProcessRequestAsync(SessionContext context, LoginRequest request)
        {
            _logger.LogInformation("处理登录请求: {Username}, 客户端版本: {Version}", request.Username, request.ClientVersion);

            // 模拟处理延迟
            await Task.Delay(100);

            // 模拟简单的验证逻辑
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                _logger.LogWarning("登录失败: 用户名或密码为空");
                return new LoginResponse
                {
                    Status = ResponseStatus.InvalidParameter,
                    ErrorMessage = "用户名和密码不能为空"
                };
            }

            // 模拟用户验证 (在实际应用中应通过数据库验证)
            if (request.Username == "admin" && request.Password == "password")
            {
                _logger.LogInformation("登录成功: {Username}", request.Username);
                return new LoginResponse
                {
                    Status = ResponseStatus.Success,
                    Token = "token-" + Guid.NewGuid().ToString("N"),
                    UserId = 1001,
                    ErrorMessage = string.Empty
                };
            }
            else
            {
                _logger.LogWarning("登录失败: 用户名或密码错误");
                return new LoginResponse
                {
                    Status = ResponseStatus.AuthenticationFailed,
                    ErrorMessage = "用户名或密码错误"
                };
            }
        }
    }

    /// <summary>
    /// 注册请求处理器
    /// </summary>
    public class RegisterRequestHandler : RequestHandlerBase<RegisterRequest, RegisterResponse>
    {
        private readonly ILogger<RegisterRequestHandler> _logger;

        /// <summary>
        /// 创建注册请求处理器
        /// </summary>
        /// <param name="logger">日志记录器</param>
        public RegisterRequestHandler(ILogger<RegisterRequestHandler> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 处理注册请求
        /// </summary>
        /// <param name="context">会话上下文</param>
        /// <param name="request">注册请求</param>
        /// <returns>注册响应</returns>
        protected override async Task<RegisterResponse> ProcessRequestAsync(SessionContext context, RegisterRequest request)
        {
            _logger.LogInformation("处理注册请求: {Username}, Email: {Email}", request.Username, request.Email);

            // 模拟处理延迟
            await Task.Delay(200);

            // 模拟简单的验证逻辑
            if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
            {
                _logger.LogWarning("注册失败: 用户名或密码为空");
                return new RegisterResponse
                {
                    Status = ResponseStatus.InvalidParameter,
                    ErrorMessage = "用户名和密码不能为空"
                };
            }

            // 模拟检查用户名是否已存在
            if (request.Username == "admin")
            {
                _logger.LogWarning("注册失败: 用户名已存在");
                return new RegisterResponse
                {
                    Status = ResponseStatus.AlreadyExists,
                    ErrorMessage = "用户名已存在"
                };
            }

            // 模拟成功注册
            int newUserId = new Random().Next(10000, 99999);
            _logger.LogInformation("注册成功: {Username}, 分配用户ID: {UserId}", request.Username, newUserId);

            return new RegisterResponse
            {
                Status = ResponseStatus.Success,
                UserId = newUserId,
                ErrorMessage = string.Empty
            };
        }
    }
}
