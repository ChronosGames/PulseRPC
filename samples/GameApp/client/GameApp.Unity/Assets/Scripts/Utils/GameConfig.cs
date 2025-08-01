using UnityEngine;

namespace GameApp.Unity.Utils
{
    /// <summary>
    /// 游戏配置管理器 - 统一管理游戏中的各种配置
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "GameApp/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("Auth Server Configuration")]
        [SerializeField] private string authServerUrl = "http://localhost:8080";

        [Header("Game Server Configuration")]
        [SerializeField] private string gameServerAddress = "localhost";
        [SerializeField] private int gameServerTcpPort = 9000;
        [SerializeField] private int gameServerKcpPort = 9001;

        [Header("Battle Server Configuration")]
        [SerializeField] private string battleServerAddress = "localhost";
        [SerializeField] private int battleServerTcpPort = 8000;
        [SerializeField] private int battleServerKcpPort = 8001;

        [Header("Player Settings")]
        [SerializeField] private int defaultPlayerId = 1;
        [SerializeField] private string defaultPlayerName = "Player";

        [Header("Battle Settings")]
        [SerializeField] private float skillCooldownTime = 1.0f;
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int maxMana = 100;

        [Header("UI Settings")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private float damageNumberDuration = 2.0f;
        [SerializeField] private float effectDuration = 3.0f;

        // 属性访问器
        public string AuthServerUrl => authServerUrl;

        public string GameServerAddress => gameServerAddress;
        public int GameServerTcpPort => gameServerTcpPort;
        public int GameServerKcpPort => gameServerKcpPort;

        public string BattleServerAddress => battleServerAddress;
        public int BattleServerTcpPort => battleServerTcpPort;
        public int BattleServerKcpPort => battleServerKcpPort;

        public int DefaultPlayerId => defaultPlayerId;
        public string DefaultPlayerName => defaultPlayerName;

        public float SkillCooldownTime => skillCooldownTime;
        public float MoveSpeed => moveSpeed;
        public int MaxHealth => maxHealth;
        public int MaxMana => maxMana;

        public bool ShowDebugInfo => showDebugInfo;
        public float DamageNumberDuration => damageNumberDuration;
        public float EffectDuration => effectDuration;

        /// <summary>
        /// 获取完整的 Game Server 地址
        /// </summary>
        public string GetGameServerFullAddress()
        {
            return $"{gameServerAddress}:{gameServerTcpPort}";
        }

        /// <summary>
        /// 获取完整的 Battle Server 地址
        /// </summary>
        public string GetBattleServerFullAddress()
        {
            return $"{battleServerAddress}:{battleServerTcpPort}";
        }

        /// <summary>
        /// 验证配置是否有效
        /// </summary>
        public bool ValidateConfig()
        {
            if (string.IsNullOrEmpty(authServerUrl))
            {
                Debug.LogError("Auth Server URL is not configured");
                return false;
            }

            if (string.IsNullOrEmpty(gameServerAddress))
            {
                Debug.LogError("Game Server Address is not configured");
                return false;
            }

            if (string.IsNullOrEmpty(battleServerAddress))
            {
                Debug.LogError("Battle Server Address is not configured");
                return false;
            }

            if (gameServerTcpPort <= 0 || gameServerKcpPort <= 0)
            {
                Debug.LogError("Game Server ports are not properly configured");
                return false;
            }

            if (battleServerTcpPort <= 0 || battleServerKcpPort <= 0)
            {
                Debug.LogError("Battle Server ports are not properly configured");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 从环境变量或命令行参数覆盖配置
        /// </summary>
        public void OverrideFromEnvironment()
        {
            // Auth Server
            string envAuthUrl = System.Environment.GetEnvironmentVariable("GAMEAPP_AUTH_SERVER_URL");
            if (!string.IsNullOrEmpty(envAuthUrl))
            {
                authServerUrl = envAuthUrl;
                Debug.Log($"Auth Server URL overridden from environment: {authServerUrl}");
            }

            // Game Server
            string envGameAddress = System.Environment.GetEnvironmentVariable("GAMEAPP_GAME_SERVER_ADDRESS");
            if (!string.IsNullOrEmpty(envGameAddress))
            {
                gameServerAddress = envGameAddress;
                Debug.Log($"Game Server Address overridden from environment: {gameServerAddress}");
            }

            string envGameTcpPort = System.Environment.GetEnvironmentVariable("GAMEAPP_GAME_SERVER_TCP_PORT");
            if (!string.IsNullOrEmpty(envGameTcpPort) && int.TryParse(envGameTcpPort, out int gameTcpPort))
            {
                gameServerTcpPort = gameTcpPort;
                Debug.Log($"Game Server TCP Port overridden from environment: {gameServerTcpPort}");
            }

            string envGameKcpPort = System.Environment.GetEnvironmentVariable("GAMEAPP_GAME_SERVER_KCP_PORT");
            if (!string.IsNullOrEmpty(envGameKcpPort) && int.TryParse(envGameKcpPort, out int gameKcpPort))
            {
                gameServerKcpPort = gameKcpPort;
                Debug.Log($"Game Server KCP Port overridden from environment: {gameServerKcpPort}");
            }

            // Battle Server
            string envBattleAddress = System.Environment.GetEnvironmentVariable("GAMEAPP_BATTLE_SERVER_ADDRESS");
            if (!string.IsNullOrEmpty(envBattleAddress))
            {
                battleServerAddress = envBattleAddress;
                Debug.Log($"Battle Server Address overridden from environment: {battleServerAddress}");
            }

            string envBattleTcpPort = System.Environment.GetEnvironmentVariable("GAMEAPP_BATTLE_SERVER_TCP_PORT");
            if (!string.IsNullOrEmpty(envBattleTcpPort) && int.TryParse(envBattleTcpPort, out int battleTcpPort))
            {
                battleServerTcpPort = battleTcpPort;
                Debug.Log($"Battle Server TCP Port overridden from environment: {battleServerTcpPort}");
            }

            string envBattleKcpPort = System.Environment.GetEnvironmentVariable("GAMEAPP_BATTLE_SERVER_KCP_PORT");
            if (!string.IsNullOrEmpty(envBattleKcpPort) && int.TryParse(envBattleKcpPort, out int battleKcpPort))
            {
                battleServerKcpPort = battleKcpPort;
                Debug.Log($"Battle Server KCP Port overridden from environment: {battleServerKcpPort}");
            }

            // Player Settings
            string envPlayerId = System.Environment.GetEnvironmentVariable("GAMEAPP_PLAYER_ID");
            if (!string.IsNullOrEmpty(envPlayerId) && int.TryParse(envPlayerId, out int playerId))
            {
                defaultPlayerId = playerId;
                Debug.Log($"Player ID overridden from environment: {defaultPlayerId}");
            }
        }

        #if UNITY_EDITOR
        /// <summary>
        /// 编辑器下的验证方法
        /// </summary>
        private void OnValidate()
        {
            // 确保端口号在有效范围内
            gameServerTcpPort = Mathf.Clamp(gameServerTcpPort, 1024, 65535);
            gameServerKcpPort = Mathf.Clamp(gameServerKcpPort, 1024, 65535);
            battleServerTcpPort = Mathf.Clamp(battleServerTcpPort, 1024, 65535);
            battleServerKcpPort = Mathf.Clamp(battleServerKcpPort, 1024, 65535);

            // 确保各种时间和数值在合理范围内
            skillCooldownTime = Mathf.Max(0.1f, skillCooldownTime);
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            maxHealth = Mathf.Max(1, maxHealth);
            maxMana = Mathf.Max(1, maxMana);
            damageNumberDuration = Mathf.Max(0.5f, damageNumberDuration);
            effectDuration = Mathf.Max(0.5f, effectDuration);
        }
        #endif
    }
}
