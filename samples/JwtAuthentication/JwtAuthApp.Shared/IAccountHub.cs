using System;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC;

namespace JwtAuthApp.Shared;

/// <summary>
/// 账户服务 - 演示统一 IPulseHub 架构下的 JWT 连接级认证。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SignInAsync"/> 校验凭据后，会在服务端为当前连接签发一个真实的 JWT（<c>System.IdentityModel.Tokens.Jwt</c>），
/// 并立即通过 <c>IServerChannel.SetAuthentication</c> 把身份/角色写入该连接的 <c>AuthenticationContext</c>——
/// 此后同一连接上的所有调用都会自动携带认证信息（<c>PulseContext.Current.UserId/Roles</c>），无需每次请求单独传递 Token。
/// </para>
/// <para>
/// <see cref="AuthenticateAsync"/> 展示了"使用之前签发的 Token 重新认证一个连接"的场景（例如客户端重连后，
/// 不想再次输入密码，而是用本地缓存的 Token 恢复登录态）。
/// </para>
/// <para>
/// <c>[Authorize]</c>/<c>[AllowAnonymous]</c> 目前仅由源生成器捕获用于文档/诊断目的，运行时的实际鉴权由各方法
/// 实现内部读取 <c>PulseContext.Current</c> 完成（见 <c>AccountHub</c> 实现）。
/// </para>
/// </remarks>
public interface IAccountHub : IPulseHub
{
    /// <summary>
    /// 使用账号密码登录，成功后当前连接被标记为已认证，并返回签发的 JWT。
    /// </summary>
    [AllowAnonymous]
    Task<SignInResponse> SignInAsync(string signInId, string password);

    /// <summary>
    /// 使用此前签发的 JWT 重新认证当前连接（例如断线重连后跳过重新输入密码）。
    /// </summary>
    [AllowAnonymous]
    Task<bool> AuthenticateAsync(string token);

    /// <summary>
    /// 获取当前连接的认证状态；未认证时返回 <see cref="CurrentUserResponse.Anonymous"/>。
    /// </summary>
    [AllowAnonymous]
    Task<CurrentUserResponse> GetCurrentUserNameAsync();

    /// <summary>
    /// 需要 Administrators 角色才能调用的危险操作，用于演示基于角色的访问控制。
    /// </summary>
    [Authorize(Role = "Administrators")]
    Task<string> DangerousOperationAsync();
}

/// <summary>
/// 登录结果。
/// </summary>
[MemoryPackable]
public partial class SignInResponse
{
    public long UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset Expiration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static SignInResponse Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
    };

    [MemoryPackConstructor]
    public SignInResponse(long userId, string name, string token, DateTimeOffset expiration)
    {
        Success = true;
        UserId = userId;
        Name = name;
        Token = token;
        Expiration = expiration;
    }

    public SignInResponse()
    {
    }
}

/// <summary>
/// 当前用户信息。
/// </summary>
[MemoryPackable]
public partial class CurrentUserResponse
{
    public static CurrentUserResponse Anonymous { get; } = new() { IsAuthenticated = false, Name = "Anonymous" };

    public bool IsAuthenticated { get; set; }
    public string Name { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
