using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using PulseRPC.Transport;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Authentication;

/// <summary>
/// 认证中间件，提供基于特性的自动认证功能
/// </summary>
public class AuthenticationMiddleware
{
    private readonly IAuthenticationProvider? _authenticationProvider;
    private readonly IAuthorizationProvider? _authorizationProvider;
    private readonly IServerChannelManager _channelManager;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    public AuthenticationMiddleware(
        IServerChannelManager channelManager,
        IAuthenticationProvider? authenticationProvider = null,
        IAuthorizationProvider? authorizationProvider = null,
        ILogger<AuthenticationMiddleware>? logger = null)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _authenticationProvider = authenticationProvider;
        _authorizationProvider = authorizationProvider;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthenticationMiddleware>.Instance;
    }

    /// <summary>
    /// 对请求进行认证检查
    /// </summary>
    /// <param name="transport">服务器连接</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="methodInfo">方法信息（用于检查特性）</param>
    /// <returns>是否通过认证</returns>
    public async Task<bool> AuthenticateRequestAsync(IServerTransport transport, string serviceName, string methodName, MethodInfo? methodInfo = null)
    {
        try
        {
            // 获取传输通道
            var channel = _channelManager.GetChannel(((ITransport)transport).Id);
            if (channel == null)
            {
                _logger.LogWarning("找不到连接 {ConnectionId} 的传输通道", ((ITransport)transport).Id);
                return false;
            }

            // 确定认证要求
            var authRequirement = DetermineAuthenticationRequirement(methodInfo, methodName);

            _logger.LogDebug("方法 {ServiceName}.{MethodName} 的认证要求: {Requirement}",
                serviceName, methodName, authRequirement);

            // 如果明确允许匿名访问，直接返回成功
            if (authRequirement == AuthRequirement.AllowAnonymous)
            {
                _logger.LogDebug("方法 {ServiceName}.{MethodName} 允许匿名访问", serviceName, methodName);
                return true;
            }

            // 从通道中提取用户身份
            var user = ExtractUserFromChannel(channel);

            // 如果没有用户身份且通道有认证上下文，尝试认证
            if (user == null && _authenticationProvider != null && channel.AuthenticationContext?.Token != null)
            {
                var authResult = await _authenticationProvider.AuthenticateAsync(channel.AuthenticationContext.Token);
                if (authResult.IsAuthenticated && authResult.User != null)
                {
                    // 创建新的认证上下文
                    var authContext = new AuthenticationContext(((ITransport)transport).Id);
                    var userId = authResult.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var username = authResult.User.FindFirst(ClaimTypes.Name)?.Value;

                    if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username))
                    {
                        authContext.SetClientAuthentication(userId, username, channel.AuthenticationContext.Token, authResult.User);
                    }

                    // 更新通道的认证信息
                    channel.SetAuthentication(authContext);
                    user = authResult.User;

                    _logger.LogDebug("用户 {UserId} 通过token认证成功", userId);
                }
                else
                {
                    _logger.LogWarning("Token认证失败: {Error}", authResult.ErrorMessage);
                    return false;
                }
            }

            // 如果仍然没有用户身份，检查认证要求
            if (user == null)
            {
                if (authRequirement == AuthRequirement.RequireAuthentication)
                {
                    _logger.LogWarning("未认证用户尝试访问需要认证的方法 {ServiceName}.{MethodName}", serviceName, methodName);
                    return false;
                }

                // 默认情况下允许访问
                _logger.LogDebug("未认证用户访问方法 {ServiceName}.{MethodName}，默认允许", serviceName, methodName);
                return true;
            }

            // 执行授权检查
            return await AuthorizeUserAsync(user, serviceName, methodName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "认证过程中发生异常");
            return false;
        }
    }

    /// <summary>
    /// 确定方法的认证要求
    /// </summary>
    /// <param name="methodInfo">方法信息</param>
    /// <param name="methodName">方法名称</param>
    /// <returns>认证要求</returns>
    private AuthRequirement DetermineAuthenticationRequirement(MethodInfo? methodInfo, string methodName)
    {
        _logger.LogDebug("=== 开始确定认证要求 ===");
        _logger.LogDebug("方法名称: {MethodName}", methodName);
        _logger.LogDebug("MethodInfo是否为null: {IsNull}", methodInfo == null);

        if (methodInfo == null)
        {
            // 如果没有方法信息，特殊处理登录方法
            if (methodName == "LoginAsync")
            {
                _logger.LogDebug("登录方法默认允许匿名访问");
                return AuthRequirement.AllowAnonymous;
            }

            // 默认不要求认证（向后兼容）
            _logger.LogDebug("MethodInfo为null，返回NoRequirement");
            return AuthRequirement.NoRequirement;
        }

        _logger.LogDebug("方法全名: {FullName}", methodInfo.ToString());
        _logger.LogDebug("声明类型: {DeclaringType}", methodInfo.DeclaringType?.FullName ?? "null");

        // 检查 [AllowAnonymous] 特性
        var allowAnonymousAttr = methodInfo.GetCustomAttribute<PulseRPC.AllowAnonymousAttribute>();
        _logger.LogDebug("是否有[AllowAnonymous]特性: {HasAttribute}", allowAnonymousAttr != null);
        if (allowAnonymousAttr != null)
        {
            _logger.LogDebug("方法 {MethodName} 标记了 [AllowAnonymous] 特性", methodInfo.Name);
            return AuthRequirement.AllowAnonymous;
        }

        // 检查 [Authorize] 特性
        var authorizeAttr = methodInfo.GetCustomAttribute<PulseRPC.AuthorizeAttribute>();
        _logger.LogDebug("是否有[Authorize]特性: {HasAttribute}", authorizeAttr != null);
        if (authorizeAttr != null)
        {
            _logger.LogDebug("方法 {MethodName} 标记了 [Authorize] 特性", methodInfo.Name);
            return AuthRequirement.RequireAuthentication;
        }

        // 检查类级别的 [Authorize] 特性
        var classAuthorizeAttr = methodInfo.DeclaringType?.GetCustomAttribute<PulseRPC.AuthorizeAttribute>();
        _logger.LogDebug("类是否有[Authorize]特性: {HasAttribute}", classAuthorizeAttr != null);
        if (classAuthorizeAttr != null)
        {
            // 类标记了 [Authorize]，检查方法是否有 [AllowAnonymous] 覆盖
            if (methodInfo.GetCustomAttribute<PulseRPC.AllowAnonymousAttribute>() != null)
            {
                _logger.LogDebug("方法 {MethodName} 继承类的 [Authorize] 特性，但被 [AllowAnonymous] 覆盖", methodInfo.Name);
                return AuthRequirement.AllowAnonymous;
            }

            _logger.LogDebug("方法 {MethodName} 继承类的 [Authorize] 特性", methodInfo.Name);
            return AuthRequirement.RequireAuthentication;
        }

        // 特殊处理登录方法
        if (methodName == "LoginAsync")
        {
            _logger.LogDebug("登录方法默认允许匿名访问");
            return AuthRequirement.AllowAnonymous;
        }

        // 默认不要求认证（向后兼容）
        _logger.LogDebug("没有找到任何认证特性，返回NoRequirement");
        return AuthRequirement.NoRequirement;
    }

    /// <summary>
    /// 从通道中提取用户身份
    /// </summary>
    /// <param name="channel">传输通道</param>
    /// <returns>用户身份，如果未认证则返回null</returns>
    private ClaimsPrincipal? ExtractUserFromChannel(IServerChannel channel)
    {
        // 检查通道是否已认证
        if (channel.AuthenticationContext != null && channel.AuthenticationContext.IsAuthenticated)
        {
            // 如果有Principal，直接返回
            if (channel.AuthenticationContext.Principal != null)
            {
                return channel.AuthenticationContext.Principal;
            }

            // 如果没有Principal但有基本认证信息，构建ClaimsPrincipal
            if (!string.IsNullOrEmpty(channel.AuthenticationContext.Identity) && !string.IsNullOrEmpty(channel.AuthenticationContext.Name))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, channel.AuthenticationContext.Identity),
                    new Claim(ClaimTypes.Name, channel.AuthenticationContext.Name)
                };

                var identity = new ClaimsIdentity(claims, "pulse-rpc");
                var principal = new ClaimsPrincipal(identity);

                return principal;
            }
        }

        return null;
    }

    /// <summary>
    /// 授权用户访问
    /// </summary>
    /// <param name="user">用户身份</param>
    /// <param name="serviceName">服务名称</param>
    /// <param name="methodName">方法名称</param>
    /// <returns>是否授权成功</returns>
    private async Task<bool> AuthorizeUserAsync(ClaimsPrincipal user, string serviceName, string methodName)
    {
        // 如果没有配置授权提供程序，默认允许已认证用户访问
        if (_authorizationProvider == null)
        {
            _logger.LogDebug("未配置授权提供程序，允许已认证用户访问");
            return true;
        }

        var authzResult = await _authorizationProvider.AuthorizeAsync(user, serviceName, methodName);

        if (!authzResult.IsAuthorized)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "未知用户";
            _logger.LogWarning("用户 {UserId} 访问 {ServiceName}.{MethodName} 被拒绝: {Reason}",
                userId, serviceName, methodName, authzResult.ErrorMessage);
        }

        return authzResult.IsAuthorized;
    }

    /// <summary>
    /// 认证要求枚举
    /// </summary>
    private enum AuthRequirement
    {
        /// <summary>无特定要求</summary>
        NoRequirement,
        /// <summary>要求认证</summary>
        RequireAuthentication,
        /// <summary>允许匿名访问</summary>
        AllowAnonymous
    }
}
