using System.Security.Claims;
using JwtAuthApp.Shared;
using PulseRPC;
using Microsoft.AspNetCore.Authorization;

namespace JwtAuthApp.Server.Services
{
    [Authorize]
    public class GreeterService : IPulseService<GreeterService>
    {
        public PulseResult<string> HelloAsync()
        {
            throw new NotImplementedException();
            //var userPrincipal = Context.User;
            //return PulseResult<string>.Success($"Hello {userPrincipal.Identity?.Name} (UserId:{userPrincipal.Claims.First(x => x.Type == ClaimTypes.NameIdentifier).Value})!");
        }

        public async Task<PulseResult<string>> ServerAsync(string name, int age)
        {
            var messages = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                messages.Add($"{name} ({age}) @ {DateTime.Now}");
            }
            return PulseResult<string>.Success(string.Join("\n", messages));
        }

        public async Task<PulseResult<string>> ClientAsync(int[] items)
        {
            await Task.Delay(100); // Simulate some processing
            return PulseResult<string>.Success($"Received : {string.Join(",", items)} @ {DateTime.Now}");
        }

        public async Task<PulseResult<string>> DuplexAsync(int[] items)
        {
            var responses = new List<string>();
            foreach (var item in items)
            {
                await Task.Delay(100); // Simulate some processing
                responses.Add($"Hello from Server @ {item}");
            }
            return PulseResult<string>.Success(string.Join("\n", responses));
        }
    }
}
