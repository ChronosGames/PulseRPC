using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Client;

/// <summary>
/// LoginServer HTTP 客户端 - 用于登录认证和获取服务器列表
/// </summary>
/// <remarks>
/// 此类负责与 LoginServer 的 HTTP REST API 交互，包括：
/// - 用户注册和登录
/// - 获取 JWT Token
/// - 获取可用的 GameServer 列表
/// - 令牌刷新
/// </remarks>
public class LoginServerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LoginServerClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _accessToken;
    private string? _refreshToken;
    private bool _disposed;

    /// <summary>
    /// 初始化 LoginServerClient
    /// </summary>
    /// <param name="baseAddress">LoginServer 的基础地址，例如 "http://localhost:5000"</param>
    /// <param name="logger">日志记录器</param>
    public LoginServerClient(string baseAddress, ILogger<LoginServerClient> logger)
        : this(new HttpClient { BaseAddress = new Uri(baseAddress) }, logger, true)
    {
    }

    /// <summary>
    /// 初始化 LoginServerClient
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="disposeHttpClient">是否在 Dispose 时释放 HttpClient</param>
    public LoginServerClient(HttpClient httpClient, ILogger<LoginServerClient> logger, bool disposeHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _disposed = false;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DistributedGameClient/1.0");
    }

    /// <summary>
    /// 当前访问令牌
    /// </summary>
    public string? AccessToken => _accessToken;

    /// <summary>
    /// 当前刷新令牌
    /// </summary>
    public string? RefreshToken => _refreshToken;

    /// <summary>
    /// 是否已认证
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    /// <summary>
    /// 用户注册
    /// </summary>
    /// <param name="username">用户名（至少3个字符）</param>
    /// <param name="password">密码（至少6个字符）</param>
    /// <param name="email">邮箱地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>认证响应</returns>
    public async Task<AuthResponse> RegisterAsync(
        string username,
        string password,
        string email,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("正在注册用户: {Username}", username);

        var request = new RegisterRequest
        {
            Username = username,
            Password = password,
            Email = email
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/auth/register",
                request,
                _jsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("注册失败: {StatusCode}, {Content}", response.StatusCode, errorContent);
                throw new LoginServerException($"注册失败: {response.StatusCode}", response.StatusCode);
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions, cancellationToken)
                ?? throw new LoginServerException("注册响应为空");

            // 保存令牌
            _accessToken = authResponse.AccessToken;
            _refreshToken = authResponse.RefreshToken;

            // 设置认证头
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("注册成功: {UserId}", authResponse.UserId);
            return authResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "注册请求失败");
            throw new LoginServerException("注册请求失败，请检查网络连接", ex);
        }
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="usernameOrEmail">用户名或邮箱</param>
    /// <param name="password">密码</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>认证响应</returns>
    public async Task<AuthResponse> LoginAsync(
        string usernameOrEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("正在登录: {UsernameOrEmail}", usernameOrEmail);

        var request = new LoginRequest
        {
            UsernameOrEmail = usernameOrEmail,
            Password = password
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/auth/login",
                request,
                _jsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("登录失败: {StatusCode}, {Content}", response.StatusCode, errorContent);
                throw new LoginServerException($"登录失败: {response.StatusCode}", response.StatusCode);
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions, cancellationToken)
                ?? throw new LoginServerException("登录响应为空");

            // 保存令牌
            _accessToken = authResponse.AccessToken;
            _refreshToken = authResponse.RefreshToken;

            // 设置认证头
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("登录成功: {UserId}", authResponse.UserId);
            return authResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "登录请求失败");
            throw new LoginServerException("登录请求失败，请检查网络连接", ex);
        }
    }

    /// <summary>
    /// 刷新访问令牌
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新的认证响应</returns>
    public async Task<AuthResponse> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_refreshToken))
        {
            throw new InvalidOperationException("没有可用的刷新令牌");
        }

        _logger.LogInformation("正在刷新访问令牌");

        var request = new RefreshTokenRequest
        {
            RefreshToken = _refreshToken
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/auth/refresh",
                request,
                _jsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("令牌刷新失败: {StatusCode}, {Content}", response.StatusCode, errorContent);
                throw new LoginServerException($"令牌刷新失败: {response.StatusCode}", response.StatusCode);
            }

            var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions, cancellationToken)
                ?? throw new LoginServerException("令牌刷新响应为空");

            // 更新令牌
            _accessToken = authResponse.AccessToken;
            _refreshToken = authResponse.RefreshToken;

            // 更新认证头
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            _logger.LogInformation("令牌刷新成功");
            return authResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "令牌刷新请求失败");
            throw new LoginServerException("令牌刷新请求失败，请检查网络连接", ex);
        }
    }

    /// <summary>
    /// 获取游戏服务器列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>游戏服务器列表</returns>
    public async Task<List<ServerInfo>> GetGameServersAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        _logger.LogInformation("正在获取游戏服务器列表");

        try
        {
            var response = await _httpClient.GetAsync("api/server/game-servers", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("获取服务器列表失败: {StatusCode}, {Content}", response.StatusCode, errorContent);
                throw new LoginServerException($"获取服务器列表失败: {response.StatusCode}", response.StatusCode);
            }

            var servers = await response.Content.ReadFromJsonAsync<List<ServerInfo>>(_jsonOptions, cancellationToken)
                ?? new List<ServerInfo>();

            _logger.LogInformation("成功获取 {Count} 个游戏服务器", servers.Count);
            return servers;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取服务器列表请求失败");
            throw new LoginServerException("获取服务器列表请求失败，请检查网络连接", ex);
        }
    }

    /// <summary>
    /// 获取战斗服务器列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>战斗服务器列表</returns>
    public async Task<List<ServerInfo>> GetBattleServersAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        _logger.LogInformation("正在获取战斗服务器列表");

        try
        {
            var response = await _httpClient.GetAsync("api/server/battle-servers", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("获取战斗服务器列表失败: {StatusCode}, {Content}", response.StatusCode, errorContent);
                throw new LoginServerException($"获取战斗服务器列表失败: {response.StatusCode}", response.StatusCode);
            }

            var servers = await response.Content.ReadFromJsonAsync<List<ServerInfo>>(_jsonOptions, cancellationToken)
                ?? new List<ServerInfo>();

            _logger.LogInformation("成功获取 {Count} 个战斗服务器", servers.Count);
            return servers;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取战斗服务器列表请求失败");
            throw new LoginServerException("获取战斗服务器列表请求失败，请检查网络连接", ex);
        }
    }

    /// <summary>
    /// 获取推荐的游戏服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>推荐的游戏服务器</returns>
    public async Task<ServerInfo> GetRecommendedGameServerAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();

        _logger.LogInformation("正在获取推荐的游戏服务器");

        try
        {
            var response = await _httpClient.GetAsync("api/server/recommend/game-server", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("获取推荐服务器失败: {StatusCode}, {Content}", response.StatusCode, errorContent);
                throw new LoginServerException($"获取推荐服务器失败: {response.StatusCode}", response.StatusCode);
            }

            var server = await response.Content.ReadFromJsonAsync<ServerInfo>(_jsonOptions, cancellationToken)
                ?? throw new LoginServerException("推荐服务器响应为空");

            _logger.LogInformation("成功获取推荐服务器: {ServerName}", server.ServerName);
            return server;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取推荐服务器请求失败");
            throw new LoginServerException("获取推荐服务器请求失败，请检查网络连接", ex);
        }
    }

    /// <summary>
    /// 确保已认证
    /// </summary>
    private void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("未认证，请先登录");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _httpClient?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

#region DTO Classes

/// <summary>
/// 注册请求
/// </summary>
public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// 登录请求
/// </summary>
public class LoginRequest
{
    public string UsernameOrEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 刷新令牌请求
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// 认证响应
/// </summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// 服务器信息
/// </summary>
public class ServerInfo
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int TcpPort { get; set; }
    public int? KcpPort { get; set; }
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string Status { get; set; } = string.Empty;
    public int LoadPercentage { get; set; }
}

#endregion

/// <summary>
/// LoginServer 异常
/// </summary>
public class LoginServerException : Exception
{
    public System.Net.HttpStatusCode? StatusCode { get; }

    public LoginServerException(string message)
        : base(message)
    {
    }

    public LoginServerException(string message, System.Net.HttpStatusCode statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public LoginServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
