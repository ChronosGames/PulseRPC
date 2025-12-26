using System.Collections.Concurrent;
using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Infrastructure.ServiceClient;
using DistributedGameApp.Shared.Domain.Mail;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using DistributedGameApp.Shared.Domain.Characters;
using DistributedGameApp.GameServer.Services;
using Microsoft.Extensions.Logging;
using PulseRPC.Server;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Hubs;
using PulseRPC.Server.ServiceManagement;
using CreateCharacterRequest = DistributedGameApp.Shared.Messages.CreateCharacterRequest;

namespace DistributedGameApp.GameServer.Hubs;

/// <summary>
/// 游戏服务 Hub 实现 - 无状态版本
/// </summary>
/// <remarks>
/// <para><strong>设计原则</strong>:</para>
/// <list type="bullet">
/// <item><description>✅ IPulseHub 保持无状态 - 只作为请求的入口点</description></item>
/// <item><description>✅ 连接管理委托给 GameServerInternalHub</description></item>
/// <item><description>✅ 业务逻辑委托给 Service 层</description></item>
/// <item><description>✅ 使用 IServiceAccessor 访问 ProcessSingleton 服务，确保线程安全</description></item>
/// </list>
/// <para><strong>服务访问模式</strong>:</para>
/// <code>
/// // ProcessSingleton 服务通过 IServiceAccessor 访问，serviceId 固定为 "local"
/// await _characterService.ExecuteAsync("local", s => s.CreateCharacterAsync(...));
/// </code>
/// </remarks>
public class GameHub : PulseHubBase, IGameHub
{
    private readonly CharacterRepository _characterRepository;
    private readonly AccountRepository _accountRepository;
    private readonly IServiceAccessor<CharacterService> _characterService;
    private readonly IServiceAccessor<MailService> _mailService;
    private readonly UnifiedServiceClientManager _serviceClientManager;
    private readonly IAuthenticationService _authenticationService;
    private readonly GameServerInternalHub _internalHub;
    private readonly IUserConnectionMapping _userConnectionMapping;
    private readonly ILogger<GameHub> _logger;

