using Microsoft.Extensions.Logging;

namespace DistributedGameApp.Infrastructure.MongoDB.Repositories;

/// <summary>
/// 公会相关 Repository 索引初始化器
/// </summary>
public class GuildRepositoryIndexInitializer
{
    private readonly GuildRepository _guildRepository;
    private readonly GuildMemberRepository _memberRepository;
    private readonly GuildJoinRequestRepository _joinRequestRepository;
    private readonly GuildMessageRepository _messageRepository;
    private readonly GuildActivityRepository _activityRepository;
    private readonly GuildAnnouncementRepository _announcementRepository;
    private readonly ILogger<GuildRepositoryIndexInitializer> _logger;

    public GuildRepositoryIndexInitializer(
        GuildRepository guildRepository,
        GuildMemberRepository memberRepository,
        GuildJoinRequestRepository joinRequestRepository,
        GuildMessageRepository messageRepository,
        GuildActivityRepository activityRepository,
        GuildAnnouncementRepository announcementRepository,
        ILogger<GuildRepositoryIndexInitializer> logger)
    {
        _guildRepository = guildRepository;
        _memberRepository = memberRepository;
        _joinRequestRepository = joinRequestRepository;
        _messageRepository = messageRepository;
        _activityRepository = activityRepository;
        _announcementRepository = announcementRepository;
        _logger = logger;
    }

    /// <summary>
    /// 初始化所有公会相关集合的索引
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        _logger.LogInformation("开始初始化公会相关集合的索引...");

        try
        {
            await _guildRepository.EnsureIndexesAsync();
            _logger.LogInformation("✓ Guild 集合索引初始化完成");

            await _memberRepository.EnsureIndexesAsync();
            _logger.LogInformation("✓ GuildMember 集合索引初始化完成");

            await _joinRequestRepository.EnsureIndexesAsync();
            _logger.LogInformation("✓ GuildJoinRequest 集合索引初始化完成");

            await _messageRepository.EnsureIndexesAsync();
            _logger.LogInformation("✓ GuildMessage 集合索引初始化完成");

            await _activityRepository.EnsureIndexesAsync();
            _logger.LogInformation("✓ GuildActivity 集合索引初始化完成");

            await _announcementRepository.EnsureIndexesAsync();
            _logger.LogInformation("✓ GuildAnnouncement 集合索引初始化完成");

            _logger.LogInformation("公会相关集合的索引初始化全部完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化公会索引时发生错误");
            throw;
        }
    }
}
