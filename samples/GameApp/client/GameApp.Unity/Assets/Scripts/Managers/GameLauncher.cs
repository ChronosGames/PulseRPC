using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameApp.Unity.Network;
using GameApp.Unity.Utils;

namespace GameApp.Unity.Managers
{
    /// <summary>
    /// 游戏启动器 - 统一管理游戏的初始化和启动流程
    /// </summary>
    public class GameLauncher : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private GameConfig gameConfig;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool connectOnStart = true;

        [Header("Scene Management")]
        [SerializeField] private string authSceneName = "AuthScene";
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string battleSceneName = "BattleScene";

        // 网络客户端
        private AuthClient _authClient;
        private GameClient _gameClient;
        private BattleManager _battleManager;

        // 启动状态
        private bool _isInitialized = false;
        private bool _isConnectedToAuth = false;
        private bool _isConnectedToGame = false;
        private bool _isConnectedToBattle = false;

        // 事件
        public event Action OnGameInitialized;
        public event Action<string> OnInitializationFailed;
        public event Action<string> OnSceneChanged;

        // 单例模式
        public static GameLauncher Instance { get; private set; }

        private void Awake()
        {
            // 单例模式实现
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // 验证配置
            if (gameConfig == null)
            {
                Debug.LogError("GameConfig is not assigned!");
                return;
            }
        }

        private void Start()
        {
            if (autoStart)
            {
                InitializeGame();
            }
        }

