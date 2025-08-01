using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using PulseRPC.Client;
using GameApp.Shared.Services;

namespace GameApp.Unity.Network
{
    /// <summary>
    /// 战斗客户端 - 负责与 BattleServer 的 PulseRPC 通信
    /// </summary>
    public class BattleClient
    {
        private IPulseClient _pulseClient;
        private IBattleService _battleService;
        private ISkillService _skillService;

        // 事件监听器
        private BattleEventsImpl _battleEvents;

        public bool IsConnected => _pulseClient?.IsConnected ?? false;
        public string CurrentBattleId { get; private set; } = string.Empty;
        public BattleInfo? CurrentBattle { get; private set; }

        // 连接事件
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnConnectionError;

        // 战斗事件
        public event Action<BattleStateUpdateEvent> OnBattleStateUpdated;
        public event Action<SkillUsedEvent> OnSkillUsed;
        public event Action<DamageDealtEvent> OnDamageDealt;
        public event Action<PlayerDefeatedEvent> OnPlayerDefeated;
        public event Action<BattleEndedEvent> OnBattleEnded;

        /// <summary>
        /// 初始化战斗客户端
        /// </summary>
        public async Task<bool> InitializeAsync(string serverAddress, int tcpPort, int kcpPort)
        {
            try
            {
                Debug.Log($"Initializing BattleClient: {serverAddress}:{tcpPort}/{kcpPort}");

                // 创建 PulseRPC 客户端，优先使用KCP进行低延迟战斗
                _pulseClient = PulseClientBuilder.Create()
                    .AddKcp("KcpChannel", serverAddress, kcpPort)  // 主要用于实时战斗
                    .AddTcp("TcpChannel", serverAddress, tcpPort)  // 用于可靠传输
                    .Build();

                // 连接到服务器
                await _pulseClient.ConnectAsync();

                // 获取服务代理
                _battleService = await _pulseClient.GetServiceAsync<IBattleService>();
                _skillService = await _pulseClient.GetServiceAsync<ISkillService>();

                // 注册战斗事件监听器
                _battleEvents = new BattleEventsImpl();
                _battleEvents.BattleStateUpdated += OnBattleStateUpdate;
                _battleEvents.SkillUsed += OnSkillUse;
                _battleEvents.DamageDealt += OnDamageDealt;
                _battleEvents.PlayerDefeated += OnPlayerDefeat;
                _battleEvents.BattleEnded += OnBattleEnd;

                await _pulseClient.RegisterEventListenerAsync<IBattleEvents>(_battleEvents);

                Debug.Log("BattleClient initialized successfully");
                OnConnected?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"BattleClient initialization failed: {ex.Message}");
                OnConnectionError?.Invoke(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 加入战斗
        /// </summary>
        public async Task<JoinBattleResponse> JoinBattleAsync(int playerId, string battleType, string? roomId = null, Dictionary<string, object>? parameters = null)
        {
            try
            {
                if (_battleService == null)
                {
                    throw new InvalidOperationException("BattleClient not initialized");
                }

                var request = new JoinBattleRequest
                {
                    PlayerId = playerId,
                    BattleType = battleType,
                    RoomId = roomId,
                    Parameters = parameters ?? new Dictionary<string, object>()
                };

                var response = await _battleService.JoinBattleAsync(request);

                if (response.Success)
                {
                    CurrentBattleId = response.BattleInfo?.BattleId ?? "";
                    CurrentBattle = response.BattleInfo;
                    Debug.Log($"Join battle successful: {CurrentBattleId}");
                }
                else
                {
                    Debug.LogWarning($"Join battle failed: {response.Message}");
                }

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Join battle error: {ex.Message}");
                return new JoinBattleResponse
                {
                    Success = false,
                    Message = $"加入战斗失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 离开战斗
        /// </summary>
        public async Task<bool> LeaveBattleAsync(int playerId)
        {
            try
            {
                if (_battleService == null || string.IsNullOrEmpty(CurrentBattleId))
                {
                    return false;
                }

                var request = new LeaveBattleRequest
                {
                    PlayerId = playerId,
                    BattleId = CurrentBattleId
                };

                await _battleService.LeaveBattleAsync(request);

                CurrentBattleId = string.Empty;
                CurrentBattle = null;

                Debug.Log("Left battle successfully");
                return true;
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
        public async Task<SkillResult> UseSkillAsync(int playerId, int skillId, Vector3 targetPosition, int? targetPlayerId = null)
        {
            try
            {
                if (_battleService == null)
                {
                    throw new InvalidOperationException("BattleClient not initialized");
                }

                var request = new UseSkillRequest
                {
                    BattleId = CurrentBattleId,
                    PlayerId = playerId,
                    SkillId = skillId,
                    TargetPosition = new Position3D
                    {
                        X = targetPosition.x,
                        Y = targetPosition.y,
                        Z = targetPosition.z
                    },
                    TargetPlayerId = targetPlayerId
                };

                var result = await _battleService.UseSkillAsync(request);

                if (result.Success)
                {
                    Debug.Log($"Skill used successfully: {skillId}");
                }
                else
                {
                    Debug.LogWarning($"Skill use failed: {result.Message}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Use skill error: {ex.Message}");
                return new SkillResult
                {
                    Success = false,
                    Message = $"技能使用失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 移动到战斗位置
        /// </summary>
        public async Task<bool> MoveToBattlePositionAsync(int playerId, Vector3 position)
        {
            try
            {
                if (_battleService == null)
                {
                    throw new InvalidOperationException("BattleClient not initialized");
                }

                var request = new MoveBattlePositionRequest
                {
                    BattleId = CurrentBattleId,
                    PlayerId = playerId,
                    Position = new Position3D
                    {
                        X = position.x,
                        Y = position.y,
                        Z = position.z
                    }
                };

                await _battleService.MoveBattlePositionAsync(request);
                Debug.Log($"Moved to position: {position}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Move to battle position error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取战斗信息
        /// </summary>
        public async Task<BattleInfo?> GetBattleInfoAsync(string battleId)
        {
            try
            {
                if (_battleService == null)
                {
                    return null;
                }

                var request = new GetBattleInfoRequest
                {
                    BattleId = battleId
                };

                return await _battleService.GetBattleInfoAsync(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get battle info error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 学习技能
        /// </summary>
        public async Task<LearnSkillResponse> LearnSkillAsync(int playerId, int skillId)
        {
            try
            {
                if (_skillService == null)
                {
                    throw new InvalidOperationException("BattleClient not initialized");
                }

                var request = new LearnSkillRequest
                {
                    PlayerId = playerId,
                    SkillId = skillId
                };

                var response = await _skillService.LearnSkillAsync(request);

                if (response.Success)
                {
                    Debug.Log($"Skill learned successfully: {skillId}");
                }
                else
                {
                    Debug.LogWarning($"Learn skill failed: {response.Message}");
                }

                return response;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Learn skill error: {ex.Message}");
                return new LearnSkillResponse
                {
                    Success = false,
                    Message = $"学习技能失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取玩家技能列表
        /// </summary>
        public async Task<PlayerSkillsResponse> GetPlayerSkillsAsync(int playerId)
        {
            try
            {
                if (_skillService == null)
                {
                    throw new InvalidOperationException("BattleClient not initialized");
                }

                var request = new GetPlayerSkillsRequest
                {
                    PlayerId = playerId
                };

                return await _skillService.GetPlayerSkillsAsync(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Get player skills error: {ex.Message}");
                return new PlayerSkillsResponse
                {
                    Success = false,
                    Message = $"获取技能列表失败: {ex.Message}",
                    Skills = new List<PlayerSkill>()
                };
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (_pulseClient != null && IsConnected)
                {
                    await _pulseClient.DisconnectAsync();
                    Debug.Log("BattleClient disconnected");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Disconnect error: {ex.Message}");
            }
            finally
            {
                _pulseClient = null;
                _battleService = null;
                _skillService = null;
                CurrentBattleId = string.Empty;
                CurrentBattle = null;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _ = DisconnectAsync();
        }

        #region 私有事件处理

        private void OnBattleStateUpdate(BattleStateUpdateEvent eventData)
        {
            CurrentBattle = eventData.BattleInfo;
            OnBattleStateUpdated?.Invoke(eventData);
        }

        private void OnSkillUse(SkillUsedEvent eventData)
        {
            OnSkillUsed?.Invoke(eventData);
        }

        private void OnDamageDealt(DamageDealtEvent eventData)
        {
            OnDamageDealt?.Invoke(eventData);
        }

        private void OnPlayerDefeat(PlayerDefeatedEvent eventData)
        {
            OnPlayerDefeated?.Invoke(eventData);
        }

        private void OnBattleEnd(BattleEndedEvent eventData)
        {
            CurrentBattleId = string.Empty;
            CurrentBattle = null;
            OnBattleEnded?.Invoke(eventData);
        }

        #endregion
    }
}
