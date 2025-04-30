using System.Security.Claims;
using JwtAuthApp.Server.Authentication;
using JwtAuthApp.Shared;
using PulseRPC;
using Microsoft.AspNetCore.Authorization;

namespace JwtAuthApp.Server.Services
{
    [Authorize]
    public class AccountService : IPulseService<AccountService>
    {
        private static IDictionary<string, (string Password, long UserId, string DisplayName)> DummyUsers = new Dictionary<string, (string, long, string)>(StringComparer.OrdinalIgnoreCase)
        {
            {"pecorine@example.com", ("P@ssw0rd1", 1001, "Eustiana von Astraea")},
            {"kyaru@example.com", ("P@ssword2", 1002, "Kiruya Momochi")},
        };

        private readonly JwtTokenService _jwtTokenService;

        // 使用ThreadLocal存储当前用户
        internal static ThreadLocal<ClaimsPrincipal> CurrentUser = new ThreadLocal<ClaimsPrincipal>(() => new ClaimsPrincipal(new ClaimsIdentity()));

        public AccountService(JwtTokenService jwtTokenService)
        {
            _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        }

        [AllowAnonymous]
        public Task<PulseResult<SignInResponse>> SignInAsync(string signInId, string password)
        {
            if (DummyUsers.TryGetValue(signInId, out var userInfo) && userInfo.Password == password)
            {
                var (token, expires) = _jwtTokenService.CreateToken(userInfo.UserId, userInfo.DisplayName);

                return Task.FromResult(PulseResult<SignInResponse>.Success(new SignInResponse(
                    userInfo.UserId,
                    userInfo.DisplayName,
                    token,
                    expires
                )));
            }

            return Task.FromResult(PulseResult<SignInResponse>.Error("Invalid credentials"));
        }

        [AllowAnonymous]
        public Task<PulseResult<CurrentUserResponse>> GetCurrentUserNameAsync()
        {
            var userPrincipal = CurrentUser.Value;
            if (!(userPrincipal?.Identity?.IsAuthenticated ?? false))
            {
                return Task.FromResult(PulseResult<CurrentUserResponse>.Success(CurrentUserResponse.Anonymous));
            }

            if (!long.TryParse(userPrincipal.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                return Task.FromResult(PulseResult<CurrentUserResponse>.Success(CurrentUserResponse.Anonymous));
            }

            var user = DummyUsers.SingleOrDefault(x => x.Value.UserId == userId).Value;
            return Task.FromResult(PulseResult<CurrentUserResponse>.Success(new CurrentUserResponse()
            {
                IsAuthenticated = true,
                UserId = user.UserId,
                Name = user.DisplayName,
            }));

        }

        [Authorize(Roles = "Administrators")]
        public Task<PulseResult<string>> DangerousOperationAsync()
        {
            return Task.FromResult(PulseResult<string>.Success("rm -rf /"));
        }
    }
}
