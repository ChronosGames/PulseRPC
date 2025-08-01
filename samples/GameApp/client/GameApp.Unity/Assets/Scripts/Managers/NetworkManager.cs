using System;
using System.Threading.Tasks;
using UnityEngine;
using GameApp.Unity.Network;

namespace GameApp.Unity.Managers
{
    /// <summary>
    /// 网络管理器 - 统一管理所有网络连接
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        [Header("服务器配置")]
        [SerializeField] private string authServerUrl = "http://localhost:8080";
        [SerializeField] private string gameServerAddress = "localhost";
        [SerializeField] private int gameServerTcpPort = 7000;
        [SerializeField] private int gameServerKcpPort = 7001;

        [Header("连接状态")]
        [SerializeField] private bool isAuthConnected = false;
        [SerializeField] private bool isGameConnected = false;

        // 单例
        public static NetworkManager Instance { get; private set; }

        // 客户端实例
        public AuthClient AuthClient { get; private set; }
        public GameClient GameClient { get; private set; }

        // 连接状态事件
        public event Action OnAuthConnected;
        public event Action<string> OnAuthConnectionFailed;
        public event Action OnGameConnected;
        public event Action<string> OnGameConnectionFailed;
        public event Action<string> OnGameDisconnected;

        // 当前会话信息
        public string AccessToken { get; private set; } = string.Empty;
        public string RefreshToken { get; private set; } = string.Empty;
        public string GameTicket { get; private set; } = string.Empty;
        public UserInfo CurrentUser { get; private set; }

