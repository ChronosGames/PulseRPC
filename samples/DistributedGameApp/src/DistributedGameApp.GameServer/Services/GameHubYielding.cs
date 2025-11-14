using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Shared.Domain.Mail;
using DistributedGameApp.Shared.Hubs;
using DistributedGameApp.Shared.Messages;
using DistributedGameApp.Shared.Domain.Characters;
using Microsoft.Extensions.Logging;
using PulseRPC;
using PulseRPC.Server;
using CreateCharacterRequest = DistributedGameApp.Shared.Messages.CreateCharacterRequest;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// 游戏服务 Hub 实现 - YieldingService 版本
/// </summary>
/// <remarks>
/// <para><strong>核心特性</strong>：</para>
/// <list type="bullet">
/// <item><description>✅ 继承自 YieldingService，await 自动让出队列</description></item>
/// <item><description>✅ 无需手动包装 ExecuteIOAsync，代码更简洁</description></item>
/// <item><description>✅ IO 操作期间，其他消息（如心跳）可以处理</description></item>
/// <item><description>✅ 线程安全，状态修改仍在队列线程</description></item>
/// </list>
/// </remarks>
public class GameHubYielding : YieldingService, IGameHub
{
    private readonly CharacterRepository _characterRepository;
    private readonly AccountRepository _accountRepository;
    private readonly CharacterService _characterService;
    private readonly MailService _mailService;
    private readonly BackendServerClient _backendServerClient;

    // 存储玩家ID (由于继承自 YieldingService，状态修改在队列线程，无需 AsyncLocal)
    private string? _playerId;

