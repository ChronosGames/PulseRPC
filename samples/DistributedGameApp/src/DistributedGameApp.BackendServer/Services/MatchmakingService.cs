using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Messages;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services;
using MatchmakingRequest = DistributedGameApp.Shared.Domain.Matchmaking.MatchmakingRequest;
using MatchmakingResponse = DistributedGameApp.Shared.Domain.Matchmaking.MatchmakingResponse;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 匹配服务 - 处理玩家匹配队列
/// </summary>
/// <remarks>
/// <para><strong>设计模式</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 UnifiedPulseServiceBase，获得消息队列保证的单线程顺序执行</description></item>
/// <item><description>全局单例，自动启动</description></item>
/// <item><description>使用普通 Dictionary（消息队列保证线程安全）</description></item>
/// <item><description>使用 Timer 实现定时匹配任务</description></item>
/// </list>
/// </remarks>
[PulseService(
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.ProcessSingleton,
    SchedulingMode = ServiceSchedulingMode.DedicatedQueue,
    DisplayName = "MatchmakingService",
    EnableHealthCheck = true)]
public class MatchmakingService : UnifiedPulseServiceBase
{
    // ✅ 使用普通 Dictionary（UnifiedPulseServiceBase 消息队列保证线程安全）
    private readonly Dictionary<string, MatchmakingTicket> _matchQueue = new();

    // 匹配类型配置：每种模式需要的玩家数
    private static readonly Dictionary<string, int> MatchTypePlayerCount = new()
    {
        { "OneVsOne", 2 },
        { "TwoVsTwo", 4 },
        { "ThreeVsThree", 6 },
        { "FiveVsFive", 10 }
    };

    // 等待时间阈值（秒）- 每超过这个阈值，等级范围放宽一倍
    private const int WaitTimeThreshold = 30;

    private readonly UnifiedServiceClientManager _serviceClientManager;

    // 统计指标
    private long _totalMatchRequests;
    private long _totalMatchesCreated;
    private long _totalCancellations;
    private readonly Dictionary<string, int> _matchTypeStats = new();

    // 定时器
    private Timer? _matchingTimer;

    public MatchmakingService(
        ILogger<MatchmakingService> logger,
        UnifiedServiceClientManager serviceClientManager)
        : base("MatchmakingService", "Global", logger)
    {
        _serviceClientManager = serviceClientManager;
    }

    /// <summary>
    /// 服务启动时启动后台匹配任务
    /// </summary>
    public override Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("MatchmakingService starting...");

        // ✅ 启动定时匹配任务（每秒执行一次）
        _matchingTimer = new Timer(
            callback: _ => _ = FindMatchesAsync(),
            state: null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromSeconds(1));

        Logger.LogInformation("匹配服务已启动，定时匹配任务已启动");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务停止时清理资源
    /// </summary>
    public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("MatchmakingService stopping...");

        // 停止定时器
        _matchingTimer?.Dispose();
        _matchingTimer = null;

        // 清理匹配队列
        _matchQueue.Clear();

        Logger.LogInformation("匹配服务已停止");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 健康检查
    /// </summary>
    public override Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var queueSize = _matchQueue.Count;
        var message = $"Queue: {queueSize}, Matches: {_totalMatchesCreated}, Requests: {_totalMatchRequests}";

        Logger.LogDebug("MatchmakingService health check: {Message}", message);

