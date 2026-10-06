using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GameServer.Contracts;
using Microsoft.IdentityModel.Tokens;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Security;
using PulseRPC.Server.Transport;

namespace GameServer.Host;

public sealed class SessionHub : ISessionHub
{
    private readonly IServerChannelManager _channels;
    private readonly TokenValidationParameters _validation;

    public SessionHub(IServerChannelManager channels)
    {
        _channels = channels;
        _validation = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(SigningKey()),
            ValidateIssuer = true, ValidIssuer = "game-sample",
            ValidateAudience = true, ValidAudience = "game-players",
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero, ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }

    public Task<bool> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClaimsPrincipal principal;
        try { principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, _validation, out _); }
        catch (SecurityTokenException) { throw new UnauthorizedAccessException("Invalid or expired player token."); }
        var user = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(user)) throw new UnauthorizedAccessException("Missing player identity.");
        var id = PulseContext.CurrentConnectionId ?? throw new UnauthorizedAccessException("Missing connection.");
        var channel = _channels.GetChannel(id) ?? throw new UnauthorizedAccessException("Connection is closed.");
        var context = new AuthenticationContext(id);
        context.SetClientAuthentication(user, user, principal: principal);
        channel.SetAuthentication(context);
        return Task.FromResult(true);
    }

    // The acceptance client acts as the test identity provider. Production signing keys
    // belong to an identity service; only the verifier is deployed at the Gateway.
    internal static string IssueTestToken(string player, DateTime? expiry = null)
    {
        var token = new JwtSecurityToken("game-sample", "game-players", [new Claim("sub", player)],
            DateTime.UtcNow.AddMinutes(-1), expiry ?? DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(SigningKey()), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static byte[] SigningKey()
    {
        var key = Convert.FromBase64String(Environment.GetEnvironmentVariable("GAME_JWT_KEY")
            ?? throw new InvalidOperationException("GAME_JWT_KEY must be supplied by the environment."));
        if (key.Length < 32) throw new InvalidOperationException("GAME_JWT_KEY requires at least 256 bits.");
        return key;
    }
}
