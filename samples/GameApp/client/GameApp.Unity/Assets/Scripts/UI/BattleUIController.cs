using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GameApp.Unity.Managers;
using GameApp.Shared.Services;

namespace GameApp.Unity.UI
{
    /// <summary>
    /// 战斗UI控制器 - 管理战斗界面的显示和交互
    /// </summary>
    public class BattleUIController : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private GameObject battlePanel;
        [SerializeField] private TextMeshProUGUI connectionStatusText;
        [SerializeField] private TextMeshProUGUI battleStatusText;
        [SerializeField] private TextMeshProUGUI battleInfoText;

        [Header("Battle Controls")]
        [SerializeField] private Button joinPvpButton;
        [SerializeField] private Button joinPveButton;
        [SerializeField] private Button leaveBattleButton;
        [SerializeField] private Button[] skillButtons;

        [Header("Battle Info")]
        [SerializeField] private TextMeshProUGUI healthText;
        [SerializeField] private TextMeshProUGUI manaText;
        [SerializeField] private Slider healthSlider;
        [SerializeField] private Slider manaSlider;

        [Header("Chat/Log")]
        [SerializeField] private ScrollRect logScrollRect;
        [SerializeField] private TextMeshProUGUI logText;
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private Button sendChatButton;

        private BattleManager _battleManager;
        private List<PlayerSkill> _playerSkills = new List<PlayerSkill>();
        private string _logContent = "";

        private void Start()
        {
            InitializeUI();
            FindBattleManager();
            SetupEventListeners();
        }

        /// <summary>
        /// 初始化UI
        /// </summary>
        private void InitializeUI()
        {
            // 初始状态
            UpdateConnectionStatus(false);
            UpdateBattleStatus(false);

            // 设置按钮点击事件
            if (joinPvpButton != null)
                joinPvpButton.onClick.AddListener(() => JoinBattle("pvp"));

            if (joinPveButton != null)
                joinPveButton.onClick.AddListener(() => JoinBattle("pve"));

            if (leaveBattleButton != null)
                leaveBattleButton.onClick.AddListener(LeaveBattle);

            if (sendChatButton != null)
                sendChatButton.onClick.AddListener(SendChat);

            // 设置技能按钮
            for (int i = 0; i < skillButtons.Length; i++)
            {
                int skillIndex = i; // 闭包捕获
                if (skillButtons[i] != null)
                {
                    skillButtons[i].onClick.AddListener(() => UseSkill(skillIndex));
                }
            }

            // 初始化血量和法力值
            UpdatePlayerStats(100, 100, 100, 100);
        }

        /// <summary>
        /// 查找战斗管理器
        /// </summary>
        private void FindBattleManager()
        {
            _battleManager = FindObjectOfType<BattleManager>();
            if (_battleManager == null)
            {
                Debug.LogError("BattleManager not found! Please add BattleManager to the scene.");
                return;
            }
        }

        /// <summary>
        /// 设置事件监听器
        /// </summary>
        private void SetupEventListeners()
        {
            if (_battleManager == null) return;

            // 连接状态事件
            _battleManager.OnConnectionStatusChanged += UpdateConnectionStatus;

            // 战斗事件
            _battleManager.OnBattleStateUpdated += OnBattleStateUpdated;
            _battleManager.OnSkillUsed += OnSkillUsed;
            _battleManager.OnDamageDealt += OnDamageDealt;
            _battleManager.OnPlayerDefeated += OnPlayerDefeated;
            _battleManager.OnBattleEnded += OnBattleEnded;
        }

        /// <summary>
        /// 加入战斗
        /// </summary>
        private async void JoinBattle(string battleType)
        {
            if (_battleManager == null) return;

            AddLog($"尝试加入 {battleType.ToUpper()} 战斗...");

            bool success = await _battleManager.JoinBattleAsync(battleType);

            if (success)
            {
                AddLog($"成功加入 {battleType.ToUpper()} 战斗！");
                UpdateBattleStatus(true);
                await LoadPlayerSkills();
            }
            else
            {
                AddLog($"加入 {battleType.ToUpper()} 战斗失败！");
            }
        }

        /// <summary>
        /// 离开战斗
        /// </summary>
        private async void LeaveBattle()
        {
            if (_battleManager == null) return;

            AddLog("离开战斗...");

            bool success = await _battleManager.LeaveBattleAsync();

            if (success)
            {
                AddLog("成功离开战斗！");
                UpdateBattleStatus(false);
            }
            else
            {
                AddLog("离开战斗失败！");
            }
        }

