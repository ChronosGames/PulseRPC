using DistributedGameApp.BackendServer.Repositories;
using DistributedGameApp.Shared.Domain.Guilds;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;

namespace DistributedGameApp.BackendServer.Services;

/// <summary>
/// 公会服务 - 核心业务逻辑
/// </summary>
/// <remarks>
/// <para><strong>设计原则</strong>:</para>
/// <list type="bullet">
/// <item><description>✅ 继承 UnifiedPulseServiceBase - 统一的服务基类</description></item>
/// <item><description>✅ 使用专属消息队列保证线程安全</description></item>
/// <item><description>✅ 全局单例 - 所有玩家共享同一个服务实例</description></item>
/// <item><description>✅ IO密集型服务 - 所有操作直接访问数据库</description></item>
/// <item><description>✅ 支持生命周期管理和健康检查</description></item>
/// </list>
/// </remarks>
[PulseService(
    Scenario = ServiceScenario.Actor,  // 单线程顺序执行，保证线程安全
    StartupType = ServiceStartupType.AutoStart,
    InstanceScope = ServiceInstanceScope.Singleton,
    DisplayName = "GuildService",
    EnableHealthCheck = true)]
public class GuildService : UnifiedPulseServiceBase
{
    private readonly GuildRepository _repository;

    public GuildService(
        ILogger<GuildService> logger,
        GuildRepository repository)
        : base("GuildService", "Global", logger)
    {
        _repository = repository;
    }

