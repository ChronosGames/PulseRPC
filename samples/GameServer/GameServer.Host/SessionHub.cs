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
    private readonly PlayerSessions _sessions;

    internal SessionHub(IServerChannelManager channels, PlayerSessions sessions)
    {
        _channels = channels;
        _sessions = sessions;
        _validation = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(SigningKey()),
            ValidateIssuer = true, ValidIssuer = "game-sample",
            ValidateAudience = true, ValidAudience = "game-players",
            ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero, ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }

    public async Task<bool> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClaimsPrincipal principal;
        SecurityToken validated;
        try { principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, _validation, out validated); }
        catch (SecurityTokenException) { throw new UnauthorizedAccessException("Invalid or expired player token."); }
        var user = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(user)) throw new UnauthorizedAccessException("Missing player identity.");
        var id = PulseContext.CurrentConnectionId ?? throw new UnauthorizedAccessException("Missing connection.");
        var channel = _channels.GetChannel(id) ?? throw new UnauthorizedAccessException("Connection is closed.");
        var stamp = new PlayerSessions.Stamp(user, Guid.NewGuid());
        await _sessions.ReplaceAsync(stamp, validated.ValidTo, cancellationToken);
        // This authoritative claim is issued by the Gateway, never trusted from the JWT.
        principal = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims.Where(claim => claim.Type != PlayerSessions.Claim)
            .Append(new Claim(PlayerSessions.Claim, stamp.Session.ToString("D"))), "game-session"));
        var context = new AuthenticationContext(id);
        context.SetClientAuthentication(user, user, principal: principal);
        channel.SetAuthentication(context);
        return true;
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _sessions.RevokeAsync(PlayerSessions.Require(PulseContext.Current), cancellationToken);
        return true;
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