        return Task.FromResult(ServiceHealthCheckResult.Healthy(message));
    }

    /// <summary>
    /// 开始匹配
    /// </summary>
    /// <remarks>
    /// ✅ UnifiedPulseServiceBase 消息队列保证单线程顺序执行，无需加锁
    /// </remarks>
    public Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request)
    {
        try
        {
            // ✅ 检查是否已在匹配队列中（无需担心并发，消息队列保证顺序执行）
            if (_matchQueue.ContainsKey(request.PlayerId))
            {
                Logger.LogWarning("玩家已在匹配队列中: {PlayerId}", request.PlayerId);
                return Task.FromResult(new MatchmakingResponse
                {
                    Success = false,
                    Message = "已在匹配队列中"
                });
            }

            // 获取来源节点信息（如果有的话）
            var connection = PulseRPC.Server.RequestContext.Current;
            var sourceNodeId = connection?.Id ?? "unknown";

            // 创建匹配票据
            var ticket = new MatchmakingTicket
            {
                TicketId = Guid.NewGuid().ToString(),
                PlayerId = request.PlayerId,
                CharacterId = request.CharacterId,
                MatchType = request.MatchType,
                LevelRange = request.LevelRange,
                Level = request.Level,
                StartTime = DateTime.UtcNow,
                SourceGameServerNodeId = sourceNodeId
            };

            // ✅ 直接 Add（无需 TryAdd，消息队列保证线程安全）
            _matchQueue.Add(request.PlayerId, ticket);

            // 更新统计
            _totalMatchRequests++;

            Logger.LogInformation("玩家开始匹配: {PlayerId} - Ticket: {TicketId}, MatchType: {MatchType}, SourceNode: {SourceNode}",
                request.PlayerId, ticket.TicketId, request.MatchType, sourceNodeId);

            return Task.FromResult(new MatchmakingResponse
            {
                Success = true,
                MatchId = ticket.TicketId,
                EstimatedWaitSeconds = EstimateWaitTime(),
                Message = "已加入匹配队列"
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "开始匹配失败: {PlayerId}", request.PlayerId);
            return Task.FromResult(new MatchmakingResponse
            {
                Success = false,
                Message = $"匹配失败: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public Task<bool> CancelMatchmakingAsync(string playerId)
    {
        try
        {
            // ✅ 直接 Remove（消息队列保证线程安全）
            var removed = _matchQueue.Remove(playerId, out var ticket);

            if (removed && ticket != null)
            {
                // 更新统计
                _totalCancellations++;

                Logger.LogInformation("玩家取消匹配: {PlayerId} - Ticket: {TicketId}",
                    playerId, ticket.TicketId);
            }
            else
            {
                Logger.LogWarning("玩家不在匹配队列中: {PlayerId}", playerId);
            }

            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "取消匹配失败: {PlayerId}", playerId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 获取队列中的玩家数量
    /// </summary>
    public int GetQueueSize()
    {
        return _matchQueue.Count;
    }

    /// <summary>
    /// 查找匹配（定时任务调用）
    /// </summary>
    /// <remarks>
    /// ✅ 此方法由 Timer 定时调用，消息队列保证顺序执行
    ///
    /// 匹配算法流程：
    /// 1. 按匹配类型分组
    /// 2. 对每组按等待时间排序（等待久的优先）
    /// 3. 按等级范围筛选（随等待时间放宽）
    /// 4. 创建匹配组
    /// 5. 通知玩家匹配成功
    /// </remarks>
    private Task FindMatchesAsync()
    {
        try
        {
            if (_matchQueue.Count == 0)
            {
                return Task.CompletedTask;
            }

            Logger.LogDebug("查找匹配，队列大小: {QueueSize}", _matchQueue.Count);

            // 1. 按匹配类型分组
            var groupedByType = _matchQueue.Values
                .GroupBy(t => t.MatchType)
                .ToList();

            foreach (var group in groupedByType)
            {
                var matchType = group.Key;

                // 获取该匹配类型需要的玩家数
                if (!MatchTypePlayerCount.TryGetValue(matchType, out var requiredPlayers))
                {
                    Logger.LogWarning("未知的匹配类型: {MatchType}", matchType);
                    continue;
                }

                // 2. 按等待时间排序（等待越久优先级越高）
                var sortedTickets = group
                    .OrderBy(t => t.StartTime)
                    .ToList();

                // 如果玩家数不足，跳过
                if (sortedTickets.Count < requiredPlayers)
                {
                    Logger.LogDebug("匹配类型 {MatchType} 玩家数不足: {CurrentCount}/{RequiredCount}",
                        matchType, sortedTickets.Count, requiredPlayers);
                    continue;
                }

                // 3. 尝试创建匹配组
                TryCreateMatches(matchType, sortedTickets, requiredPlayers);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "查找匹配失败");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 尝试创建匹配组
    /// </summary>
    private void TryCreateMatches(string matchType, List<MatchmakingTicket> tickets, int requiredPlayers)
    {
        var now = DateTime.UtcNow;
        var matched = new List<MatchmakingTicket>();

        // 从等待时间最长的玩家开始匹配
        for (int i = 0; i < tickets.Count && matched.Count < requiredPlayers; i++)
        {
            var ticket = tickets[i];

            // 如果已经匹配过，跳过
            if (matched.Contains(ticket))
            {
                continue;
            }

            // 第一个玩家直接加入
            if (matched.Count == 0)
            {
                matched.Add(ticket);
                continue;
            }

            // 检查等级是否匹配
            if (IsLevelMatch(matched[0], ticket, now))
            {
                matched.Add(ticket);
            }

            // 如果凑齐了所需人数，创建匹配
            if (matched.Count != requiredPlayers)
            {
                continue;
            }

            // ✅ 传入列表副本，因为 CreateMatchGroup 是 async void，会异步执行
            _ = CreateMatchGroupAsync(matchType, matched.ToList());
            matched.Clear();

            // 如果剩余玩家还够一组，继续匹配
            if (tickets.Count - i - 1 < requiredPlayers)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 检查两个玩家的等级是否匹配
    /// </summary>
    /// <remarks>
    /// 随着等待时间增加，等级范围会逐步放宽
    /// </remarks>
    private bool IsLevelMatch(MatchmakingTicket ticket1, MatchmakingTicket ticket2, DateTime now)
    {
        // 计算两个玩家的最大等待时间
        var maxWaitTime = Math.Max(
            (now - ticket1.StartTime).TotalSeconds,
            (now - ticket2.StartTime).TotalSeconds
        );

        // 基础等级范围（取两者中较宽松的）
        var baseLevelRange = Math.Max(ticket1.LevelRange, ticket2.LevelRange);

        // 根据等待时间放宽等级范围：每等待 WaitTimeThreshold 秒，范围翻倍
        var waitTimeMultiplier = 1 + (int)(maxWaitTime / WaitTimeThreshold);
        var effectiveLevelRange = baseLevelRange * waitTimeMultiplier;

        // 检查等级差是否在范围内
        var levelDiff = Math.Abs(ticket1.Level - ticket2.Level);

        Logger.LogDebug(
            "等级匹配检查: Player1(Lv{Level1}) vs Player2(Lv{Level2}), " +
            "等待时间: {WaitTime}s, 有效范围: ±{Range}, 等级差: {Diff}, 匹配: {Match}",
            ticket1.Level, ticket2.Level, (int)maxWaitTime, effectiveLevelRange, levelDiff, levelDiff <= effectiveLevelRange);

        return levelDiff <= effectiveLevelRange;
    }

    /// <summary>
    /// 创建匹配组并通知玩家
    /// </summary>
    private async Task CreateMatchGroupAsync(string matchType, List<MatchmakingTicket> matchedTickets)
    {
        var matchId = Guid.NewGuid().ToString();

        // 更新统计
        _totalMatchesCreated++;
        _matchTypeStats.TryAdd(matchType, 0);
        _matchTypeStats[matchType]++;

        Logger.LogInformation(
            "匹配成功 - MatchId: {MatchId}, MatchType: {MatchType}, Players: [{Players}]",
            matchId,
            matchType,
            string.Join(", ", matchedTickets.Select(t => $"{t.PlayerId}(Lv{t.Level})")));

        // 移除已匹配的玩家
        foreach (var ticket in matchedTickets)
        {
            _matchQueue.Remove(ticket.PlayerId);
        }

        try
        {
            // 调用 BattleServer 创建战斗房间
            var battleHub = _serviceClientManager.GetHub<IBattleHub>(matchId);
            if (battleHub == null)
            {
                Logger.LogError("无法获取 BattleHub - MatchId: {MatchId}", matchId);
                // 重新加入匹配队列
                foreach (var ticket in matchedTickets) _matchQueue[ticket.PlayerId] = ticket;
                return;
            }

            var createRoomRequest = new CreateBattleRoomRequest
            {
                MatchId = matchId,
                MatchType = matchType,
                PlayerIds = matchedTickets.Select(t => t.PlayerId).ToList()
            };

            var roomResponse = await battleHub.CreateBattleRoomAsync(createRoomRequest);
            if (!roomResponse.Success)
            {
                Logger.LogError("创建战斗房间失败 - MatchId: {MatchId}, Error: {Error}",
                    matchId, roomResponse.Message);
                // 重新加入匹配队列
                foreach (var ticket in matchedTickets) _matchQueue[ticket.PlayerId] = ticket;
                return;
            }

            Logger.LogInformation("战斗房间创建成功 - RoomId: {RoomId}, Server: {Host}:{Port}",
                roomResponse.RoomId, roomResponse.ServerHost, roomResponse.ServerPort);

            // 构造匹配成功通知
            var notification = BuildMatchFoundNotification(
                matchId, matchType, matchedTickets,
                roomResponse.ServerHost,
                roomResponse.ServerPort,
                roomResponse.AccessToken);

            // ✅ 回调通知 GameServer
            await NotifyGameServersAsync(matchedTickets, notification);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "创建战斗房间或通知失败 - MatchId: {MatchId}", matchId);
        }
    }


    /// <summary>
    /// 构造匹配成功通知
    /// </summary>
    private DistributedGameApp.Shared.Domain.Matchmaking.MatchFoundNotification BuildMatchFoundNotification(
        string matchId,
        string matchType,
        List<MatchmakingTicket> tickets,
        string battleServerHost,
        int battleServerPort,
        string accessToken)
    {
        // 根据匹配类型分配队伍
        var playersPerTeam = MatchTypePlayerCount[matchType] / 2;
        var teammates = new List<MatchPlayer>();
        var opponents = new List<MatchPlayer>();

        for (int i = 0; i < tickets.Count; i++)
        {
            var ticket = tickets[i];
            var player = new MatchPlayer
            {
                PlayerId = ticket.PlayerId,
                CharacterName = ticket.CharacterId, // TODO: 从数据库获取真实的角色名
                Level = ticket.Level,
                Class = "Unknown" // TODO: 从数据库获取真实的职业
            };

            if (i < playersPerTeam)
            {
                teammates.Add(player);
            }
            else
            {
                opponents.Add(player);
            }
        }

        return new DistributedGameApp.Shared.Domain.Matchmaking.MatchFoundNotification
        {
            MatchId = matchId,
            BattleRoomId = matchId,
            BattleServerHost = battleServerHost,
            BattleServerPort = battleServerPort,
            AccessToken = accessToken,
            Teammates = teammates,
            Opponents = opponents
        };
    }

    /// <summary>
    /// 通知 GameServer 匹配结果（回调）
    /// </summary>
    private async Task NotifyGameServersAsync(
        List<MatchmakingTicket> matchedTickets,
        DistributedGameApp.Shared.Domain.Matchmaking.MatchFoundNotification notification)
    {
        Logger.LogInformation("开始通知 GameServer，玩家数: {Count}", matchedTickets.Count);

        // 当前 Demo 场景只有一个 GameServer 实例，
        // 且 BackendServer 通过 Consul 以服务类型 (GameServer) 发现节点，
        // 因此这里不再按 SourceGameServerNodeId 精确路由，统一通过服务类型路由即可。
        try
        {
            // ✅ 方案2: 使用重试机制获取 Hub 代理（应对瞬时网络故障）
            var gameServerHub = await GetGameServerHubWithRetryAsync();

            if (gameServerHub == null)
            {
                Logger.LogError("无法获取 GameServerInternalHub 代理（重试后仍失败）");
                return;
            }

            // 逐个通知所有玩家（当前所有玩家都在同一个 GameServer 上）
            foreach (var ticket in matchedTickets)
            {
                try
                {
                    // ✅ 直接使用多参数调用（源生成器已支持）
                    var success = await gameServerHub.OnMatchFoundAsync(ticket.PlayerId, notification);

                    if (success)
                    {
                        Logger.LogInformation(
                            "匹配通知已发送 - PlayerId: {PlayerId}",
                            ticket.PlayerId);
                    }
                    else
                    {
                        Logger.LogWarning(
                            "匹配通知发送失败 - PlayerId: {PlayerId}",
                            ticket.PlayerId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "匹配通知异常 - PlayerId: {PlayerId}",
                        ticket.PlayerId);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取 GameServerInternalHub 失败（自动路由）");
        }
    }

    /// <summary>
    /// 带重试机制获取 GameServerInternalHub 代理
    /// </summary>
    /// <param name="maxRetries">最大重试次数（默认3次）</param>
    /// <param name="delayMs">重试延迟（默认500ms）</param>
    /// <returns>Hub 代理实例，如果重试后仍失败则返回 null</returns>
    private async Task<IGameServerInternalHub?> GetGameServerHubWithRetryAsync(int maxRetries = 3, int delayMs = 500)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            Logger.LogInformation("正在获取 GameServerInternalHub 代理（尝试 {Attempt}/{MaxRetries}）", i + 1, maxRetries);

            // 不指定 serviceId，交给 UnifiedServiceClientManager 按服务类型自动路由
            var hub = _serviceClientManager.GetHub<IGameServerInternalHub>();

            if (hub != null)
            {
                Logger.LogInformation("成功获取 GameServerInternalHub 代理（第 {Attempt} 次尝试）", i + 1);
                return hub;
            }

            if (i < maxRetries - 1)
            {
                Logger.LogWarning("获取 GameServerInternalHub 失败，将在 {DelayMs}ms 后重试 ({Retry}/{MaxRetries})",
                    delayMs, i + 1, maxRetries);
                await Task.Delay(delayMs);
            }
        }

        Logger.LogError("获取 GameServerInternalHub 失败（重试 {MaxRetries} 次后仍失败）", maxRetries);
        return null;
    }

    /// <summary>
    /// 估算等待时间
    /// </summary>
    private int EstimateWaitTime()
    {
        // 简化实现：根据队列大小估算
        var queueSize = _matchQueue.Count;
        return queueSize switch
        {
            0 => 60, // 1分钟
            < 5 => 30, // 30秒
            < 10 => 15, // 15秒
            _ => 5 // 5秒
        };
    }

    /// <summary>
    /// 获取匹配统计信息
    /// </summary>
    public Task<MatchmakingStats> GetStatsAsync()
    {
        var stats = new MatchmakingStats
        {
            CurrentQueueSize = _matchQueue.Count,
            TotalMatchRequests = _totalMatchRequests,
            TotalMatchesCreated = _totalMatchesCreated,
            TotalCancellations = _totalCancellations,
            MatchTypeBreakdown = new Dictionary<string, int>(_matchTypeStats),
            MatchSuccessRate = _totalMatchRequests > 0
                ? (double)(_totalMatchesCreated * 2) / _totalMatchRequests * 100
                : 0
        };

        return Task.FromResult(stats);
    }
}

/// <summary>
/// 匹配票据（内部使用）
/// </summary>
public class MatchmakingTicket
{
    public string TicketId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string MatchType { get; set; } = string.Empty;
    public int LevelRange { get; set; }
    public int Level { get; set; }
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 来源 GameServer 节点ID（用于回调通知）
    /// </summary>
    public string? SourceGameServerNodeId { get; set; }
}

/// <summary>
/// 匹配统计信息
/// </summary>
public class MatchmakingStats
{
    /// <summary>
    /// 当前队列大小
    /// </summary>
    public int CurrentQueueSize { get; set; }

    /// <summary>
    /// 总匹配请求数
    /// </summary>
    public long TotalMatchRequests { get; set; }

    /// <summary>
    /// 总匹配成功数
    /// </summary>
    public long TotalMatchesCreated { get; set; }

    /// <summary>
    /// 总取消数
    /// </summary>
    public long TotalCancellations { get; set; }

    /// <summary>
    /// 各匹配类型的统计
    /// </summary>
    public Dictionary<string, int> MatchTypeBreakdown { get; set; } = new();

    /// <summary>
    /// 匹配成功率（百分比）
    /// </summary>
    public double MatchSuccessRate { get; set; }
}