        /// <summary>
        /// 使用技能
        /// </summary>
        private async void UseSkill(int skillIndex)
        {
            if (_battleManager == null || !_battleManager.IsInBattle) return;

            if (skillIndex < _playerSkills.Count)
            {
                var skill = _playerSkills[skillIndex];
                AddLog($"使用技能: {skill.Name}");

                // 使用技能，默认目标位置为原点
                bool success = await _battleManager.UseSkillAsync(skill.SkillId, Vector3.zero);

                if (!success)
                {
                    AddLog($"技能 {skill.Name} 使用失败！");
                }
            }
            else
            {
                AddLog($"技能槽 {skillIndex + 1} 为空！");
            }
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        private void SendChat()
        {
            if (chatInput == null || string.IsNullOrEmpty(chatInput.text)) return;

            string message = chatInput.text;
            AddLog($"[玩家]: {message}");
            chatInput.text = "";
        }

        /// <summary>
        /// 加载玩家技能
        /// </summary>
        private async System.Threading.Tasks.Task LoadPlayerSkills()
        {
            if (_battleManager == null) return;

            _playerSkills = await _battleManager.GetPlayerSkillsAsync();
            UpdateSkillButtons();
        }

        /// <summary>
        /// 更新技能按钮
        /// </summary>
        private void UpdateSkillButtons()
        {
            for (int i = 0; i < skillButtons.Length; i++)
            {
                if (skillButtons[i] != null)
                {
                    var buttonText = skillButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        if (i < _playerSkills.Count)
                        {
                            buttonText.text = _playerSkills[i].Name;
                            skillButtons[i].interactable = true;
                        }
                        else
                        {
                            buttonText.text = "空";
                            skillButtons[i].interactable = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 更新连接状态
        /// </summary>
        private void UpdateConnectionStatus(bool isConnected)
        {
            if (connectionStatusText != null)
            {
                connectionStatusText.text = isConnected ? "已连接" : "未连接";
                connectionStatusText.color = isConnected ? Color.green : Color.red;
            }
        }

        /// <summary>
        /// 更新战斗状态
        /// </summary>
        private void UpdateBattleStatus(bool isInBattle)
        {
            if (battleStatusText != null)
            {
                battleStatusText.text = isInBattle ? "战斗中" : "未在战斗";
                battleStatusText.color = isInBattle ? Color.yellow : Color.gray;
            }

            // 更新按钮状态
            if (joinPvpButton != null)
                joinPvpButton.interactable = !isInBattle && (_battleManager?.IsConnected ?? false);

            if (joinPveButton != null)
                joinPveButton.interactable = !isInBattle && (_battleManager?.IsConnected ?? false);

            if (leaveBattleButton != null)
                leaveBattleButton.interactable = isInBattle;

            // 更新技能按钮状态
            for (int i = 0; i < skillButtons.Length; i++)
            {
                if (skillButtons[i] != null)
                {
                    skillButtons[i].interactable = isInBattle && i < _playerSkills.Count;
                }
            }
        }

        /// <summary>
        /// 更新玩家状态
        /// </summary>
        private void UpdatePlayerStats(int currentHealth, int maxHealth, int currentMana, int maxMana)
        {
            if (healthText != null)
                healthText.text = $"{currentHealth}/{maxHealth}";

            if (manaText != null)
                manaText.text = $"{currentMana}/{maxMana}";

            if (healthSlider != null)
            {
                healthSlider.maxValue = maxHealth;
                healthSlider.value = currentHealth;
            }

            if (manaSlider != null)
            {
                manaSlider.maxValue = maxMana;
                manaSlider.value = currentMana;
            }
        }

        /// <summary>
        /// 添加日志
        /// </summary>
        private void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logContent += $"[{timestamp}] {message}\n";

            if (logText != null)
            {
                logText.text = _logContent;
            }

            // 自动滚动到底部
            if (logScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                logScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        #region 战斗事件处理

        private void OnBattleStateUpdated(BattleStateUpdateEvent eventData)
        {
            AddLog($"战斗状态更新: {eventData.Status}");

            if (battleInfoText)
            {
                battleInfoText.text = $"战斗ID: {eventData.BattleId}\n状态: {eventData.Status}";
            }
        }

        private void OnSkillUsed(SkillUsedEvent eventData)
        {
            AddLog($"玩家 {eventData.PlayerId} 使用了技能 {eventData.Skill.Name} ({eventData.Skill.SkillId})");
        }

        private void OnDamageDealt(DamageDealtEvent eventData)
        {
            foreach (var info in eventData.DamageResults)
            {
                AddLog($"伤害：{info.Damage} ({info.Type}) - {eventData.SourcePlayerId} → {info.TargetPlayerId}");
            }

            // 如果是当前玩家受到伤害，更新血量显示
            // 这里需要根据实际的玩家ID来判断
            // UpdatePlayerStats(newHealth, maxHealth, currentMana, maxMana);
        }

        private void OnPlayerDefeated(PlayerDefeatedEvent eventData)
        {
            AddLog($"玩家 {eventData.PlayerId} 被击败了！");
        }

        private void OnBattleEnded(BattleEndedEvent eventData)
        {
            AddLog($"战斗结束！获胜队伍: {eventData.WinnerTeam}");
            UpdateBattleStatus(false);
        }

        #endregion

        private void OnDestroy()
        {
            // 清理事件监听器
            if (!_battleManager)
            {
                return;
            }

            _battleManager.OnConnectionStatusChanged -= UpdateConnectionStatus;
            _battleManager.OnBattleStateUpdated -= OnBattleStateUpdated;
            _battleManager.OnSkillUsed -= OnSkillUsed;
            _battleManager.OnDamageDealt -= OnDamageDealt;
            _battleManager.OnPlayerDefeated -= OnPlayerDefeated;
            _battleManager.OnBattleEnded -= OnBattleEnded;
        }
    }
}
