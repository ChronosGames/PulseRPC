using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Messages;
using DistributedGameApp.Shared.Hubs;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using MatchmakingRequest = DistributedGameApp.Shared.Domain.Matchmaking.MatchmakingRequest;
using MatchmakingResponse = DistributedGameApp.Shared.Domain.Matchmaking.MatchmakingResponse;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 匹配服务 - 处理玩家匹配队列（基于 BaseService 架构）
/// </summary>
/// <remarks>
/// <para><strong>改进点</strong>:</para>
/// <list type="bullet">
/// <item><description>继承 BaseService，获得 Actor 模型保证的单线程顺序执行</description></item>
/// <item><description>实现 IPulseService，支持全局单例</description></item>
/// <item><description>移除 ConcurrentDictionary，使用普通 Dictionary（BaseService 保证线程安全）</description></item>
/// <item><description>添加定时匹配任务（使用 ScheduleRecurring）</description></item>
/// <item><description>获得监控指标和灾难隔离能力</description></item>
/// </list>
/// </remarks>
public class MatchmakingService : BaseService, IPulseService
{
    // ServiceId 用于标识全局单例
    public string ServiceName => "Matchmaking";
    public string ServiceId => "Matchmaking:Global";

    // ✅ 使用普通 Dictionary（BaseService 保证单线程，无需 ConcurrentDictionary）
    private readonly Dictionary<string, MatchmakingTicket> _matchQueue = new();

    // 匹配类型配置：每种模式需要的玩家数
    private static readonly Dictionary<string, int> MatchTypePlayerCount = new()
    {
        { "1v1", 2 },
        { "2v2", 4 },
        { "3v3", 6 },
        { "5v5", 10 }
    };

    // 等待时间阈值（秒）- 每超过这个阈值，等级范围放宽一倍
    private const int WaitTimeThreshold = 30;

    private readonly ClientNotificationService _notificationService;
    private readonly UnifiedServiceClientManager _serviceClientManager;

    // 统计指标
    private long _totalMatchRequests;
    private long _totalMatchesCreated;
    private long _totalCancellations;
    private readonly Dictionary<string, int> _matchTypeStats = new();

    public MatchmakingService(
        ILogger<MatchmakingService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator,
        ClientNotificationService notificationService,
        UnifiedServiceClientManager serviceClientManager)
        : base(logger, authenticationService, permissionValidator)
    {
        _notificationService = notificationService;
        _serviceClientManager = serviceClientManager;
    }

    /// <summary>
    /// 服务启动时启动后台匹配任务
    /// </summary>
    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        await base.OnStartAsync(cancellationToken);

        // ✅ 启动定时匹配任务（每秒执行一次）
        ScheduleRecurring(TimeSpan.Zero, TimeSpan.FromSeconds(1), FindMatchesAsync);

        Logger.LogInformation("匹配服务已启动，定时匹配任务已启动");
    }

    /// <summary>
    /// 开始匹配
    /// </summary>
    /// <remarks>
    /// ✅ BaseService 保证单线程顺序执行，无需加锁
    /// </remarks>
    public Task<MatchmakingResponse> StartMatchmakingAsync(MatchmakingRequest request)
    {
        try
        {
            // ✅ 检查是否已在匹配队列中（无需担心并发，BaseService 保证顺序执行）
            if (_matchQueue.ContainsKey(request.PlayerId))
            {
                Logger.LogWarning("玩家已在匹配队列中: {PlayerId}", request.PlayerId);
                return Task.FromResult(new MatchmakingResponse
                {
                    Success = false,
                    Message = "已在匹配队列中"
                });
            }

            // 创建匹配票据
            var ticket = new MatchmakingTicket
            {
                TicketId = Guid.NewGuid().ToString(),
                PlayerId = request.PlayerId,
                CharacterId = request.CharacterId,
                MatchType = request.MatchType,
                LevelRange = request.LevelRange,
                Level = request.Level,
                StartTime = DateTime.UtcNow
            };

            // ✅ 直接 Add（无需 TryAdd，BaseService 保证线程安全）
            _matchQueue.Add(request.PlayerId, ticket);

            // 更新统计
            _totalMatchRequests++;

            Logger.LogInformation("玩家开始匹配: {PlayerId} - Ticket: {TicketId}, MatchType: {MatchType}",
                request.PlayerId, ticket.TicketId, request.MatchType);

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
            // ✅ 直接 Remove（BaseService 保证线程安全）
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
    /// ✅ 此方法由 ScheduleRecurring 定时调用，BaseService 保证顺序执行
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

            CreateMatchGroup(matchType, matched);
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
    private async void CreateMatchGroup(string matchType, List<MatchmakingTicket> matchedTickets)
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

            // 构造匹配成功通知（使用 BattleServer 返回的真实地址）
            var notification = BuildMatchFoundNotification(
                matchId, matchType, matchedTickets,
                roomResponse.ServerHost,
                roomResponse.ServerPort,
                roomResponse.AccessToken);

            // 通知所有匹配成功的玩家
            var playerIds = matchedTickets.Select(t => t.PlayerId).ToList();
            var successCount = await _notificationService.NotifyMatchFoundAsync(playerIds, notification);

            Logger.LogInformation(
                "匹配通知发送完成 - MatchId: {MatchId}, Total: {Total}, Success: {Success}",
                matchId, playerIds.Count, successCount);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送匹配通知失败 - MatchId: {MatchId}", matchId);
        }
    }

    /// <summary>
    /// 构造匹配成功通知
    /// </summary>
    private DistributedGameApp.Shared.Domain.Matchmaking.MatchFoundNotification BuildMatchFoundNotification(
        string matchId,
        string matchType,
        List<MatchmakingTicket> tickets,
        string battleServerHost = "localhost",
        int battleServerPort = 8082,
        string accessToken = "")
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
