using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace GameApp.Unity.Network
{
    /// <summary>
    /// 认证客户端 - 负责与 AuthServer 的 HTTP 通信
    /// </summary>
    public class AuthClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AuthClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"GameApp-Unity/{Application.version}");
        }

        /// <summary>
        /// 用户登录
        /// </summary>
        public async Task<AuthResult<LoginResponse>> LoginAsync(string username, string password, string deviceId)
        {
            try
            {
                var request = new LoginRequest
                {
                    Username = username,
                    Password = password,
                    DeviceId = deviceId,
                    DeviceInfo = new ClientDeviceInfo
                    {
                        Platform = Application.platform.ToString(),
                        Version = Application.version,
                        UnityVersion = Application.unityVersion
                    }
                };

                var response = await PostAsync<LoginResponse>("/api/auth/login", request);
                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Login failed: {ex.Message}");
                return new AuthResult<LoginResponse>
                {
                    Success = false,
                    Message = $"登录失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 用户注册
        /// </summary>
        public async Task<AuthResult<RegisterResponse>> RegisterAsync(RegisterRequest request)
        {
            try
            {
                var response = await PostAsync<RegisterResponse>("/api/auth/register", request);
                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Registration failed: {ex.Message}");
                return new AuthResult<RegisterResponse>
                {
                    Success = false,
                    Message = $"注册失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取区服列表
        /// </summary>
        public async Task<AuthResult<ZoneListResponse>> GetZoneListAsync()
        {
            try
            {
                var response = await GetAsync<ZoneListResponse>("/api/zone/list");
                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get zone list failed: {ex.Message}");
                return new AuthResult<ZoneListResponse>
                {
                    Success = false,
                    Message = $"获取区服列表失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 选择区服
        /// </summary>
        public async Task<AuthResult<ZoneSelectionResponse>> SelectZoneAsync(string zoneId, string accessToken)
        {
            try
            {
                SetAuthHeader(accessToken);

                var request = new ZoneSelectionRequest { ZoneId = zoneId };
                var response = await PostAsync<ZoneSelectionResponse>("/api/zone/select", request);
                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Zone selection failed: {ex.Message}");
                return new AuthResult<ZoneSelectionResponse>
                {
                    Success = false,
                    Message = $"区服选择失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 验证Token
        /// </summary>
        public async Task<AuthResult<TokenValidationResponse>> ValidateTokenAsync(string accessToken)
        {
            try
            {
                SetAuthHeader(accessToken);
                var response = await GetAsync<TokenValidationResponse>("/api/auth/validate-token");
                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Token validation failed: {ex.Message}");
                return new AuthResult<TokenValidationResponse>
                {
                    Success = false,
                    Message = $"Token验证失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 刷新Token
        /// </summary>
        public async Task<AuthResult<RefreshTokenResponse>> RefreshTokenAsync(string refreshToken)
        {
            try
            {
                var request = new RefreshTokenRequest { RefreshToken = refreshToken };
                var response = await PostAsync<RefreshTokenResponse>("/api/auth/refresh-token", request);
                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Token refresh failed: {ex.Message}");
                return new AuthResult<RefreshTokenResponse>
                {
                    Success = false,
                    Message = $"Token刷新失败: {ex.Message}"
                };
            }
        }

        #region Private Methods

        private async Task<AuthResult<T>> GetAsync<T>(string endpoint) where T : class
        {
            var url = _baseUrl + endpoint;
            var response = await _httpClient.GetAsync(url);
            return await ProcessResponse<T>(response);
        }

        private async Task<AuthResult<T>> PostAsync<T>(string endpoint, object data) where T : class
        {
            var url = _baseUrl + endpoint;
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            return await ProcessResponse<T>(response);
        }

        private async Task<AuthResult<T>> ProcessResponse<T>(HttpResponseMessage response) where T : class
        {
            var responseBody = await response.Content.ReadAsStringAsync();

            try
            {
                var apiResponse = JsonConvert.DeserializeObject<ApiResponse<T>>(responseBody);

                if (apiResponse == null)
                {
                    return new AuthResult<T>
                    {
                        Success = false,
                        Message = "服务器响应格式错误"
                    };
                }

                return new AuthResult<T>
                {
                    Success = apiResponse.Success,
                    Message = apiResponse.Message,
                    Data = apiResponse.Data,
                    Errors = apiResponse.Errors,
                    RequestId = apiResponse.RequestId
                };
            }
            catch (JsonException ex)
            {
                Debug.LogError($"JSON parsing failed: {ex.Message}");
                Debug.LogError($"Response body: {responseBody}");

                return new AuthResult<T>
                {
                    Success = false,
                    Message = "服务器响应解析失败",
                    Errors = new[] { ex.Message }
                };
            }
        }

        private void SetAuthHeader(string accessToken)
        {
            if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            {
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            }
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    #region Data Models

    [Serializable]
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public ClientDeviceInfo DeviceInfo { get; set; } = new();
    }

    [Serializable]
    public class ClientDeviceInfo
    {
        public string Platform { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string UnityVersion { get; set; } = string.Empty;
    }

    [Serializable]
    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string InviteCode { get; set; } = string.Empty;
        public bool AgreementAccepted { get; set; }
    }

    [Serializable]
    public class ZoneSelectionRequest
    {
        public string ZoneId { get; set; } = string.Empty;
    }

    [Serializable]
    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    [Serializable]
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string TokenType { get; set; } = string.Empty;
        public UserInfo User { get; set; } = new();
        public string GameTicket { get; set; } = string.Empty;
        public int GameTicketExpiresIn { get; set; }
    }

    [Serializable]
    public class RegisterResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public UserInfo User { get; set; } = new();
    }

    [Serializable]
    public class ZoneListResponse
    {
        public ZoneInfo[] Zones { get; set; } = Array.Empty<ZoneInfo>();
    }

    [Serializable]
    public class ZoneSelectionResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public string GameTicket { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public ServerEndpoint[] GameServers { get; set; } = Array.Empty<ServerEndpoint>();
        public ServerEndpoint[] BattleServers { get; set; } = Array.Empty<ServerEndpoint>();
    }

    [Serializable]
    public class TokenValidationResponse
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public UserClaims UserClaims { get; set; } = new();
    }

    [Serializable]
    public class RefreshTokenResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string TokenType { get; set; } = string.Empty;
    }

    [Serializable]
    public class UserInfo
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserProfile Profile { get; set; } = new();
    }

    [Serializable]
    public class UserProfile
    {
        public string Nickname { get; set; } = string.Empty;
        public string Avatar { get; set; } = string.Empty;
        public int Level { get; set; }
        public int VipLevel { get; set; }
        public string Language { get; set; } = string.Empty;
    }

    [Serializable]
    public class ZoneInfo
    {
        public string ZoneId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int MaxPlayers { get; set; }
        public int CurrentPlayers { get; set; }
        public string LoadLevel { get; set; } = string.Empty;
        public bool Recommendation { get; set; }
        public bool NewPlayerAllowed { get; set; }
        public string Description { get; set; } = string.Empty;
        public string[] Features { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    public class ServerEndpoint
    {
        public string ServerId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    [Serializable]
    public class UserClaims
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string[] Permissions { get; set; } = Array.Empty<string>();
    }

    [Serializable]
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; } = default!;
        public string[] Errors { get; set; } = Array.Empty<string>();
        public string RequestId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    [Serializable]
    public class AuthResult<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T Data { get; set; } = default!;
        public string[] Errors { get; set; } = Array.Empty<string>();
        public string RequestId { get; set; } = string.Empty;
    }

    #endregion
}
