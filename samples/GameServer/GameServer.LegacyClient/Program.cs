using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using GameServer.LegacyContracts;
using Microsoft.IdentityModel.Tokens;
using PulseRPC;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;

namespace GameServer.LegacyClient
{
    [PulseClientGeneration(typeof(IPlayerHub))]
    [PulseClientGeneration(typeof(ISessionHub))]
    internal static class Program
    {
        private static async Task Main(string[] args)
        {
            var port = int.Parse(args[0]);
            var player = args[1];
            var key = Convert.FromBase64String(Environment.GetEnvironmentVariable("GAME_JWT_KEY")
                ?? throw new InvalidOperationException("Missing test signing key."));
            var token = new JwtSecurityToken("game-sample", "game-players", new[] { new Claim("sub", player) },
                DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
                new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
            using var client = new PulseClientBuilder().Build();
            await client.InitializeAsync();
            var channel = await client.ConnectToServerAsync("127.0.0.1", port);
            try
            {
                await channel.GetHub<ISessionHub>().AuthenticateAsync(new JwtSecurityTokenHandler().WriteToken(token));
                var state = await channel.ForGatewayActor<IPlayerHub>(player).GetHub<IPlayerHub>().ReadStateV1Async();
                Console.WriteLine("RESULT " + JsonSerializer.Serialize(new { state.Balance, state.Inventory, Contract = "V1", Language = "C# 9" }));
            }
            finally { await channel.DisconnectAsync(); await client.StopAsync(); }
        }
    }
}
