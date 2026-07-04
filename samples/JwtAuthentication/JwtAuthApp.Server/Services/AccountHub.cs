using System.Security.Claims;
using JwtAuthApp.Server.Authentication;
using JwtAuthApp.Shared;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;

namespace JwtAuthApp.Server.Services;

/// <summary>
/// 账户 Hub 实现 - 演示统一 IPulseHub 架构下的连接级 JWT 认证。
/// </summary>
public class AccountHub : IAccountHub
{
    private static readonly IReadOnlyDictionary<string, (string Password, long UserId, string DisplayName, string[] Roles)> DummyUsers =
        new Dictionary<string, (string, long, string, string[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["pecorine@example.com"] = ("P@ssw0rd1", 1001, "Eustiana von Astraea", new[] { "User" }),
            ["kyaru@example.com"] = ("P@ssword2", 1002, "Kiruya Momochi", new[] { "User", "Administrators" }),
        };

    private readonly JwtTokenService _jwtTokenService;
    private readonly IServerChannelManager _channelManager;
    private readonly ILogger<AccountHub> _logger;

    public AccountHub(JwtTokenService jwtTokenService, IServerChannelManager channelManager, ILogger<AccountHub> logger)
    {
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _channelManager = channelManager ?? throw new ArgumentNullException(nameof(channelManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SignInResponse> SignInAsync(string signInId, string password)
    {
        if (!DummyUsers.TryGetValue(signInId, out var user) || user.Password != password)
        {
            return Task.FromResult(SignInResponse.Failed("Invalid credentials"));
        }

        var (token, expires) = _jwtTokenService.CreateToken(user.UserId, user.DisplayName, user.Roles);
        AuthenticateCurrentConnection(user.UserId, user.DisplayName, token, user.Roles);

        _logger.LogInformation("User {UserId} ({DisplayName}) signed in", user.UserId, user.DisplayName);

        return Task.FromResult(new SignInResponse(user.UserId, user.DisplayName, token, expires));
    }

    public Task<bool> AuthenticateAsync(string token)
    {
        var principal = _jwtTokenService.ValidateToken(token);
        if (principal?.Identity is null || !long.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            return Task.FromResult(false);
        }

        var displayName = principal.FindFirst(ClaimTypes.Name)?.Value ?? userId.ToString();
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        AuthenticateCurrentConnection(userId, displayName, token, roles);

        _logger.LogInformation("Connection re-authenticated via token for user {UserId}", userId);

        return Task.FromResult(true);
    }

    public Task<CurrentUserResponse> GetCurrentUserNameAsync()
    {
        var context = PulseContext.Current;
        if (context?.UserId is null)
        {
            return Task.FromResult(CurrentUserResponse.Anonymous);
        }

        return Task.FromResult(new CurrentUserResponse
        {
            IsAuthenticated = true,
            UserId = long.Parse(context.UserId),
            Name = context.User?.FindFirst(ClaimTypes.Name)?.Value ?? context.UserId,
            Roles = context.Roles.ToArray(),
        });
    }

    public Task<string> DangerousOperationAsync()
    {
        if (PulseContext.Current?.HasRole("Administrators") != true)
        {
            throw new UnauthorizedAccessException("This operation requires the 'Administrators' role.");
        }

        return Task.FromResult("rm -rf / (just kidding — this is a demo)");
    }

    /// <summary>
    /// 把身份信息写入当前连接的 <see cref="IServerChannel.AuthenticationContext"/>，使后续同一连接上的所有
    /// 调用都自动携带该身份（<c>PulseContext.Current.UserId/Roles</c>），无需每次请求单独传递 Token。
    /// </summary>
    private void AuthenticateCurrentConnection(long userId, string displayName, string token, IReadOnlyCollection<string> roles)
    {
        var connectionId = PulseContext.CurrentConnectionId;
        if (string.IsNullOrEmpty(connectionId))
        {
            throw new InvalidOperationException("No active connection to authenticate.");
        }

        var channel = _channelManager.GetChannel(connectionId)
            ?? throw new InvalidOperationException($"Connection '{connectionId}' not found.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, displayName),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt"));

        var authContext = new AuthenticationContext(connectionId);
        authContext.SetClientAuthentication(userId.ToString(), displayName, token, principal);

        channel.SetAuthentication(authContext);
    }
}
