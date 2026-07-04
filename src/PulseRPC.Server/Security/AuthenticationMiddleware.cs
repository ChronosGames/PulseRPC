using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using PulseRPC.Authentication;
using PulseRPC.Shared;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Security;

/// <summary>
/// 认证中间件，提供基于特性的自动认证功能
/// </summary>
public class AuthenticationMiddleware
{
    private readonly IAuthenticationValidator? _authenticationValidator;
    private readonly IServerChannelManager _channelManager;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    public AuthenticationMiddleware(
        IServerChannelManager channelManager,
        IAuthenticationValidator? authenticationValidator = null,
        ILogger<AuthenticationMiddleware>? logger = null)
    {
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _authenticationValidator = authenticationValidator;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthenticationMiddleware>.Instance;
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
