using System.Security.Claims;
using JwtAuthApp.Shared;
using PulseRPC.Server.Contexts;

namespace JwtAuthApp.Server.Services;

/// <summary>
/// 问候 Hub 实现 - 需要连接已认证（任意用户）才能调用。
/// </summary>
public class GreeterHub : IGreeterHub
{
    public Task<string> HelloAsync()
    {
        var context = PulseContext.Current;
        if (context?.UserId is null)
        {
            throw new UnauthorizedAccessException("HelloAsync requires an authenticated connection. Call IAccountHub.SignInAsync first.");
        }

        var displayName = context.User?.FindFirst(ClaimTypes.Name)?.Value ?? context.UserId;
        return Task.FromResult($"Hello {displayName} (UserId: {context.UserId})!");
    }
}