    public GameHub(
        CharacterRepository characterRepository,
        AccountRepository accountRepository,
        IServiceAccessor<CharacterService> characterService,
        IServiceAccessor<MailService> mailService,
        UnifiedServiceClientManager serviceClientManager,
        GameServerInternalHub internalHub,
        IUserConnectionMapping userConnectionMapping,
        ILogger<GameHub> logger,
        IAuthenticationService authenticationService)
    {
        _characterRepository = characterRepository ?? throw new ArgumentNullException(nameof(characterRepository));
        _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        _characterService = characterService ?? throw new ArgumentNullException(nameof(characterService));
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));
        _serviceClientManager = serviceClientManager ?? throw new ArgumentNullException(nameof(serviceClientManager));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _internalHub = internalHub ?? throw new ArgumentNullException(nameof(internalHub));
        _userConnectionMapping = userConnectionMapping ?? throw new ArgumentNullException(nameof(userConnectionMapping));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 玩家登录 - 基于 JWT Token 的会话级认证
    /// </summary>
    /// <remarks>
    /// 认证流程：
    /// 1. 验证 JWT Token（从 LoginServer 获取的 AccessToken）
    /// 2. 提取用户信息（UserId, Username, Permissions, Roles）
    /// 3. 建立会话级认证映射
    /// </remarks>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            // 获取当前连接
            if (string.IsNullOrEmpty(ConnectionId))
            {
                _logger.LogWarning("Login attempt without connection context");
                return new LoginResponse
                {
                    Success = false,
                    ErrorCode = 1002,
                    ErrorMessage = "无效的连接上下文"
                };
            }

            // ✅ JWT Token 认证（要求必须提供 Ticket）
            if (string.IsNullOrEmpty(request.Ticket))
            {
                _logger.LogWarning("Login request missing JWT Token, ConnectionId: {ConnectionId}", ConnectionId);
                return new LoginResponse
                {
                    Success = false,
                    ErrorCode = 1004,
                    ErrorMessage = "请提供有效的 JWT Token"
                };
            }

            _logger.LogDebug("JWT-based login attempt, ConnectionId: {ConnectionId}", ConnectionId);

            // 使用 IAuthenticationService 验证 JWT Token
            var authContext = await _authenticationService.AuthenticateUserAsync(request.Ticket);
            if (authContext == null)
            {
                _logger.LogWarning("JWT authentication failed, ConnectionId: {ConnectionId}", ConnectionId);
                return new LoginResponse
                {
                    Success = false,
                    ErrorCode = 1003,
                    ErrorMessage = "JWT Token 验证失败或已过期"
                };
            }

            var userId = authContext.UserId;
            _logger.LogInformation("JWT authentication successful: {UserId}, ConnectionId: {ConnectionId}",
                userId, ConnectionId);

            // ✅ 注册到 IUserConnectionMapping（用于 IHubContext<IGameReceiver>.Clients.User() 查找连接）
            _userConnectionMapping.Add(userId, ConnectionId!);

            // ✅ 注册到 InternalHub（用于接收 BackendServer 的回调通知）
            _internalHub.RegisterPlayerConnection(userId, ConnectionId!);

            _logger.LogInformation("Session established: UserId={UserId}, ConnectionId={ConnectionId}",
                userId, ConnectionId);

            return new LoginResponse
            {
                Success = true,
                PlayerId = userId,
                AccessToken = request.Ticket,
                TokenExpireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed");
            return new LoginResponse
            {
                Success = false,
                ErrorCode = 1000,
                ErrorMessage = "登录失败"
            };
        }
    }

    /// <summary>
    /// 创建新角色
    /// </summary>
    public async Task<CharacterInfo> CreateCharacterAsync(CreateCharacterRequest request)
    {
        return await _characterService.ExecuteWithUserId(
            (service, playerId) => service.CreateCharacterAsync(playerId, request));
    }

    /// <summary>
    /// 获取角色信息
    /// </summary>
    public async Task<CharacterInfo?> GetCharacterAsync(string characterId)
    {
        var character = await _characterRepository.GetByCharacterIdAsync(characterId);

        if (character == null || character.UserId != UserId)
        {
            return null;
        }

        return MapToCharacterInfo(character);
    }

    /// <summary>
    /// 获取玩家的所有角色列表
    /// </summary>
    public async Task<CharacterInfo[]> GetCharacterListAsync()
    {
        var characters = await _characterRepository.GetByUserIdAsync(UserId);

        return characters.Select(MapToCharacterInfo).ToArray();
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    public async Task<bool> DeleteCharacterAsync(string characterId)
    {
        var character = await _characterRepository.GetByCharacterIdAsync(characterId);

        if (character == null || character.UserId != UserId)
        {
            return false;
        }

        return await _characterRepository.DeleteAsync(characterId);
    }

    /// <summary>
    /// 请求匹配
    /// </summary>
    public async Task<MatchmakingResponse> RequestMatchAsync(MatchmakingRequest request)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("玩家未登录，无法请求匹配");
            return new MatchmakingResponse
            {
                Success = false,
                ErrorMessage = "玩家未登录"
            };
        }

        try
        {
            _logger.LogInformation("Player {PlayerId} 请求匹配: Mode={Mode}",
                UserId, request.Mode);

            // 使用 UnifiedServiceClientManager 获取 BackendHub 代理
            var backendHub = _serviceClientManager.GetHub<IBackendHub>(UserId);

            if (backendHub == null)
            {
                _logger.LogWarning("无法获取 BackendHub 代理: PlayerId={PlayerId}", UserId);
                return new MatchmakingResponse
                {
                    Success = false,
                    ErrorMessage = "BackendServer 不可用，请稍后重试"
                };
            }

            var backendRequest = new Shared.Domain.Matchmaking.MatchmakingRequest
            {
                PlayerId = UserId,
                CharacterId = "",
                MatchType = request.Mode.ToString(),
                LevelRange = 5
            };

            var backendResponse = await backendHub.StartMatchmakingAsync(backendRequest);

            var response = new MatchmakingResponse
            {
                Success = backendResponse.Success,
                TicketId = backendResponse.MatchId,
                EstimatedWaitTime = backendResponse.EstimatedWaitSeconds,
                ErrorMessage = backendResponse.Message
            };

            if (response.Success)
            {
                _logger.LogInformation("玩家 {PlayerId} 匹配请求成功, TicketId={TicketId}",
                    UserId, response.TicketId);
            }
            else
            {
                _logger.LogWarning("玩家 {PlayerId} 匹配请求失败: {Message}",
                    UserId, response.ErrorMessage);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "玩家 {PlayerId} 请求匹配时发生异常", UserId);
            return new MatchmakingResponse
            {
                Success = false,
                ErrorMessage = $"匹配请求异常: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 取消匹配
    /// </summary>
    public async Task<bool> CancelMatchAsync(string ticketId)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("玩家未登录，无法取消匹配");
            return false;
        }

        try
        {
            _logger.LogInformation("Player {PlayerId} 取消匹配: TicketId={TicketId}",
                UserId, ticketId);

            var backendHub = _serviceClientManager.GetHub<IBackendHub>(UserId);

            if (backendHub == null)
            {
                _logger.LogWarning("无法获取 BackendHub 代理: PlayerId={PlayerId}", UserId);
                return false;
            }

            var result = await backendHub.CancelMatchmakingAsync(UserId);

            if (result)
            {
                _logger.LogInformation("玩家 {PlayerId} 成功取消匹配", UserId);
            }
            else
            {
                _logger.LogWarning("玩家 {PlayerId} 取消匹配失败", UserId);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "玩家 {PlayerId} 取消匹配时发生异常", UserId);
            return false;
        }
    }

    /// <summary>
    /// 确认加入战斗
    /// </summary>
    public Task<bool> ConfirmBattleAsync(string battleId)
    {
        _logger.LogInformation("Player {PlayerId} confirmed battle: {BattleId}", CurrentUserId, battleId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 心跳（保持在线状态）
    /// </summary>
    public Task<long> HeartbeatAsync()
    {
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// 更新在线状态
    /// </summary>
    public Task<bool> UpdateOnlineStatusAsync(OnlineStatus status)
    {
        _logger.LogDebug("Player {PlayerId} status updated to: {Status}", CurrentUserId, status);
        return Task.FromResult(true);
    }

    // ========== 邮件系统实现 ==========

    public async Task<SendMailResponse> SendMailAsync(SendMailRequest request)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("未登录玩家尝试发送邮件");
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = "请先登录"
            };
        }

        try
        {
            _logger.LogInformation("玩家 {PlayerId} 发送邮件给 {ReceiverId}", UserId, request.ReceiverId);

            var response = await _mailService.ExecuteWithUserId(
                (service, playerId) => service.SendMailAsync(playerId, request));

            if (response.Success)
            {
                _logger.LogInformation("邮件发送成功 - MailId={MailId}", response.MailId);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送邮件时发生异常 - SenderId={PlayerId}", UserId);
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = $"发送邮件失败: {ex.Message}"
            };
        }
    }

    public async Task<MailListResponse> GetMailListAsync(GetMailListRequest request)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("未登录玩家尝试获取邮件列表");
            return new MailListResponse
            {
                Mails = new List<Mail>(),
                TotalCount = 0,
                UnreadCount = 0
            };
        }

        try
        {
            _logger.LogDebug("玩家 {PlayerId} 获取邮件列表 - Page={Page}, PageSize={PageSize}",
                UserId, request.Page, request.PageSize);

            return await _mailService.ExecuteWithUserId(
                (service, playerId) => service.GetMailListAsync(playerId, request));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取邮件列表时发生异常 - PlayerId={PlayerId}", UserId);
            return new MailListResponse
            {
                Mails = new List<Mail>(),
                TotalCount = 0,
                UnreadCount = 0
            };
        }
    }

    public async Task<Mail?> ReadMailAsync(string mailId)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("未登录玩家尝试读取邮件");
            return null;
        }

        try
        {
            _logger.LogDebug("玩家 {PlayerId} 读取邮件 - MailId={MailId}", UserId, mailId);

            return await _mailService.ExecuteWithUserId(
                (service, playerId) => service.ReadMailAsync(playerId, mailId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取邮件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                UserId, mailId);
            return null;
        }
    }

    public async Task<bool> ClaimMailAttachmentAsync(string mailId)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("未登录玩家尝试领取邮件附件");
            return false;
        }

        try
        {
            _logger.LogInformation("玩家 {PlayerId} 领取邮件附件 - MailId={MailId}", UserId, mailId);

            return await _mailService.ExecuteWithUserId(
                (service, playerId) => service.ClaimAttachmentAsync(playerId, mailId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "领取邮件附件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                UserId, mailId);
            return false;
        }
    }

    public async Task<bool> DeleteMailAsync(string mailId)
    {
        if (!IsAuthenticated)
        {
            _logger.LogWarning("未登录玩家尝试删除邮件");
            return false;
        }

        try
        {
            _logger.LogInformation("玩家 {PlayerId} 删除邮件 - MailId={MailId}", UserId, mailId);

            return await _mailService.ExecuteWithUserId(
                (service, playerId) => service.DeleteMailAsync(playerId, mailId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除邮件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                UserId, mailId);
            return false;
        }
    }

    public async Task<int> GetUnreadMailCountAsync()
    {
        if (!IsAuthenticated)
        {
            return 0;
        }

        try
        {
            return await _mailService.ExecuteWithUserId(
                (service, playerId) => service.GetUnreadCountAsync(playerId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取未读邮件数量时发生异常 - PlayerId={PlayerId}", UserId);
            return 0;
        }
    }

    /// <summary>
    /// 映射 Character 到 CharacterInfo
    /// </summary>
    private static CharacterInfo MapToCharacterInfo(Character character)
    {
        Enum.TryParse<CharacterClass>(character.Class, out var characterClass);

        return new CharacterInfo
        {
            CharacterId = character.CharacterId,
            PlayerId = character.UserId,
            CharacterName = character.Name,
            Class = characterClass,
            Gender = Gender.Male,
            Level = character.Level,
            Exp = character.Exp,
            Hp = character.Attributes.Hp,
            MaxHp = character.Attributes.MaxHp,
            Mp = character.Attributes.Mp,
            MaxMp = character.Attributes.MaxMp,
            Attack = character.Attributes.Attack,
            Defense = character.Attributes.Defense,
            Speed = character.Attributes.Speed,
            CreatedAt = character.CreatedAt,
            LastLoginAt = character.LastOnlineAt,
            Position = new Position
            {
                MapId = "default",
                X = 0,
                Y = 0,
                Z = 0
            },
            OnlineStatus = OnlineStatus.Online
        };
    }
}
