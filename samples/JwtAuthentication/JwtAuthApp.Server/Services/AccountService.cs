using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using JwtAuthApp.Server.Authentication;
using JwtAuthApp.Shared;
using PulseRPC;
using Microsoft.AspNetCore.Authorization;

namespace JwtAuthApp.Server.Services
{
    [Authorize]
    public class AccountService : IPulseService<IAccountService>
    {
        private static IDictionary<string, (string Password, long UserId, string DisplayName)> DummyUsers = new Dictionary<string, (string, long, string)>(StringComparer.OrdinalIgnoreCase)
        {
            {"pecorine@example.com", ("P@ssw0rd1", 1001, "Eustiana von Astraea")},
            {"kyaru@example.com", ("P@ssword2", 1002, "Kiruya Momochi")},
        };

        private readonly JwtTokenService _jwtTokenService;

        public AccountService(JwtTokenService jwtTokenService)
        {
            _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        }

        [AllowAnonymous]
        public PulseResult<SignInResponse> SignInAsync(string signInId, string password)
        {
            if (DummyUsers.TryGetValue(signInId, out var userInfo) && userInfo.Password == password)
            {
                var (token, expires) = _jwtTokenService.CreateToken(userInfo.UserId, userInfo.DisplayName);

                return PulseResult<SignInResponse>.Success(new SignInResponse(
                    userInfo.UserId,
                    userInfo.DisplayName,
                    token,
                    expires
                ));
            }

            return PulseResult<SignInResponse>.Error("Invalid credentials");
        }

        [AllowAnonymous]
        public PulseResult<CurrentUserResponse> GetCurrentUserNameAsync()
        {
            var userPrincipal = Context.User;
            if (userPrincipal.Identity?.IsAuthenticated ?? false)
            {
                if (!int.TryParse(userPrincipal.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value, out var userId))
                {
                    return PulseResult<CurrentUserResponse>.Success(CurrentUserResponse.Anonymous);
                }

                var user = DummyUsers.SingleOrDefault(x => x.Value.UserId == userId).Value;
                return PulseResult<CurrentUserResponse>.Success(new CurrentUserResponse()
                {
                    IsAuthenticated = true,
                    UserId = user.UserId,
                    Name = user.DisplayName,
                });
            }

            return PulseResult<CurrentUserResponse>.Success(CurrentUserResponse.Anonymous);
        }

        [Authorize(Roles = "Administrators")]
        public PulseResult<string> DangerousOperationAsync()
        {
            return PulseResult<string>.Success("rm -rf /");
        }
    }
}