        private void Awake()
        {
            // 单例模式
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeClients();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void InitializeClients()
        {
            try
            {
                // 初始化认证客户端
                AuthClient = new AuthClient(authServerUrl);
                isAuthConnected = true;

                Debug.Log($"AuthClient initialized: {authServerUrl}");
                OnAuthConnected?.Invoke();

                // 初始化游戏客户端
                GameClient = new GameClient();
                GameClient.OnConnected += () =>
                {
                    isGameConnected = true;
                    OnGameConnected?.Invoke();
                };
                GameClient.OnDisconnected += (reason) =>
                {
                    isGameConnected = false;
                    OnGameDisconnected?.Invoke(reason);
                };
                GameClient.OnConnectionError += (error) =>
                {
                    OnGameConnectionFailed?.Invoke(error);
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize clients: {ex.Message}");
                OnAuthConnectionFailed?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// 用户登录流程
        /// </summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                Debug.Log($"Starting login process for user: {username}");

                // 1. 认证服务器登录
                var deviceId = SystemInfo.deviceUniqueIdentifier;
                var loginResult = await AuthClient.LoginAsync(username, password, deviceId);

                if (!loginResult.Success)
                {
                    Debug.LogError($"Auth login failed: {loginResult.Message}");
                    return false;
                }

                // 2. 保存认证信息
                AccessToken = loginResult.Data.AccessToken;
                RefreshToken = loginResult.Data.RefreshToken;
                GameTicket = loginResult.Data.GameTicket;
                CurrentUser = loginResult.Data.User;

                Debug.Log($"Auth login successful for user: {CurrentUser.Username}");

                // 3. 连接游戏服务器
                var gameConnected = await GameClient.InitializeAsync(
                    gameServerAddress,
                    gameServerTcpPort,
                    gameServerKcpPort);

                if (!gameConnected)
                {
                    Debug.LogError("Failed to connect to game server");
                    return false;
                }

                // 4. 游戏服务器登录
                var gameLoginResult = await GameClient.LoginAsync(GameTicket);
                if (!gameLoginResult.Success)
                {
                    Debug.LogError($"Game server login failed: {gameLoginResult.Message}");
                    await GameClient.DisconnectAsync();
                    return false;
                }

                Debug.Log("Complete login process successful");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Login process failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 用户注册
        /// </summary>
        public async Task<bool> RegisterAsync(string username, string email, string password, string confirmPassword)
        {
            try
            {
                Debug.Log($"Starting registration process for user: {username}");

                var request = new RegisterRequest
                {
                    Username = username,
                    Email = email,
                    Password = password,
                    ConfirmPassword = confirmPassword,
                    AgreementAccepted = true
                };

                var result = await AuthClient.RegisterAsync(request);

                if (result.Success)
                {
                    Debug.Log($"Registration successful for user: {username}");
                    return true;
                }
                else
                {
                    Debug.LogError($"Registration failed: {result.Message}");
                    if (result.Errors != null && result.Errors.Length > 0)
                    {
                        foreach (var error in result.Errors)
                        {
                            Debug.LogError($"Registration error: {error}");
                        }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Registration process failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取区服列表
        /// </summary>
        public async Task<ZoneInfo[]> GetZoneListAsync()
        {
            try
            {
                var result = await AuthClient.GetZoneListAsync();
                if (result.Success && result.Data != null)
                {
                    Debug.Log($"Retrieved {result.Data.Zones.Length} zones");
                    return result.Data.Zones;
                }
                else
                {
                    Debug.LogError($"Failed to get zone list: {result.Message}");
                    return Array.Empty<ZoneInfo>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get zone list failed: {ex.Message}");
                return Array.Empty<ZoneInfo>();
            }
        }

        /// <summary>
        /// 选择区服
        /// </summary>
        public async Task<bool> SelectZoneAsync(string zoneId)
        {
            try
            {
                Debug.Log($"Selecting zone: {zoneId}");

                var result = await AuthClient.SelectZoneAsync(zoneId, AccessToken);
                if (result.Success && result.Data != null)
                {
                    // 更新游戏票据
                    GameTicket = result.Data.GameTicket;
                    Debug.Log($"Zone selected successfully: {zoneId}");
                    return true;
                }
                else
                {
                    Debug.LogError($"Zone selection failed: {result.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Zone selection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 刷新Token
        /// </summary>
        public async Task<bool> RefreshTokenAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(RefreshToken))
                {
                    Debug.LogError("No refresh token available");
                    return false;
                }

                var result = await AuthClient.RefreshTokenAsync(RefreshToken);
                if (result.Success && result.Data != null)
                {
                    AccessToken = result.Data.AccessToken;
                    Debug.Log("Token refreshed successfully");
                    return true;
                }
                else
                {
                    Debug.LogError($"Token refresh failed: {result.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Token refresh failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 用户登出
        /// </summary>
        public async Task LogoutAsync()
        {
            try
            {
                Debug.Log("Starting logout process");

                // 1. 游戏服务器登出
                if (isGameConnected && CurrentUser != null)
                {
                    await GameClient.LogoutAsync(CurrentUser.UserId);
                    await GameClient.DisconnectAsync();
                }

                // 2. 清除本地会话信息
                AccessToken = string.Empty;
                RefreshToken = string.Empty;
                GameTicket = string.Empty;
                CurrentUser = null;

                isGameConnected = false;

                Debug.Log("Logout process completed");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Logout process failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查连接状态
        /// </summary>
        public bool IsConnected()
        {
            return isAuthConnected && isGameConnected;
        }

        /// <summary>
        /// 获取连接状态描述
        /// </summary>
        public string GetConnectionStatus()
        {
            if (isAuthConnected && isGameConnected)
            {
                return "已连接";
            }
            else if (isAuthConnected)
            {
                return "认证服务器已连接，游戏服务器未连接";
            }
            else
            {
                return "未连接";
            }
        }

        private void OnDestroy()
        {
            // 清理资源
            AuthClient?.Dispose();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Debug.Log("Application paused - maintaining connections");
            }
            else
            {
                Debug.Log("Application resumed - checking connections");
                // 可以在这里检查连接状态并尝试重连
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                Debug.Log("Application lost focus");
            }
            else
            {
                Debug.Log("Application gained focus");
            }
        }

        #region Editor Support

#if UNITY_EDITOR
        [ContextMenu("Test Connection")]
        private async void TestConnection()
        {
            Debug.Log("Testing connection...");

            var zones = await GetZoneListAsync();
            Debug.Log($"Test result: Retrieved {zones.Length} zones");
        }

        [ContextMenu("Clear Session")]
        private void ClearSession()
        {
            AccessToken = string.Empty;
            RefreshToken = string.Empty;
            GameTicket = string.Empty;
            CurrentUser = null;
            Debug.Log("Session cleared");
        }
#endif

        #endregion
    }
}
