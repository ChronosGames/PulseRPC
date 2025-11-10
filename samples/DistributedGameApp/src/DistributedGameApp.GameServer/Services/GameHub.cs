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
/// 游戏服务 Hub 实现
/// </summary>
public class GameHub(
    CharacterRepository characterRepository,
    AccountRepository accountRepository,
    CharacterService characterService,
    MailService mailService,
    BackendServerClient backendServerClient,
    ILogger<GameHub> logger)
    : IGameHub
{
    // 存储玩家ID (通过 AsyncLocal 保证线程安全)
    private static readonly AsyncLocal<string?> _playerId = new();

    /// <summary>
    /// 玩家登录（这里是从 LoginServer 获取 token 后连接到 GameServer）
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        try
        {
            // 在实际应用中，这里应该验证 JWT Token
            // 目前简化实现，直接通过账号密码验证
            var account = await accountRepository.GetByUserIdAsync(request.Account);

            if (account == null)
            {
                return new LoginResponse
                {
                    Success = false,
                    ErrorCode = 1001,
                    ErrorMessage = "账号不存在"
                };
            }

            _playerId.Value = account.UserId;

            logger.LogInformation("Player logged in: {PlayerId}", _playerId.Value);

            return new LoginResponse
            {
                Success = true,
                PlayerId = account.UserId,
                AccessToken = "temp_token", // 实际应该从 LoginServer 获取
                TokenExpireAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed for account: {Account}", request.Account);
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
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        return await characterService.CreateCharacterAsync(_playerId.Value, request);
    }

    /// <summary>
    /// 获取角色信息
    /// </summary>
    public async Task<CharacterInfo?> GetCharacterAsync(string characterId)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        var character = await characterRepository.GetByCharacterIdAsync(characterId);

        if (character == null || character.UserId != _playerId.Value)
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
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        var characters = await characterRepository.GetByUserIdAsync(_playerId.Value);

        return characters.Select(MapToCharacterInfo).ToArray();
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    public async Task<bool> DeleteCharacterAsync(string characterId)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            throw new InvalidOperationException("Player not authenticated");
        }

        var character = await characterRepository.GetByCharacterIdAsync(characterId);

        if (character == null || character.UserId != _playerId.Value)
        {
            return false;
        }

        return await characterRepository.DeleteAsync(characterId);
    }

    /// <summary>
    /// 请求匹配
    /// </summary>
    public async Task<MatchmakingResponse> RequestMatchAsync(MatchmakingRequest request)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("玩家未登录，无法请求匹配");
            return new MatchmakingResponse
            {
                Success = false,
                ErrorMessage = "玩家未登录"
            };
        }

        try
        {
            logger.LogInformation("Player {PlayerId} 请求匹配: Mode={Mode}",
                _playerId.Value, request.Mode);

            // 确保 BackendServerClient 已连接
            if (!backendServerClient.IsConnected)
            {
                logger.LogWarning("BackendServerClient 未连接，尝试初始化...");
                await backendServerClient.InitializeAsync();
            }

            // 转换 Messages.MatchmakingRequest 为 Domain.Matchmaking.MatchmakingRequest
            var backendRequest = new Shared.Domain.Matchmaking.MatchmakingRequest
            {
                PlayerId = _playerId.Value,
                CharacterId = "", // TODO: 从当前角色获取
                MatchType = request.Mode.ToString(), // 将 MatchMode 转换为字符串
                LevelRange = 5 // 默认等级范围
            };

            // 调用 BackendServer 的匹配服务
            var backendResponse = await backendServerClient.StartMatchmakingAsync(backendRequest);

            // 转换 Domain.Matchmaking.MatchmakingResponse 为 Messages.MatchmakingResponse
            var response = new MatchmakingResponse
            {
                Success = backendResponse.Success,
                TicketId = backendResponse.MatchId, // 使用 MatchId 作为 TicketId
                EstimatedWaitTime = backendResponse.EstimatedWaitSeconds,
                ErrorMessage = backendResponse.Message
            };

            if (response.Success)
            {
                logger.LogInformation("玩家 {PlayerId} 匹配请求成功, TicketId={TicketId}",
                    _playerId.Value, response.TicketId);
            }
            else
            {
                logger.LogWarning("玩家 {PlayerId} 匹配请求失败: {Message}",
                    _playerId.Value, response.ErrorMessage);
            }

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "玩家 {PlayerId} 请求匹配时发生异常", _playerId.Value);
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
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("玩家未登录，无法取消匹配");
            return false;
        }

        try
        {
            logger.LogInformation("Player {PlayerId} 取消匹配: TicketId={TicketId}",
                _playerId.Value, ticketId);

            // 确保 BackendServerClient 已连接
            if (!backendServerClient.IsConnected)
            {
                logger.LogWarning("BackendServerClient 未连接，无法取消匹配");
                return false;
            }

            // 调用 BackendServer 的取消匹配服务
            var result = await backendServerClient.CancelMatchmakingAsync(_playerId.Value);

            if (result)
            {
                logger.LogInformation("玩家 {PlayerId} 成功取消匹配", _playerId.Value);
            }
            else
            {
                logger.LogWarning("玩家 {PlayerId} 取消匹配失败", _playerId.Value);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "玩家 {PlayerId} 取消匹配时发生异常", _playerId.Value);
            return false;
        }
    }

    /// <summary>
    /// 确认加入战斗
    /// </summary>
    public Task<bool> ConfirmBattleAsync(string battleId)
    {
        // TODO: 实现战斗确认逻辑
        logger.LogInformation("Player {PlayerId} confirmed battle: {BattleId}", _playerId.Value, battleId);
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
        // TODO: 实现在线状态更新
        logger.LogDebug("Player {PlayerId} status updated to: {Status}", _playerId.Value, status);
        return Task.FromResult(true);
    }

    // ========== 邮件系统实现 ==========

    /// <summary>
    /// 发送邮件给其他玩家
    /// </summary>
    public async Task<SendMailResponse> SendMailAsync(SendMailRequest request)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("未登录玩家尝试发送邮件");
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = "请先登录"
            };
        }

        try
        {
            logger.LogInformation("玩家 {PlayerId} 发送邮件给 {ReceiverId}", _playerId.Value, request.ReceiverId);

            var response = await mailService.SendMailAsync(_playerId.Value, request);

            if (response.Success)
            {
                logger.LogInformation("邮件发送成功 - MailId={MailId}", response.MailId);
            }

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "发送邮件时发生异常 - SenderId={PlayerId}", _playerId.Value);
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = $"发送邮件失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 获取邮件列表
    /// </summary>
    public async Task<MailListResponse> GetMailListAsync(GetMailListRequest request)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("未登录玩家尝试获取邮件列表");
            return new MailListResponse
            {
                Mails = new List<Mail>(),
                TotalCount = 0,
                UnreadCount = 0
            };
        }

        try
        {
            logger.LogDebug("玩家 {PlayerId} 获取邮件列表 - Page={Page}, PageSize={PageSize}",
                _playerId.Value, request.Page, request.PageSize);

            return await mailService.GetMailListAsync(_playerId.Value, request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取邮件列表时发生异常 - PlayerId={PlayerId}", _playerId.Value);
            return new MailListResponse
            {
                Mails = new List<Mail>(),
                TotalCount = 0,
                UnreadCount = 0
            };
        }
    }

    /// <summary>
    /// 读取邮件
    /// </summary>
    public async Task<Mail?> ReadMailAsync(string mailId)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("未登录玩家尝试读取邮件");
            return null;
        }

        try
        {
            logger.LogDebug("玩家 {PlayerId} 读取邮件 - MailId={MailId}", _playerId.Value, mailId);

            return await mailService.ReadMailAsync(_playerId.Value, mailId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取邮件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                _playerId.Value, mailId);
            return null;
        }
    }

    /// <summary>
    /// 领取邮件附件
    /// </summary>
    public async Task<bool> ClaimMailAttachmentAsync(string mailId)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("未登录玩家尝试领取邮件附件");
            return false;
        }

        try
        {
            logger.LogInformation("玩家 {PlayerId} 领取邮件附件 - MailId={MailId}", _playerId.Value, mailId);

            return await mailService.ClaimAttachmentAsync(_playerId.Value, mailId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "领取邮件附件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                _playerId.Value, mailId);
            return false;
        }
    }

    /// <summary>
    /// 删除邮件
    /// </summary>
    public async Task<bool> DeleteMailAsync(string mailId)
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            logger.LogWarning("未登录玩家尝试删除邮件");
            return false;
        }

        try
        {
            logger.LogInformation("玩家 {PlayerId} 删除邮件 - MailId={MailId}", _playerId.Value, mailId);

            return await mailService.DeleteMailAsync(_playerId.Value, mailId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "删除邮件时发生异常 - PlayerId={PlayerId}, MailId={MailId}",
                _playerId.Value, mailId);
            return false;
        }
    }

    /// <summary>
    /// 获取未读邮件数量
    /// </summary>
    public async Task<int> GetUnreadMailCountAsync()
    {
        if (string.IsNullOrEmpty(_playerId.Value))
        {
            return 0;
        }

        try
        {
            return await mailService.GetUnreadCountAsync(_playerId.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "获取未读邮件数量时发生异常 - PlayerId={PlayerId}", _playerId.Value);
            return 0;
        }
    }

    /// <summary>
    /// 映射 Character 到 CharacterInfo
    /// </summary>
    private static CharacterInfo MapToCharacterInfo(Character character)
    {
        // 解析职业枚举
        Enum.TryParse<CharacterClass>(character.Class, out var characterClass);

        return new CharacterInfo
        {
            CharacterId = character.CharacterId,
            PlayerId = character.UserId,
            CharacterName = character.Name,
            Class = characterClass,
            Gender = Gender.Male, // Character 模型中没有 Gender 字段，默认为男性
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
