using System;
using PulseRPC;
using MemoryPack;

namespace JwtAuthApp.Shared;

public interface IAccountService : IPulseService<IAccountService>
{
    PulseResult<SignInResponse> SignInAsync(string signInId, string password);
    PulseResult<CurrentUserResponse> GetCurrentUserNameAsync();
    PulseResult<string> DangerousOperationAsync();
}

[MemoryPackable]
public partial class SignInResponse
{
    public long UserId { get; set; }
    public string Name { get; set; }
    public string Token { get; set; }
    public DateTimeOffset Expiration { get; set; }
    public bool Success { get; set; }

    public static SignInResponse Failed { get; } = new SignInResponse() { Success = false };

    public SignInResponse() { }

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
    public string Name { get; set; }
    public long UserId { get; set; }
}
