using System;
using System.Collections.Generic;
using UnityEngine;
using GameApp.Unity.Managers;
using GameApp.Shared.Services;

namespace GameApp.Unity.Managers
{
    /// <summary>
    /// 战斗场景控制器 - 管理3D战斗场景中的玩家、特效等
    /// </summary>
    public class BattleSceneController : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Transform playerSpawnPoint;
        [SerializeField] private Transform[] enemySpawnPoints;
        [SerializeField] private Camera battleCamera;

        [Header("Player Settings")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private int currentPlayerId = 1;

        [Header("Visual Effects")]
        [SerializeField] private GameObject[] skillEffectPrefabs;
        [SerializeField] private GameObject damageNumberPrefab;
        [SerializeField] private GameObject deathEffectPrefab;

        private BattleManager _battleManager;
        private Dictionary<int, GameObject> _battlePlayers = new Dictionary<int, GameObject>();
        private GameObject _localPlayerObject;

        private void Start()
        {
            InitializeScene();
            FindBattleManager();
            SetupEventListeners();
        }

        /// <summary>
        /// 初始化场景
        /// </summary>
        private void InitializeScene()
        {
            // 创建本地玩家对象
            if (playerPrefab != null && playerSpawnPoint != null)
            {
                _localPlayerObject = Instantiate(playerPrefab, playerSpawnPoint.position, playerSpawnPoint.rotation);
                _localPlayerObject.name = $"Player_{currentPlayerId}";
                _battlePlayers[currentPlayerId] = _localPlayerObject;

                // 设置相机跟随（简单实现）
                if (battleCamera != null)
                {
                    var cameraFollow = battleCamera.GetComponent<CameraFollow>();
                    if (cameraFollow != null)
                    {
                        cameraFollow.SetTarget(_localPlayerObject.transform);
                    }
                }
            }
        }

        /// <summary>
        /// 查找战斗管理器
        /// </summary>
        private void FindBattleManager()
        {
            _battleManager = FindObjectOfType<BattleManager>();
            if (_battleManager == null)
            {
                Debug.LogError("BattleManager not found!");
                return;
            }
        }

        /// <summary>
        /// 设置事件监听器
        /// </summary>
        private void SetupEventListeners()
        {
            if (_battleManager == null) return;

            _battleManager.OnBattleStateUpdated += OnBattleStateUpdated;
            _battleManager.OnSkillUsed += OnSkillUsed;
            _battleManager.OnDamageDealt += OnDamageDealt;
            _battleManager.OnPlayerDefeated += OnPlayerDefeated;
            _battleManager.OnBattleEnded += OnBattleEnded;
        }

        /// <summary>
        /// 获取或创建战斗玩家对象
        /// </summary>
        private GameObject GetOrCreateBattlePlayer(int playerId, Vector3? position = null)
        {
            if (_battlePlayers.TryGetValue(playerId, out GameObject playerObj) && playerObj != null)
            {
                return playerObj;
            }

            // 创建新的玩家对象
            if (playerPrefab != null)
            {
                Vector3 spawnPos = position ?? GetSpawnPosition(playerId);
                playerObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
                playerObj.name = $"Player_{playerId}";
                _battlePlayers[playerId] = playerObj;

                // 如果不是本地玩家，可以设置不同的材质或标识
                if (playerId != currentPlayerId)
                {
                    SetEnemyAppearance(playerObj);
                }

                Debug.Log($"Created battle player: {playerId} at {spawnPos}");
                return playerObj;
            }

            return null;
        }

        /// <summary>
        /// 获取出生位置
        /// </summary>
        private Vector3 GetSpawnPosition(int playerId)
        {
            if (playerId == currentPlayerId && playerSpawnPoint != null)
            {
                return playerSpawnPoint.position;
            }

            // 为其他玩家分配敌人出生点
            if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
            {
                int spawnIndex = (playerId - 1) % enemySpawnPoints.Length;
                if (enemySpawnPoints[spawnIndex] != null)
                {
                    return enemySpawnPoints[spawnIndex].position;
                }
            }

            // 默认位置
            return Vector3.zero;
        }

        /// <summary>
        /// 设置敌人外观
        /// </summary>
        private void SetEnemyAppearance(GameObject playerObj)
        {
            // 简单实现：改变颜色
            var renderer = playerObj.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = Color.red;
            }

            // 添加敌人标识
            var nameTag = playerObj.GetComponentInChildren<TextMesh>();
            if (nameTag != null)
            {
                nameTag.text = "Enemy";
                nameTag.color = Color.red;
            }
        }