    public override Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("GuildService starting...");
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("GuildService stopping...");
        return Task.CompletedTask;
    }

    public override Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ServiceHealthCheckResult.Healthy("GuildService is healthy"));
    }

    // ========== 公会管理 ==========

    public async Task<CreateGuildResponse> CreateGuildAsync(string userId, string username, CreateGuildRequest request)
    {
        try
        {
            // 验证用户是否已经在公会中
            var existingMember = await _repository.GetMemberAsync(userId);
            if (existingMember != null)
            {
                return new CreateGuildResponse
                {
                    Success = false,
                    Message = "您已经在一个公会中"
                };
            }

            // 检查公会名称是否已存在
            var existingGuild = await _repository.GetGuildByNameAsync(request.Name);
            if (existingGuild != null)
            {
                return new CreateGuildResponse
                {
                    Success = false,
                    Message = "公会名称已被使用"
                };
            }

            // 创建公会
            var guildId = Guid.NewGuid().ToString("N");
            var guild = new Guild
            {
                GuildId = guildId,
                Name = request.Name,
                Tag = request.Tag,
                Description = request.Description,
                LeaderId = userId,
                Level = 1,
                Exp = 0,
                MaxMembers = 50,
                MemberCount = 1,
                CreatedAt = DateTime.UtcNow
            };

            await _repository.CreateGuildAsync(guild);

            // 创建会长成员记录
            var leaderMember = new GuildMember
            {
                GuildId = guildId,
                UserId = userId,
                Username = username,
                Role = "Leader",
                Contribution = 0,
                JoinedAt = DateTime.UtcNow,
                LastOnlineAt = DateTime.UtcNow
            };

            await _repository.AddMemberAsync(leaderMember);

            // 记录活动
            await AddActivityAsync(guildId, userId, username, "GuildCreated", $"{username} 创建了公会");

            Logger.LogInformation("公会创建成功: {GuildName} by {UserId}", guild.Name, userId);

            return new CreateGuildResponse
            {
                Success = true,
                Message = "公会创建成功",
                Guild = guild
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "创建公会失败: {UserId}", userId);
            return new CreateGuildResponse
            {
                Success = false,
                Message = "创建公会失败，请稍后重试"
            };
        }
    }

    public async Task<bool> DisbandGuildAsync(string userId, string guildId)
    {
        var guild = await _repository.GetGuildAsync(guildId);
        if (guild == null) return false;

        // 检查权限
        if (guild.LeaderId != userId)
        {
            Logger.LogWarning("非会长尝试解散公会: {UserId}, {GuildId}", userId, guildId);
            return false;
        }

        // 删除所有成员
        await _repository.RemoveAllMembersAsync(guildId);

        // 删除公会
        await _repository.DeleteGuildAsync(guildId);

        Logger.LogInformation("公会已解散: {GuildId} by {UserId}", guildId, userId);
        return true;
    }

    public async Task<Guild?> GetGuildAsync(string guildId)
    {
        return await _repository.GetGuildAsync(guildId);
    }

    public async Task<GuildListResponse> GetGuildsAsync(GetGuildListRequest request)
    {
        var skip = (request.Page - 1) * request.PageSize;
        var guilds = await _repository.GetGuildsAsync(skip, request.PageSize, request.SortBy, request.Descending);
        var totalCount = await _repository.GetGuildCountAsync();

        return new GuildListResponse
        {
            Guilds = guilds.ToArray(),
            TotalCount = (int)totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<Guild[]> SearchGuildsAsync(string keyword)
    {
        var guilds = await _repository.SearchGuildsAsync(keyword);
        return guilds.ToArray();
    }

    public async Task<bool> UpdateGuildInfoAsync(string userId, UpdateGuildInfoRequest request)
    {
        var guild = await _repository.GetGuildAsync(request.GuildId);
        if (guild == null) return false;

        // 检查权限
        if (guild.LeaderId != userId)
        {
            Logger.LogWarning("非会长尝试更新公会信息: {UserId}, {GuildId}", userId, request.GuildId);
            return false;
        }

        // 更新信息
        if (!string.IsNullOrEmpty(request.Name))
            guild.Name = request.Name;
        if (!string.IsNullOrEmpty(request.Tag))
            guild.Tag = request.Tag;
        if (!string.IsNullOrEmpty(request.Description))
            guild.Description = request.Description;

        return await _repository.UpdateGuildAsync(guild);
    }

    // ========== 成员管理 ==========

    public async Task<bool> ApplyToJoinAsync(string userId, string username, int level, string guildId, string message = "")
    {
        // 检查是否已在公会中
        var existingMember = await _repository.GetMemberAsync(userId);
        if (existingMember != null)
        {
            Logger.LogWarning("用户已在公会中: {UserId}", userId);
            return false;
        }

        // 检查公会是否存在
        var guild = await _repository.GetGuildAsync(guildId);
        if (guild == null)
        {
            Logger.LogWarning("公会不存在: {GuildId}", guildId);
            return false;
        }

        // 检查公会是否已满
        if (guild.MemberCount >= guild.MaxMembers)
        {
            Logger.LogWarning("公会已满: {GuildId}", guildId);
            return false;
        }

        // 检查是否已有待处理的申请
        var existingRequest = await _repository.GetJoinRequestAsync(guildId, userId);
        if (existingRequest != null)
        {
            Logger.LogWarning("已存在待处理的申请: {UserId}, {GuildId}", userId, guildId);
            return false;
        }

        // 创建加入申请
        var joinRequest = new JoinRequest
        {
            GuildId = guildId,
            UserId = userId,
            Username = username,
            Level = level,
            Message = message,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        await _repository.CreateJoinRequestAsync(joinRequest);

        Logger.LogInformation("加入申请已提交: {UserId} -> {GuildId}", userId, guildId);
        return true;
    }

    public async Task<bool> ApproveJoinRequestAsync(string operatorId, string guildId, string userId)
    {
        var guild = await _repository.GetGuildAsync(guildId);
        if (guild == null) return false;

        // 检查权限（会长或官员）
        var operatorMember = await _repository.GetMemberAsync(operatorId);
        if (operatorMember == null || operatorMember.GuildId != guildId)
            return false;

        if (operatorMember.Role != "Leader" && operatorMember.Role != "Officer")
        {
            Logger.LogWarning("无权限批准申请: {OperatorId}", operatorId);
            return false;
        }

        // 获取申请
        var joinRequest = await _repository.GetJoinRequestAsync(guildId, userId);
        if (joinRequest == null) return false;

        // 检查公会是否已满
        if (guild.MemberCount >= guild.MaxMembers)
        {
            Logger.LogWarning("公会已满，无法批准申请: {GuildId}", guildId);
            return false;
        }

        // 添加成员
        var member = new GuildMember
        {
            GuildId = guildId,
            UserId = userId,
            Username = joinRequest.Username,
            Role = "Member",
            Contribution = 0,
            JoinedAt = DateTime.UtcNow,
            LastOnlineAt = DateTime.UtcNow
        };

        await _repository.AddMemberAsync(member);

        // 更新公会成员数
        guild.MemberCount++;
        await _repository.UpdateGuildAsync(guild);

        // 更新申请状态
        joinRequest.Status = "Approved";
        await _repository.UpdateJoinRequestAsync(joinRequest);

        // 记录活动
        await AddActivityAsync(guildId, userId, joinRequest.Username, "MemberJoined", $"{joinRequest.Username} 加入了公会");

        Logger.LogInformation("加入申请已批准: {UserId} -> {GuildId}", userId, guildId);
        return true;
    }

    public async Task<bool> RejectJoinRequestAsync(string operatorId, string guildId, string userId)
    {
        // 检查权限
        var operatorMember = await _repository.GetMemberAsync(operatorId);
        if (operatorMember == null || operatorMember.GuildId != guildId)
            return false;

        if (operatorMember.Role != "Leader" && operatorMember.Role != "Officer")
            return false;

        // 获取申请
        var joinRequest = await _repository.GetJoinRequestAsync(guildId, userId);
        if (joinRequest == null) return false;

        // 更新申请状态
        joinRequest.Status = "Rejected";
        await _repository.UpdateJoinRequestAsync(joinRequest);

        Logger.LogInformation("加入申请已拒绝: {UserId} -> {GuildId}", userId, guildId);
        return true;
    }

    public async Task<bool> LeaveGuildAsync(string userId)
    {
        var member = await _repository.GetMemberAsync(userId);
        if (member == null) return false;

        // 会长不能直接离开，需要先转让或解散
        if (member.Role == "Leader")
        {
            Logger.LogWarning("会长尝试离开公会，请先转让或解散: {UserId}", userId);
            return false;
        }

        var guildId = member.GuildId;

        // 删除成员
        await _repository.RemoveMemberAsync(userId);

        // 更新公会成员数
        var guild = await _repository.GetGuildAsync(guildId);
        if (guild != null)
        {
            guild.MemberCount--;
            await _repository.UpdateGuildAsync(guild);
        }

        // 记录活动
        await AddActivityAsync(guildId, userId, member.Username, "MemberLeft", $"{member.Username} 离开了公会");

        Logger.LogInformation("成员已离开公会: {UserId} from {GuildId}", userId, guildId);
        return true;
    }

    public async Task<bool> KickMemberAsync(string operatorId, string guildId, string targetUserId)
    {
        // 检查权限
        var operatorMember = await _repository.GetMemberAsync(operatorId);
        if (operatorMember == null || operatorMember.GuildId != guildId)
            return false;

        if (operatorMember.Role != "Leader" && operatorMember.Role != "Officer")
            return false;

        // 获取目标成员
        var targetMember = await _repository.GetMemberAsync(targetUserId);
        if (targetMember == null || targetMember.GuildId != guildId)
            return false;

        // 不能踢会长
        if (targetMember.Role == "Leader")
            return false;

        // 官员只能踢普通成员
        if (operatorMember.Role == "Officer" && targetMember.Role != "Member")
            return false;

        // 删除成员
        await _repository.RemoveMemberAsync(targetUserId);

        // 更新公会成员数
        var guild = await _repository.GetGuildAsync(guildId);
        if (guild != null)
        {
            guild.MemberCount--;
            await _repository.UpdateGuildAsync(guild);
        }

        // 记录活动
        await AddActivityAsync(guildId, targetUserId, targetMember.Username, "MemberKicked", $"{targetMember.Username} 被移出公会");

        Logger.LogInformation("成员被踢出: {TargetUserId} from {GuildId} by {OperatorId}", targetUserId, guildId, operatorId);
        return true;
    }

    public async Task<bool> PromoteMemberAsync(string operatorId, string guildId, string targetUserId, string newRole)
    {
        // 只有会长可以提升
        var operatorMember = await _repository.GetMemberAsync(operatorId);
        if (operatorMember == null || operatorMember.GuildId != guildId || operatorMember.Role != "Leader")
            return false;

        var targetMember = await _repository.GetMemberAsync(targetUserId);
        if (targetMember == null || targetMember.GuildId != guildId)
            return false;

        // 验证新职位
        if (newRole != "Officer" && newRole != "Member")
            return false;

        targetMember.Role = newRole;
        return await _repository.UpdateMemberAsync(targetMember);
    }

    public async Task<bool> DemoteMemberAsync(string operatorId, string guildId, string targetUserId)
    {
        return await PromoteMemberAsync(operatorId, guildId, targetUserId, "Member");
    }

    public async Task<bool> TransferLeadershipAsync(string currentLeaderId, string guildId, string newLeaderId)
    {
        var guild = await _repository.GetGuildAsync(guildId);
        if (guild == null || guild.LeaderId != currentLeaderId)
            return false;

        var newLeader = await _repository.GetMemberAsync(newLeaderId);
        if (newLeader == null || newLeader.GuildId != guildId)
            return false;

        var currentLeader = await _repository.GetMemberAsync(currentLeaderId);
        if (currentLeader == null)
            return false;

        // 更新会长
        guild.LeaderId = newLeaderId;
        await _repository.UpdateGuildAsync(guild);

        // 更新成员角色
        newLeader.Role = "Leader";
        await _repository.UpdateMemberAsync(newLeader);

        currentLeader.Role = "Member";
        await _repository.UpdateMemberAsync(currentLeader);

        // 记录活动
        await AddActivityAsync(guildId, newLeaderId, newLeader.Username, "LeaderChanged",
            $"{newLeader.Username} 成为新的会长");

        Logger.LogInformation("会长已转让: {GuildId} from {OldLeader} to {NewLeader}",
            guildId, currentLeaderId, newLeaderId);
        return true;
    }

    public async Task<GuildMember[]> GetMembersAsync(string guildId)
    {
        var members = await _repository.GetGuildMembersAsync(guildId);
        return members.ToArray();
    }

    public async Task<MyGuildInfo?> GetMyGuildAsync(string userId)
    {
        var member = await _repository.GetMemberAsync(userId);
        if (member == null) return null;

        var guild = await _repository.GetGuildAsync(member.GuildId);
        if (guild == null) return null;

        // 计算在线成员数（这里简化处理）
        var members = await _repository.GetGuildMembersAsync(guild.GuildId);
        var onlineCount = members.Count(m => (DateTime.UtcNow - m.LastOnlineAt).TotalMinutes < 30);

        return new MyGuildInfo
        {
            Guild = guild,
            MyMember = member,
            OnlineMemberCount = onlineCount
        };
    }

    public async Task<JoinRequest[]> GetJoinRequestsAsync(string operatorId, string guildId)
    {
        // 检查权限
        var operatorMember = await _repository.GetMemberAsync(operatorId);
        if (operatorMember == null || operatorMember.GuildId != guildId)
            return Array.Empty<JoinRequest>();

        if (operatorMember.Role != "Leader" && operatorMember.Role != "Officer")
            return Array.Empty<JoinRequest>();

        var requests = await _repository.GetJoinRequestsAsync(guildId);
        return requests.ToArray();
    }

    // ========== 公会聊天 ==========

    public async Task<bool> SendMessageAsync(string userId, string username, string content)
    {
        var member = await _repository.GetMemberAsync(userId);
        if (member == null) return false;

        var message = new GuildMessage
        {
            GuildId = member.GuildId,
            UserId = userId,
            Username = username,
            Content = content,
            Timestamp = DateTime.UtcNow
        };

        await _repository.AddMessageAsync(message);
        return true;
    }

    public async Task<GuildMessage[]> GetChatHistoryAsync(string userId, int limit)
    {
        var member = await _repository.GetMemberAsync(userId);
        if (member == null) return Array.Empty<GuildMessage>();

        var messages = await _repository.GetMessagesAsync(member.GuildId, limit);
        return messages.ToArray();
    }

    // ========== 公会贡献 ==========

    public async Task<bool> DonateAsync(string userId, DonateRequest request)
    {
        var member = await _repository.GetMemberAsync(userId);
        if (member == null) return false;

        // 增加贡献度
        member.Contribution += request.Amount;
        await _repository.UpdateMemberAsync(member);

        // 增加公会经验
        var guild = await _repository.GetGuildAsync(member.GuildId);
        if (guild != null)
        {
            guild.Exp += request.Amount;

            // 升级逻辑（每1000经验升1级）
            while (guild.Exp >= 1000 * guild.Level)
            {
                guild.Exp -= 1000 * guild.Level;
                guild.Level++;
                guild.MaxMembers += 5; // 每升1级增加5个成员槽
            }

            await _repository.UpdateGuildAsync(guild);
        }

        // 记录活动
        await AddActivityAsync(member.GuildId, userId, member.Username, "Donate",
            $"{member.Username} 捐献了 {request.Amount} {request.ResourceType}");

        return true;
    }

    public async Task<GuildMember[]> GetContributionRankingAsync(string guildId)
    {
        var members = await _repository.GetGuildMembersAsync(guildId);
        return members.OrderByDescending(m => m.Contribution).ToArray();
    }

    // ========== 公会活动 ==========

    public async Task<bool> CheckInAsync(string userId, string username)
    {
        var member = await _repository.GetMemberAsync(userId);
        if (member == null) return false;

        // 更新最后上线时间
        member.LastOnlineAt = DateTime.UtcNow;
        await _repository.UpdateMemberAsync(member);

        // 记录活动
        await AddActivityAsync(member.GuildId, userId, username, "CheckIn", $"{username} 签到");

        return true;
    }

    public async Task<GuildActivity[]> GetActivitiesAsync(string guildId)
    {
        var activities = await _repository.GetActivitiesAsync(guildId, 50);
        return activities.ToArray();
    }

    private async Task AddActivityAsync(string guildId, string userId, string username, string activityType, string description)
    {
        var activity = new GuildActivity
        {
            GuildId = guildId,
            ActivityType = activityType,
            UserId = userId,
            Username = username,
            Description = description,
            Timestamp = DateTime.UtcNow
        };

        await _repository.AddActivityAsync(activity);
    }

    // ========== 公会公告 ==========

    public async Task<bool> PostAnnouncementAsync(string operatorId, string guildId, string content)
    {
        // 检查权限
        var operatorMember = await _repository.GetMemberAsync(operatorId);
        if (operatorMember == null || operatorMember.GuildId != guildId)
            return false;

        if (operatorMember.Role != "Leader" && operatorMember.Role != "Officer")
            return false;

        var announcement = new GuildAnnouncement
        {
            GuildId = guildId,
            AuthorId = operatorId,
            AuthorName = operatorMember.Username,
            Content = content,
            CreatedAt = DateTime.UtcNow,
            IsPinned = false
        };

        await _repository.AddAnnouncementAsync(announcement);
        return true;
    }

    public async Task<GuildAnnouncement[]> GetAnnouncementsAsync(string userId, string guildId)
    {
        // 验证用户是否是公会成员
        var member = await _repository.GetMemberAsync(userId);
        if (member == null || member.GuildId != guildId)
            return Array.Empty<GuildAnnouncement>();

        var announcements = await _repository.GetAnnouncementsAsync(guildId);
        return announcements.ToArray();
    }
}
