using System;
using System.Threading.Tasks;
using PulseRPC;
using MemoryPack;

namespace JwtAuthApp.Shared;

public interface IAccountService : IPulseService<IAccountService>
{
    Task<PulseResult<SignInResponse>> SignInAsync(string signInId, string password);
    Task<PulseResult<CurrentUserResponse>> GetCurrentUserNameAsync();
    Task<PulseResult<string>> DangerousOperationAsync();
}

[MemoryPackable]
public partial class SignInResponse
{
    public long UserId { get; set; } = 0L;
    public string Name { get; set; }
    public string Token { get; set; }
    public DateTimeOffset Expiration { get; set; }
    public bool Success { get; set; }

    public static SignInResponse Failed { get; } = new(0, string.Empty, string.Empty, DateTimeOffset.Now) { Success = false };

    [MemoryPackConstructor]
    public SignInResponse(long userId, string name, string token, DateTimeOffset expiration)
    {
        Success = true;
        UserId = userId;
        Name = name;
        Token = token;
        Expiration = expiration;
    }
}

[MemoryPackable]
public partial class CurrentUserResponse
{
    public static CurrentUserResponse Anonymous { get; } = new CurrentUserResponse() { IsAuthenticated = false, Name = "Anonymous" };

    public bool IsAuthenticated { get; set; }
    public string Name { get; set; } = string.Empty;
    public long UserId { get; set; }
}