        /// <summary>
        /// 播放技能特效
        /// </summary>
        private void PlaySkillEffect(int skillId, Vector3 position, Vector3? targetPosition = null)
        {
            if (skillEffectPrefabs == null || skillId >= skillEffectPrefabs.Length) return;

            var effectPrefab = skillEffectPrefabs[skillId];
            if (effectPrefab != null)
            {
                var effect = Instantiate(effectPrefab, position, Quaternion.identity);

                // 如果有目标位置，让特效面向目标
                if (targetPosition.HasValue)
                {
                    effect.transform.LookAt(targetPosition.Value);
                }

                // 自动销毁特效
                Destroy(effect, 3f);

                Debug.Log($"Played skill effect {skillId} at {position}");
            }
        }

        /// <summary>
        /// 显示伤害数字
        /// </summary>
        private void ShowDamageNumber(Vector3 position, int damage, string damageType)
        {
            if (damageNumberPrefab == null) return;

            var damageObj = Instantiate(damageNumberPrefab, position + Vector3.up * 2f, Quaternion.identity);

            // 设置伤害数字
            var textMesh = damageObj.GetComponent<TextMesh>();
            if (textMesh != null)
            {
                textMesh.text = damage.ToString();

                // 根据伤害类型设置颜色
                switch (damageType.ToLower())
                {
                    case "physical":
                        textMesh.color = Color.white;
                        break;
                    case "magical":
                        textMesh.color = Color.blue;
                        break;
                    case "critical":
                        textMesh.color = Color.yellow;
                        break;
                    default:
                        textMesh.color = Color.red;
                        break;
                }
            }

            // 添加动画效果（简单的向上浮动）
            var rigidbody = damageObj.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.AddForce(Vector3.up * 5f + UnityEngine.Random.insideUnitSphere * 2f, ForceMode.Impulse);
            }

