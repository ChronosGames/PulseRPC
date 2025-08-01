using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using GameApp.Unity.Network;
using GameApp.Shared.Services;

namespace GameApp.Unity.Managers
{
    /// <summary>
    /// 战斗管理器 - 统一管理战斗相关的所有逻辑
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        [Header("Battle Settings")]
        [SerializeField] private string battleServerAddress = "localhost";
        [SerializeField] private int battleServerTcpPort = 8000;
        [SerializeField] private int battleServerKcpPort = 8001;

        [Header("Player Settings")]
        [SerializeField] private int playerId = 1;

        private BattleClient _battleClient;
        private bool _isInitialized = false;
        private bool _isInBattle = false;

        // 战斗状态
        public bool IsConnected => _battleClient?.IsConnected ?? false;
        public bool IsInBattle => _isInBattle;
        public BattleInfo? CurrentBattle => _battleClient?.CurrentBattle;
        public string CurrentBattleId => _battleClient?.CurrentBattleId ?? string.Empty;

        // 战斗事件
        public event Action<bool> OnConnectionStatusChanged;
        public event Action<BattleStateUpdateEvent> OnBattleStateUpdated;
        public event Action<SkillUsedEvent> OnSkillUsed;
        public event Action<DamageDealtEvent> OnDamageDealt;
        public event Action<PlayerDefeatedEvent> OnPlayerDefeated;
        public event Action<BattleEndedEvent> OnBattleEnded;

