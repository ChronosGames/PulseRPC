using DistributedGameApp.Infrastructure.MongoDB.Repositories;
using DistributedGameApp.Shared.Domain.Mail;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Services;

namespace DistributedGameApp.GameServer.Services;

/// <summary>
/// 邮件服务 - 基于 PulseServiceBase 架构
/// </summary>
/// <remarks>
/// <para><strong>设计原则</strong>:</para>
/// <list type="bullet">
/// <item><description>✅ 继承 PulseServiceBase - 统一的服务基类</description></item>
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
    DisplayName = "MailService",
    EnableHealthCheck = true)]
public class MailService : PulseServiceBase
{
    private readonly MailRepository _mailRepository;
    private readonly CharacterRepository _characterRepository;

    public MailService(
        MailRepository mailRepository,
        CharacterRepository characterRepository,
        ILogger<MailService> logger)
        : base("MailService", "Global", logger)
    {
        _mailRepository = mailRepository;
        _characterRepository = characterRepository;
    }

    public override Task OnStartingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("MailService starting...");
        return Task.CompletedTask;
    }

    public override Task OnStoppingAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("MailService stopping...");
        return Task.CompletedTask;
    }

    public override Task<ServiceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ServiceHealthCheckResult.Healthy("MailService is healthy"));
    }

    /// <summary>
    /// 发送邮件（从玩家A到玩家B）
    /// </summary>
    public async Task<SendMailResponse> SendMailAsync(
        string senderId,
        SendMailRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 验证接收者是否存在
            var receiver = await _characterRepository.GetByCharacterIdAsync(request.ReceiverId);
            if (receiver == null)
            {
                Logger.LogWarning("发送邮件失败：接收者不存在 - SenderId={SenderId}, ReceiverId={ReceiverId}",
                    senderId, request.ReceiverId);
                return new SendMailResponse
                {
                    Success = false,
                    ErrorMessage = "接收者不存在"
                };
            }

            // 获取发送者信息
            var sender = await _characterRepository.GetByCharacterIdAsync(senderId);
            if (sender == null)
            {
                Logger.LogWarning("发送邮件失败：发送者不存在 - SenderId={SenderId}", senderId);
                return new SendMailResponse
                {
                    Success = false,
                    ErrorMessage = "发送者不存在"
                };
            }

            // 创建邮件
            var mail = new Mail
            {
                MailId = Guid.NewGuid().ToString(),
                SenderId = senderId,
                SenderName = sender.Name,
                ReceiverId = request.ReceiverId,
                ReceiverName = receiver.Name,
                Title = request.Title,
                Content = request.Content,
                Type = MailType.Player,
                Attachments = request.Attachments,
                SentAt = DateTime.UtcNow,
                ExpireAt = DateTime.UtcNow.AddDays(30), // 30天后过期
                IsRead = false,
                IsAttachmentClaimed = request.Attachments.Count == 0, // 如果没有附件，默认已领取
                IsDeleted = false
            };

            // 保存到数据库
            await _mailRepository.InsertAsync(mail, cancellationToken);

            Logger.LogInformation("邮件发送成功 - MailId={MailId}, From={SenderName}, To={ReceiverName}",
                mail.MailId, mail.SenderName, mail.ReceiverName);

            return new SendMailResponse
            {
                Success = true,
                MailId = mail.MailId
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送邮件时发生异常 - SenderId={SenderId}, ReceiverId={ReceiverId}",
                senderId, request.ReceiverId);
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = $"发送邮件失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 发送系统邮件
    /// </summary>
    public async Task<SendMailResponse> SendSystemMailAsync(
        string receiverId,
        string title,
        string content,
        List<MailAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var receiver = await _characterRepository.GetByCharacterIdAsync(receiverId);
            if (receiver == null)
            {
                return new SendMailResponse
                {
                    Success = false,
                    ErrorMessage = "接收者不存在"
                };
            }

            var mail = new Mail
            {
                MailId = Guid.NewGuid().ToString(),
                SenderId = "system",
                SenderName = "系统",
                ReceiverId = receiverId,
                ReceiverName = receiver.Name,
                Title = title,
                Content = content,
                Type = MailType.System,
                Attachments = attachments ?? new List<MailAttachment>(),
                SentAt = DateTime.UtcNow,
                ExpireAt = DateTime.UtcNow.AddDays(7), // 系统邮件7天后过期
                IsRead = false,
                IsAttachmentClaimed = (attachments == null || attachments.Count == 0),
                IsDeleted = false
            };

            await _mailRepository.InsertAsync(mail, cancellationToken);

            Logger.LogInformation("系统邮件发送成功 - MailId={MailId}, To={ReceiverName}",
                mail.MailId, mail.ReceiverName);

            return new SendMailResponse
            {
                Success = true,
                MailId = mail.MailId
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送系统邮件时发生异常 - ReceiverId={ReceiverId}", receiverId);
            return new SendMailResponse
            {
                Success = false,
                ErrorMessage = $"发送系统邮件失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 获取玩家的邮件列表
    /// </summary>
    public async Task<MailListResponse> GetMailListAsync(
        string playerId,
        GetMailListRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 获取邮件列表
            var mails = await _mailRepository.GetPlayerMailsAsync(
                playerId,
                request.Page,
                request.PageSize,
                request.OnlyUnread,
                cancellationToken);

            // 获取总数和未读数
            var totalCount = await _mailRepository.GetPlayerMailCountAsync(playerId, false, cancellationToken);
            var unreadCount = await _mailRepository.GetUnreadCountAsync(playerId, cancellationToken);

            return new MailListResponse
            {
                Mails = mails,
                TotalCount = (int)totalCount,
                UnreadCount = (int)unreadCount
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取邮件列表失败 - PlayerId={PlayerId}", playerId);
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
    public async Task<Mail?> ReadMailAsync(
        string playerId,
        string mailId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mail = await _mailRepository.GetByMailIdAsync(mailId, cancellationToken);

            if (mail == null)
            {
                Logger.LogWarning("邮件不存在 - MailId={MailId}", mailId);
                return null;
            }

            // 验证邮件是否属于该玩家
            if (mail.ReceiverId != playerId)
            {
                Logger.LogWarning("玩家尝试读取不属于自己的邮件 - PlayerId={PlayerId}, MailId={MailId}",
                    playerId, mailId);
                return null;
            }

            // 标记为已读
            if (!mail.IsRead)
            {
                await _mailRepository.MarkAsReadAsync(mailId, cancellationToken);
                mail.IsRead = true;
            }

            return mail;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "读取邮件失败 - PlayerId={PlayerId}, MailId={MailId}",
                playerId, mailId);
            return null;
        }
    }

    /// <summary>
    /// 领取邮件附件
    /// </summary>
    public async Task<bool> ClaimAttachmentAsync(
        string playerId,
        string mailId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mail = await _mailRepository.GetByMailIdAsync(mailId, cancellationToken);

            if (mail == null || mail.ReceiverId != playerId)
            {
                Logger.LogWarning("无法领取附件：邮件不存在或不属于该玩家 - PlayerId={PlayerId}, MailId={MailId}",
                    playerId, mailId);
                return false;
            }

            if (mail.IsAttachmentClaimed)
            {
                Logger.LogWarning("附件已领取 - PlayerId={PlayerId}, MailId={MailId}",
                    playerId, mailId);
                return false;
            }

            if (mail.Attachments.Count == 0)
            {
                Logger.LogWarning("邮件没有附件 - PlayerId={PlayerId}, MailId={MailId}",
                    playerId, mailId);
                return false;
            }

            // TODO: 这里应该将附件物品添加到玩家背包
            // 目前简化实现，只标记为已领取
            var success = await _mailRepository.ClaimAttachmentAsync(mailId, cancellationToken);

            if (success)
            {
                Logger.LogInformation("附件领取成功 - PlayerId={PlayerId}, MailId={MailId}, AttachmentCount={Count}",
                    playerId, mailId, mail.Attachments.Count);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "领取附件失败 - PlayerId={PlayerId}, MailId={MailId}",
                playerId, mailId);
            return false;
        }
    }

    /// <summary>
    /// 删除邮件
    /// </summary>
    public async Task<bool> DeleteMailAsync(
        string playerId,
        string mailId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mail = await _mailRepository.GetByMailIdAsync(mailId, cancellationToken);

            if (mail == null || mail.ReceiverId != playerId)
            {
                Logger.LogWarning("无法删除邮件：邮件不存在或不属于该玩家 - PlayerId={PlayerId}, MailId={MailId}",
                    playerId, mailId);
                return false;
            }

            var success = await _mailRepository.DeleteMailAsync(mailId, cancellationToken);

            if (success)
            {
                Logger.LogInformation("邮件删除成功 - PlayerId={PlayerId}, MailId={MailId}",
                    playerId, mailId);
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "删除邮件失败 - PlayerId={PlayerId}, MailId={MailId}",
                playerId, mailId);
            return false;
        }
    }

    /// <summary>
    /// 获取未读邮件数量
    /// </summary>
    public async Task<int> GetUnreadCountAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _mailRepository.GetUnreadCountAsync(playerId, cancellationToken);
            return (int)count;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "获取未读邮件数量失败 - PlayerId={PlayerId}", playerId);
            return 0;
        }
    }
}