            // 自动销毁
            Destroy(damageObj, 2f);
        }

        /// <summary>
        /// 播放死亡特效
        /// </summary>
        private void PlayDeathEffect(Vector3 position)
        {
            if (deathEffectPrefab != null)
            {
                var effect = Instantiate(deathEffectPrefab, position, Quaternion.identity);
                Destroy(effect, 5f);
            }
        }

        /// <summary>
        /// 移动玩家到指定位置
        /// </summary>
        public async void MovePlayerToPosition(Vector3 targetPosition)
        {
            if (_battleManager != null && _battleManager.IsInBattle)
            {
                bool success = await _battleManager.MoveToBattlePositionAsync(targetPosition);

                if (success && _localPlayerObject != null)
                {
                    // 本地移动（平滑移动）
                    StartCoroutine(SmoothMoveToPosition(_localPlayerObject.transform, targetPosition));
                }
            }
        }

        /// <summary>
        /// 平滑移动到目标位置
        /// </summary>
        private System.Collections.IEnumerator SmoothMoveToPosition(Transform playerTransform, Vector3 targetPosition)
        {
            Vector3 startPosition = playerTransform.position;
            float moveTime = 1f; // 移动时间
            float elapsedTime = 0f;

            while (elapsedTime < moveTime)
            {
                elapsedTime += Time.deltaTime;
                float t = elapsedTime / moveTime;

                // 使用平滑插值
                playerTransform.position = Vector3.Lerp(startPosition, targetPosition, t);
                yield return null;
            }

            playerTransform.position = targetPosition;
        }

        #region 战斗事件处理

        private void OnBattleStateUpdated(BattleStateUpdateEvent eventData)
        {
            Debug.Log($"Battle scene updated: {eventData.Status}");

            // 根据战斗状态更新场景
            if (eventData.Status == "InProgress")
            {
                // 战斗开始，可以播放开始特效
            }
        }

        private void OnSkillUsed(SkillUsedEvent eventData)
        {
            // 获取使用技能的玩家位置
            var playerObj = GetOrCreateBattlePlayer(eventData.PlayerId);
            if (playerObj != null)
            {
                Vector3 playerPos = playerObj.transform.position;
                Vector3 targetPos = new Vector3(
                    eventData.TargetPosition?.X ?? 0f,
                    eventData.TargetPosition?.Y ?? 0f,
                    eventData.TargetPosition?.Z ?? 0f
                );

                // 播放技能特效
                PlaySkillEffect(eventData.Skill.SkillId, playerPos, targetPos);

                Debug.Log($"Player {eventData.PlayerId} used skill {eventData.Skill.Name} at {targetPos}");
            }
        }

        private void OnDamageDealt(DamageDealtEvent eventData)
        {
            foreach (var damageInfo in eventData.DamageResults)
            {
                Debug.Log($"Damage dealt: {damageInfo.Damage} to player {damageInfo.TargetPlayerId} by player {eventData.SourcePlayerId}");


                // 显示伤害数字
                var defenderObj = GetOrCreateBattlePlayer(damageInfo.TargetPlayerId);
                if (defenderObj != null)
                {
                    ShowDamageNumber(defenderObj.transform.position, damageInfo.Damage, damageInfo.Type.ToString());
                }

                Debug.Log($"Damage dealt: {damageInfo.Damage} to player {damageInfo.TargetPlayerId}");
            }
        }

        private void OnPlayerDefeated(PlayerDefeatedEvent eventData)
        {
            // 播放死亡特效
            var playerObj = GetOrCreateBattlePlayer(eventData.PlayerId);
            if (playerObj != null)
            {
                PlayDeathEffect(playerObj.transform.position);

                // 隐藏或移除玩家对象
                playerObj.SetActive(false);
            }

            Debug.Log($"Player {eventData.PlayerId} defeated");
        }

        private void OnBattleEnded(BattleEndedEvent eventData)
        {
            Debug.Log($"Battle ended, winner: {eventData.WinnerTeam}");

            // 清理场景
            StartCoroutine(CleanupBattleScene());
        }

        /// <summary>
        /// 清理战斗场景
        /// </summary>
        private System.Collections.IEnumerator CleanupBattleScene()
        {
            yield return new WaitForSeconds(3f); // 等待3秒显示结果

            // 重置所有玩家对象
            foreach (var kvp in _battlePlayers)
            {
                if (kvp.Value != null && kvp.Key != currentPlayerId)
                {
                    Destroy(kvp.Value);
                }
                else if (kvp.Value != null)
                {
                    kvp.Value.SetActive(true); // 重新激活本地玩家
                }
            }

            // 清理字典，但保留本地玩家
            var localPlayer = _battlePlayers.ContainsKey(currentPlayerId) ? _battlePlayers[currentPlayerId] : null;
            _battlePlayers.Clear();
            if (localPlayer != null)
            {
                _battlePlayers[currentPlayerId] = localPlayer;
            }
        }

        #endregion

        #region 输入处理（简单实现）

        private void Update()
        {
            // 简单的点击移动实现
            if (Input.GetMouseButtonDown(0) && _battleManager != null && _battleManager.IsInBattle)
            {
                Ray ray = battleCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    MovePlayerToPosition(hit.point);
                }
            }
        }

        #endregion

        private void OnDestroy()
        {
            // 清理事件监听器
            if (_battleManager != null)
            {
                _battleManager.OnBattleStateUpdated -= OnBattleStateUpdated;
                _battleManager.OnSkillUsed -= OnSkillUsed;
                _battleManager.OnDamageDealt -= OnDamageDealt;
                _battleManager.OnPlayerDefeated -= OnPlayerDefeated;
                _battleManager.OnBattleEnded -= OnBattleEnded;
            }
        }
    }

    /// <summary>
    /// 简单的相机跟随组件
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 offset = new Vector3(0, 5, -5);
        [SerializeField] private float smoothSpeed = 0.125f;

        private Transform target;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPosition = target.position + offset;
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            transform.position = smoothedPosition;

            transform.LookAt(target);
        }
    }
}
