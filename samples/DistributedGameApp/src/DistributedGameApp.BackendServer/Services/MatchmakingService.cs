using DistributedGameApp.Shared.Domain.Matchmaking;
using DistributedGameApp.Shared.Messages;
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

    public MatchmakingService(
        ILogger<MatchmakingService> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
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
                StartTime = DateTime.UtcNow
            };

            // ✅ 直接 Add（无需 TryAdd，BaseService 保证线程安全）
            _matchQueue.Add(request.PlayerId, ticket);

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

            // TODO: 实现实际的匹配算法
            // 1. 按匹配类型分组
            // 2. 按等级范围筛选
            // 3. 按等待时间排序
            // 4. 创建匹配组
            // 5. 通知玩家匹配成功

            // 简化实现：每2个玩家匹配成功
            var tickets = _matchQueue.Values.ToList();
            if (tickets.Count >= 2)
            {
                var player1 = tickets[0];
                var player2 = tickets[1];

                Logger.LogInformation("匹配成功: {Player1} vs {Player2}",
                    player1.PlayerId, player2.PlayerId);

                // 移除已匹配的玩家
                _matchQueue.Remove(player1.PlayerId);
                _matchQueue.Remove(player2.PlayerId);

                // TODO: 创建战斗房间并通知玩家
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "查找匹配失败");
        }

        return Task.CompletedTask;
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
    public DateTime StartTime { get; set; }
}