        /// <summary>
        /// 初始化游戏
        /// </summary>
        public async void InitializeGame()
        {
            try
            {
                Debug.Log("Starting game initialization...");

                // 验证配置
                if (!gameConfig.ValidateConfig())
                {
                    throw new Exception("Game configuration validation failed");
                }

                // 从环境变量覆盖配置
                gameConfig.OverrideFromEnvironment();

                // 初始化网络客户端
                await InitializeNetworkClients();

                // 连接到服务器（如果设置了自动连接）
                if (connectOnStart)
                {
                    await ConnectToServers();
                }

                _isInitialized = true;
                Debug.Log("Game initialization completed successfully");
                OnGameInitialized?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Game initialization failed: {ex.Message}");
                OnInitializationFailed?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// 初始化网络客户端
        /// </summary>
        private async Task InitializeNetworkClients()
        {
            // 初始化 AuthClient
            _authClient = new AuthClient(gameConfig.AuthServerUrl);

            // 初始化 GameClient
            _gameClient = new GameClient();

            // 查找或创建 BattleManager
            _battleManager = FindObjectOfType<BattleManager>();
            if (_battleManager == null)
            {
                var battleManagerGo = new GameObject("BattleManager");
                _battleManager = battleManagerGo.AddComponent<BattleManager>();
            }

            Debug.Log("Network clients initialized");
        }

        /// <summary>
        /// 连接到所有服务器
        /// </summary>
        private async Task ConnectToServers()
        {
            Debug.Log("Connecting to servers...");

            // 连接到 AuthServer (HTTP客户端，不需要异步连接)
            _isConnectedToAuth = true;
            Debug.Log("Connected to AuthServer");

            // 连接到 GameServer
            try
            {
                bool gameConnected = await _gameClient.InitializeAsync(
                    gameConfig.GameServerAddress,
                    gameConfig.GameServerTcpPort,
                    gameConfig.GameServerKcpPort);

                _isConnectedToGame = gameConnected;
                Debug.Log($"GameServer connection: {(gameConnected ? "Success" : "Failed")}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to connect to GameServer: {ex.Message}");
                _isConnectedToGame = false;
            }

            // BattleManager 会自动尝试连接到 BattleServer
            // 这里我们等待一下然后检查状态
            await Task.Delay(2000);
            _isConnectedToBattle = _battleManager?.IsConnected ?? false;
            Debug.Log($"BattleServer connection: {(_isConnectedToBattle ? "Success" : "Failed")}");
        }

        /// <summary>
        /// 开始认证流程
        /// </summary>
        public async Task<bool> StartAuthenticationAsync(string username, string password)
        {
            if (!_isInitialized || !_isConnectedToAuth)
            {
                Debug.LogError("Game not initialized or not connected to auth server");
                return false;
            }

            try
            {
                Debug.Log($"Starting authentication for user: {username}");

                // 执行登录
                var loginResponse = await _authClient.LoginAsync(username, password, SystemInfo.deviceUniqueIdentifier);

                if (loginResponse.Success)
                {
                    Debug.Log("Authentication successful");

                    // 可以在这里保存用户信息和游戏票据
                    string gameTicket = loginResponse.Data.GameTicket;

                    // 自动登录到游戏服务器
                    if (_isConnectedToGame)
                    {
                        var gameLoginResponse = await _gameClient.LoginAsync(gameTicket);
                        if (gameLoginResponse.Success)
                        {
                            Debug.Log("Game server login successful");
                            return true;
                        }
                        else
                        {
                            Debug.LogError($"Game server login failed: {gameLoginResponse.Message}");
                        }
                    }

                    return true;
                }
                else
                {
                    Debug.LogError($"Authentication failed: {loginResponse.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Authentication error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 加载场景
        /// </summary>
        public async Task LoadSceneAsync(string sceneName)
        {
            try
            {
                Debug.Log($"Loading scene: {sceneName}");

                var asyncLoad = SceneManager.LoadSceneAsync(sceneName);

                while (!asyncLoad.isDone)
                {
                    await Task.Yield();
                }

                Debug.Log($"Scene loaded: {sceneName}");
                OnSceneChanged?.Invoke(sceneName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load scene {sceneName}: {ex.Message}");
            }
        }

        /// <summary>
        /// 进入战斗场景
        /// </summary>
        public async Task EnterBattleSceneAsync(string battleType = "pvp")
        {
            if (!_isConnectedToBattle)
            {
                Debug.LogError("Not connected to battle server");
                return;
            }

            try
            {
                // 加载战斗场景
                await LoadSceneAsync(battleSceneName);

                // 等待场景加载完成后加入战斗
                await Task.Delay(1000);

                if (_battleManager != null)
                {
                    bool success = await _battleManager.JoinBattleAsync(battleType);
                    if (!success)
                    {
                        Debug.LogError("Failed to join battle");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Enter battle scene error: {ex.Message}");
            }
        }

        /// <summary>
        /// 退出战斗并返回游戏场景
        /// </summary>
        public async Task ExitBattleSceneAsync()
        {
            try
            {
                // 离开战斗
                if (_battleManager != null && _battleManager.IsInBattle)
                {
                    await _battleManager.LeaveBattleAsync();
                }

                // 返回游戏场景
                await LoadSceneAsync(gameSceneName);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exit battle scene error: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取连接状态信息
        /// </summary>
        public string GetConnectionStatus()
        {
            return $"Auth: {(_isConnectedToAuth ? "Connected" : "Disconnected")}, " +
                   $"Game: {(_isConnectedToGame ? "Connected" : "Disconnected")}, " +
                   $"Battle: {(_isConnectedToBattle ? "Connected" : "Disconnected")}";
        }

        /// <summary>
        /// 断开所有连接
        /// </summary>
        public async Task DisconnectAllAsync()
        {
            try
            {
                Debug.Log("Disconnecting from all servers...");

                if (_gameClient != null)
                {
                    await _gameClient.DisconnectAsync();
                    _isConnectedToGame = false;
                }

                if (_battleManager != null)
                {
                    _battleManager.GetComponent<BattleClient>()?.Dispose();
                    _isConnectedToBattle = false;
                }

                _isConnectedToAuth = false;
                Debug.Log("Disconnected from all servers");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Disconnect error: {ex.Message}");
            }
        }

        /// <summary>
        /// 重新连接到所有服务器
        /// </summary>
        public async Task ReconnectAllAsync()
        {
            await DisconnectAllAsync();
            await Task.Delay(1000);
            await ConnectToServers();
        }

        #region Unity 生命周期

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                Debug.Log("Application paused");
                // 可以在这里暂停网络连接或保存状态
            }
            else
            {
                Debug.Log("Application resumed");
                // 可以在这里恢复网络连接
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

        private async void OnApplicationQuit()
        {
            Debug.Log("Application quitting...");
            await DisconnectAllAsync();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region 调试方法

        [ContextMenu("Test Authentication")]
        public async void TestAuthentication()
        {
            await StartAuthenticationAsync("testuser", "testpass");
        }

        [ContextMenu("Test Battle Scene")]
        public async void TestBattleScene()
        {
            await EnterBattleSceneAsync("pvp");
        }

        [ContextMenu("Show Connection Status")]
        public void ShowConnectionStatus()
        {
            Debug.Log($"Connection Status: {GetConnectionStatus()}");
        }

        [ContextMenu("Reconnect All")]
        public async void ReconnectAll()
        {
            await ReconnectAllAsync();
        }

        #endregion
    }
}