        private void Awake()
        {
            // 确保只有一个 BattleManager 实例
            if (FindObjectsOfType<BattleManager>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeBattleClient();
        }

        /// <summary>
        /// 初始化战斗客户端
        /// </summary>
        private async void InitializeBattleClient()
        {
            try
            {
                _battleClient = new BattleClient();

                // 订阅连接事件
                _battleClient.OnConnected += OnBattleClientConnected;
                _battleClient.OnDisconnected += OnBattleClientDisconnected;
                _battleClient.OnConnectionError += OnBattleClientError;

                // 订阅战斗事件
                _battleClient.OnBattleStateUpdated += OnBattleStateUpdate;
                _battleClient.OnSkillUsed += OnSkillUse;
                _battleClient.OnDamageDealt += OnDamageDealt;
                _battleClient.OnPlayerDefeated += OnPlayerDefeat;
                _battleClient.OnBattleEnded += OnBattleEnd;

                // 连接到战斗服务器
                bool success = await _battleClient.InitializeAsync(battleServerAddress, battleServerTcpPort, battleServerKcpPort);

                if (success)
                {
                    _isInitialized = true;
                    Debug.Log("BattleManager initialized successfully");
                }
                else
                {
                    Debug.LogError("Failed to initialize BattleManager");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"BattleManager initialization error: {ex.Message}");
            }
        }

        /// <summary>
        /// 加入战斗
        /// </summary>
        public async Task<bool> JoinBattleAsync(string battleType, string? roomId = null, Dictionary<string, object>? parameters = null)
        {
            if (!_isInitialized || _isInBattle)
            {
                Debug.LogWarning("Cannot join battle: not initialized or already in battle");
                return false;
            }

            try
            {
                var response = await _battleClient.JoinBattleAsync(playerId, battleType, roomId, parameters);

                if (response.Success)
                {
                    _isInBattle = true;
                    Debug.Log($"Successfully joined battle: {response.BattleInfo?.BattleId}");
                    return true;
                }
                else
                {
                    Debug.LogWarning($"Failed to join battle: {response.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Join battle error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 离开战斗
        /// </summary>
        public async Task<bool> LeaveBattleAsync()
        {
            if (!_isInBattle)
            {
                Debug.LogWarning("Not in battle");
                return false;
            }

            try
            {
                bool success = await _battleClient.LeaveBattleAsync(playerId);

                if (success)
                {
                    _isInBattle = false;
                    Debug.Log("Successfully left battle");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Leave battle error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 使用技能
        /// </summary>
        public async Task<bool> UseSkillAsync(int skillId, Vector3 targetPosition, int? targetPlayerId = null)
        {
            if (!_isInBattle)
            {
                Debug.LogWarning("Not in battle");
                return false;
            }

            try
            {
                var result = await _battleClient.UseSkillAsync(playerId, skillId, targetPosition, targetPlayerId);
                return result.Success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Use skill error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 移动到战斗位置
        /// </summary>
        public async Task<bool> MoveToBattlePositionAsync(Vector3 position)
        {
            if (!_isInBattle)
            {
                Debug.LogWarning("Not in battle");
                return false;
            }

            try
            {
                return await _battleClient.MoveToBattlePositionAsync(playerId, position);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Move to battle position error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 学习技能
        /// </summary>
        public async Task<bool> LearnSkillAsync(int skillId)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("BattleManager not initialized");
                return false;
            }

            try
            {
                var response = await _battleClient.LearnSkillAsync(playerId, skillId);
                return response.Success;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Learn skill error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取玩家技能列表
        /// </summary>
        public async Task<List<PlayerSkill>> GetPlayerSkillsAsync()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("BattleManager not initialized");
                return new List<PlayerSkill>();
            }

            try
            {
                var response = await _battleClient.GetPlayerSkillsAsync(playerId);
                return response.Success ? response.Skills : new List<PlayerSkill>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get player skills error: {ex.Message}");
                return new List<PlayerSkill>();
            }
        }

        #region 连接事件处理

        private void OnBattleClientConnected()
        {
            Debug.Log("Battle client connected");
            OnConnectionStatusChanged?.Invoke(true);
        }

        private void OnBattleClientDisconnected(string reason)
        {
            Debug.Log($"Battle client disconnected: {reason}");
            _isInBattle = false;
            OnConnectionStatusChanged?.Invoke(false);
        }

        private void OnBattleClientError(string error)
        {
            Debug.LogError($"Battle client error: {error}");
            OnConnectionStatusChanged?.Invoke(false);
        }

        #endregion

        #region 战斗事件处理

        private void OnBattleStateUpdate(BattleStateUpdateEvent eventData)
        {
            Debug.Log($"Battle state updated: {eventData.BattleInfo?.Status}");
            OnBattleStateUpdated?.Invoke(eventData);
        }

        private void OnSkillUse(SkillUsedEvent eventData)
        {
            Debug.Log($"Skill used: Player {eventData.UserId} used skill {eventData.SkillId}");
            OnSkillUsed?.Invoke(eventData);
        }

        private void OnDamageDealt(DamageDealtEvent eventData)
        {
            Debug.Log($"Damage dealt: {eventData.Damage} to player {eventData.DefenderId}");
            OnDamageDealt?.Invoke(eventData);
        }

        private void OnPlayerDefeat(PlayerDefeatedEvent eventData)
        {
            Debug.Log($"Player defeated: {eventData.PlayerId}");
            OnPlayerDefeated?.Invoke(eventData);
        }

        private void OnBattleEnd(BattleEndedEvent eventData)
        {
            Debug.Log($"Battle ended: Winner team {eventData.WinnerTeam}");
            _isInBattle = false;
            OnBattleEnded?.Invoke(eventData);
        }

        #endregion

        #region Unity生命周期

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && _isInBattle)
            {
                // 应用暂停时离开战斗
                _ = LeaveBattleAsync();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _isInBattle)
            {
                // 应用失焦时离开战斗
                _ = LeaveBattleAsync();
            }
        }

        private void OnDestroy()
        {
            if (_battleClient != null)
            {
                _battleClient.Dispose();
            }
        }

        #endregion

        #region 公共属性设置（用于Inspector）

        [ContextMenu("Test Join PVP Battle")]
        public async void TestJoinPvpBattle()
        {
            await JoinBattleAsync("pvp");
        }

        [ContextMenu("Test Leave Battle")]
        public async void TestLeaveBattle()
        {
            await LeaveBattleAsync();
        }

        [ContextMenu("Test Use Skill")]
        public async void TestUseSkill()
        {
            await UseSkillAsync(1, Vector3.zero);
        }

        #endregion
    }
}