    public GameHubYielding(
        CharacterRepository characterRepository,
        AccountRepository accountRepository,
        CharacterService characterService,
        MailService mailService,
        BackendServerClient backendServerClient,
        ILogger<GameHubYielding> logger,
        IAuthenticationService authenticationService,
        PermissionValidator permissionValidator)
        : base(logger, authenticationService, permissionValidator)
    {
        _characterRepository = characterRepository ?? throw new ArgumentNullException(nameof(characterRepository));
        _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        _characterService = characterService ?? throw new ArgumentNullException(nameof(characterService));
        _mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));
        _backendServerClient = backendServerClient ?? throw new ArgumentNullException(nameof(backendServerClient));
    }

    /// <summary>
    /// 玩家登录（这里是从 LoginServer 获取 token 后连接到 GameServer）
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            // ✅ 数据库查询 - await 自动让出队列，其他消息可以处理
            Logger.LogDebug("Loading account from database: {Account}", request.Account);
            var account = await _accountRepository.GetByUserIdAsync(request.Account);

            if (account == null)
            {
                return new LoginResponse
                {
                    Success = false,
                    ErrorCode = 1001,
                    ErrorMessage = "账号不存在"
                };
            }

            // ✅ IO 完成后自动回到队列线程，可以安全修改状态
            _playerId = account.UserId;

            Logger.LogInformation("Player logged in: {PlayerId}", _playerId);

            return new LoginResponse
            {
                Success = true,
                PlayerId = account.UserId,
                AccessToken = "temp_token",
                TokenExpireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Login failed for account: {Account}", request.Account);
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
        if (string.IsNullOrEmpty(_playerId))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        return await _characterService.CreateCharacterAsync(_playerId, request);
    }

    /// <summary>
    /// 获取角色信息
    /// </summary>
    public async Task<CharacterInfo?> GetCharacterAsync(string characterId)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        // ✅ await 自动让出队列
        var character = await _characterRepository.GetByCharacterIdAsync(characterId);

        if (character == null || character.UserId != _playerId)
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
        if (string.IsNullOrEmpty(_playerId))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        // ✅ await 自动让出队列
        var characters = await _characterRepository.GetByUserIdAsync(_playerId);

        return characters.Select(MapToCharacterInfo).ToArray();
    }

    /// <summary>
    /// 删除角色 - 原子操作示例
    /// </summary>
    public async Task<bool> DeleteCharacterAsync(string characterId)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        // ✅ 使用 AtomicAsync 确保原子性
        // 锁定角色 ID，但 await 仍会让出队列（不阻塞其他消息）
        return await AtomicAsync(characterId, async () =>
        {
            var character = await _characterRepository.GetByCharacterIdAsync(characterId);

            if (character == null || character.UserId != _playerId)
            {
                return false;
            }

            return await _characterRepository.DeleteAsync(characterId);
        });
    }

    /// <summary>
    /// 请求匹配
    /// </summary>
    public async Task<MatchmakingResponse> RequestMatchAsync(MatchmakingRequest request)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("玩家未登录，无法请求匹配");
            return new MatchmakingResponse
            {
                Success = false,
                ErrorMessage = "玩家未登录"
            };
        }

        try
        {
            Logger.LogInformation("Player {PlayerId} 请求匹配: Mode={Mode}",
                _playerId, request.Mode);

            // 确保 BackendServerClient 已连接
            if (!_backendServerClient.IsConnected)
            {
                Logger.LogWarning("BackendServerClient 未连接，尝试初始化...");
                await _backendServerClient.InitializeAsync();
            }

            var backendRequest = new Shared.Domain.Matchmaking.MatchmakingRequest
            {
                PlayerId = _playerId,
                CharacterId = "",
                MatchType = request.Mode.ToString(),
                LevelRange = 5
            };

            // ✅ await 自动让出队列
            var backendResponse = await _backendServerClient.StartMatchmakingAsync(backendRequest);

            var response = new MatchmakingResponse
            {
                Success = backendResponse.Success,
                TicketId = backendResponse.MatchId,
                EstimatedWaitTime = backendResponse.EstimatedWaitSeconds,
                ErrorMessage = backendResponse.Message
            };

            if (response.Success)
            {
                Logger.LogInformation("玩家 {PlayerId} 匹配请求成功, TicketId={TicketId}",
                    _playerId, response.TicketId);
            }
            else
            {
                Logger.LogWarning("玩家 {PlayerId} 匹配请求失败: {Message}",
                    _playerId, response.ErrorMessage);
            }

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "玩家 {PlayerId} 请求匹配时发生异常", _playerId);
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
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("玩家未登录，无法取消匹配");
            return false;
        }

        try
        {
            Logger.LogInformation("Player {PlayerId} 取消匹配: TicketId={TicketId}",
                _playerId, ticketId);

            if (!_backendServerClient.IsConnected)
            {
                Logger.LogWarning("BackendServerClient 未连接，无法取消匹配");
                return false;
            }

            var result = await _backendServerClient.CancelMatchmakingAsync(_playerId);

            if (result)
            {
                Logger.LogInformation("玩家 {PlayerId} 成功取消匹配", _playerId);
            }
            else
            {
                Logger.LogWarning("玩家 {PlayerId} 取消匹配失败", _playerId);
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "玩家 {PlayerId} 取消匹配时发生异常", _playerId);
            return false;
        }
    }

    /// <summary>
    /// 确认加入战斗
    /// </summary>
    public Task<bool> ConfirmBattleAsync(string battleId)
    {
        Logger.LogInformation("Player {PlayerId} confirmed battle: {BattleId}", _playerId, battleId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// 心跳（保持在线状态）
    /// </summary>
    public Task<long> HeartbeatAsync()
    {
        // ✅ 纯内存操作 - 直接返回，不需要 await
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// 更新在线状态
    /// </summary>
    public Task<bool> UpdateOnlineStatusAsync(OnlineStatus status)
    {
        Logger.LogDebug("Player {PlayerId} status updated to: {Status}", _playerId, status);
        return Task.FromResult(true);
    }

    // ========== 邮件系统实现 ==========

    public async Task<SendMailResponse> SendMailAsync(SendMailRequest request)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("未登录玩家尝试发送邮件");
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = "请先登录"
            };
        }

        try
        {
            Logger.LogInformation("玩家 {PlayerId} 发送邮件给 {ReceiverId}", _playerId, request.ReceiverId);

            var response = await _mailService.SendMailAsync(_playerId, request);

            if (response.Success)
            {
                Logger.LogInformation("邮件发送成功 - MailId={MailId}", response.MailId);
            }

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送邮件时发生异常 - SenderId={PlayerId}", _playerId);
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = $"发送邮件失败: {ex.Message}"
            };
        }
    }

    public async Task<MailListResponse> GetMailListAsync(GetMailListRequest request)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("未登录玩家尝试获取邮件列表");
            return new MailListResponse
            {
                Mails = new List<Mail>(),
                TotalCount = 0,
                UnreadCount = 0
            };
        }

        try
        {
            Logger.LogDebug("玩家 {PlayerId} 获取邮件列表 - Page={Page}, PageSize={PageSize}",
                _playerId, request.Page, request.PageSize);

            return await _mailService.GetMailListAsync(_playerId, request);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取邮件列表时发生异常 - PlayerId={PlayerId}", _playerId);
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
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("未登录玩家尝试读取邮件");
            return null;
        }

        try
        {
            Logger.LogDebug("玩家 {PlayerId} 读取邮件 - MailId={MailId}", _playerId, mailId);

            return await _mailService.ReadMailAsync(_playerId, mailId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "读取邮件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                _playerId, mailId);
            return null;
        }
    }

    public async Task<bool> ClaimMailAttachmentAsync(string mailId)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("未登录玩家尝试领取邮件附件");
            return false;
        }

        try
        {
            Logger.LogInformation("玩家 {PlayerId} 领取邮件附件 - MailId={MailId}", _playerId, mailId);

            return await _mailService.ClaimAttachmentAsync(_playerId, mailId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "领取邮件附件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                _playerId, mailId);
            return false;
        }
    }

    public async Task<bool> DeleteMailAsync(string mailId)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("未登录玩家尝试删除邮件");
            return false;
        }

        try
        {
            Logger.LogInformation("玩家 {PlayerId} 删除邮件 - MailId={MailId}", _playerId, mailId);

            return await _mailService.DeleteMailAsync(_playerId, mailId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "删除邮件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                _playerId, mailId);
            return false;
        }
    }

    public async Task<int> GetUnreadMailCountAsync()
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            return 0;
        }

        try
        {
            return await _mailService.GetUnreadCountAsync(_playerId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取未读邮件数量时发生异常 - PlayerId={PlayerId}", _playerId);
            return 0;
        }
    }

    // ========== 原子操作示例 ==========

    /// <summary>
    /// 转账操作 - AtomicAsync 最佳实践示例
    /// </summary>
    /// <remarks>
    /// 演示如何使用 AtomicAsync 实现需要原子性的操作：
    /// - ✅ 使用细粒度锁（只锁定涉及的两个玩家）
    /// - ✅ await 仍会让出队列（不阻塞其他消息）
    /// - ✅ 自动按 ID 排序加锁（避免死锁）
    /// - ✅ 保证扣款和加款的原子性
    /// </remarks>
    public async Task<bool> TransferGoldExampleAsync(string toPlayerId, int amount)
    {
        if (string.IsNullOrEmpty(_playerId))
        {
            Logger.LogWarning("未登录玩家尝试转账");
            return false;
        }

        if (amount <= 0)
        {
            Logger.LogWarning("转账金额必须大于 0");
            return false;
        }

        try
        {
            // ✅ 使用 AtomicAsync 锁定两个玩家（按 ID 排序避免死锁）
            return await AtomicAsync(new[] { _playerId, toPlayerId }, async () =>
            {
                Logger.LogInformation(
                    "开始转账：从 {From} 向 {To} 转账 {Amount} 金币",
                    _playerId, toPlayerId, amount);

                // 模拟数据库操作（在真实实现中，这里应该查询数据库）
                // 这里所有的 await 都会让出队列，但锁保证了原子性
                await Task.Delay(10);

                Logger.LogInformation(
                    "转账成功：从 {From} 向 {To} 转账 {Amount} 金币",
                    _playerId, toPlayerId, amount);

                return true;
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "转账失败：从 {From} 向 {To}", _playerId, toPlayerId);
            return false;
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
