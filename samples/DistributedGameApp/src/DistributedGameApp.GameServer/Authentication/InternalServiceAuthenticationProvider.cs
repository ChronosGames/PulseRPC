using PulseRPC.Authentication;

namespace DistributedGameApp.GameServer.Authentication;

/// <summary>
/// 内部服务认证提供者 - 用于服务器间通信
/// </summary>
public class InternalServiceAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _serviceId;
    private readonly string _clusterSecret;
    private AuthenticationToken? _cachedToken;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string AuthenticationType => "InternalService";

    public InternalServiceAuthenticationProvider(string serviceId, string clusterSecret)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("ServiceId 不能为空", nameof(serviceId));

        if (string.IsNullOrWhiteSpace(clusterSecret))
            throw new ArgumentException("ClusterSecret 不能为空", nameof(clusterSecret));

        _serviceId = serviceId;
        _clusterSecret = clusterSecret;
    }

    public async Task<AuthenticationToken> GetTokenAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // 如果缓存的 token 仍然有效，直接返回
            if (_cachedToken != null && !_cachedToken.IsExpired && !_cachedToken.IsExpiringSoon())
            {
                return _cachedToken;
            }

            // 创建新的 token
            _cachedToken = CreateToken();
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<AuthenticationToken> RefreshTokenAsync(AuthenticationToken currentToken, CancellationToken cancellationToken = default)
    {
        // 内部服务 token 直接创建新的
        return Task.FromResult(CreateToken());
    }

    public bool IsTokenValid(AuthenticationToken token)
    {
        return token != null && !token.IsExpired;
    }

    public Task RevokeTokenAsync(AuthenticationToken token, CancellationToken cancellationToken = default)
    {
        // 内部服务 token 无需撤销
        return Task.CompletedTask;
    }

    private AuthenticationToken CreateToken()
    {
        // 创建一个简单的认证 token
        // Token 格式: InternalService:{ServiceId}:{ClusterSecret}
        var tokenValue = $"InternalService:{_serviceId}:{_clusterSecret}";

        return new AuthenticationToken
        {
            Token = tokenValue,
            TokenType = "InternalService",
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24), // 内部服务 token 24小时有效
            Claims = new Dictionary<string, string>
            {
                ["service_id"] = _serviceId,
                ["auth_type"] = "internal_service"
            },
            Scopes = new[] { "*" } // 内部服务拥有所有权限
        };
    }
}
