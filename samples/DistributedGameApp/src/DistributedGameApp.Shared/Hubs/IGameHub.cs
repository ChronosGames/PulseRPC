using DistributedGameApp.Shared.Domain.Mail;
using DistributedGameApp.Shared.Messages;
using PulseRPC;
using System.Threading.Tasks;

namespace DistributedGameApp.Shared.Hubs;

/// <summary>
/// 游戏服务 Hub - 处理登录、角色管理、匹配、邮件等
/// </summary>
/// <remarks>
/// 这个接口定义了游戏网关服务的所有方法。
/// GameServer 负责：
/// - 玩家登录和认证
/// - 角色创建和管理
/// - 匹配队列管理
/// - 在线状态管理
/// - 邮件系统
/// <para>
/// [Channel("GameServer")] 表示这个接口由 GameServer 提供。
/// [Authorize(Role = RoleTypes.External)] 表示需要外部玩家认证。
/// </para>
/// </remarks>
[Channel("GameServer")]
[Authorize(Role = RoleTypes.External)]
public interface IGameHub : IPulseHub
{
    /// <summary>
    /// 玩家登录
    /// </summary>
    /// <param name="request">登录请求</param>
    /// <returns>登录响应（包含访问令牌）</returns>
    /// <remarks>登录接口允许匿名访问</remarks>
    [AllowAnonymous]
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// 创建新角色
    /// </summary>
    /// <param name="request">创建角色请求</param>
    /// <returns>创建的角色信息</returns>
    Task<CharacterInfo> CreateCharacterAsync(CreateCharacterRequest request);

    /// <summary>
    /// 获取角色信息
    /// </summary>
    /// <param name="characterId">角色ID</param>
    /// <returns>角色信息</returns>
    Task<CharacterInfo?> GetCharacterAsync(string characterId);

    /// <summary>
    /// 获取玩家的所有角色列表
    /// </summary>
    /// <returns>角色列表</returns>
    Task<CharacterInfo[]> GetCharacterListAsync();

    /// <summary>
    /// 删除角色
    /// </summary>
    /// <param name="characterId">角色ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteCharacterAsync(string characterId);

    /// <summary>
    /// 请求匹配
    /// </summary>
    /// <param name="request">匹配请求</param>
    /// <returns>匹配响应</returns>
    Task<MatchmakingResponse> RequestMatchAsync(MatchmakingRequest request);

    /// <summary>
    /// 取消匹配
    /// </summary>
    /// <param name="ticketId">匹配票据ID</param>
    /// <returns>是否成功取消</returns>
    Task<bool> CancelMatchAsync(string ticketId);

    /// <summary>
    /// 确认加入战斗
    /// </summary>
    /// <param name="battleId">战斗ID</param>
    /// <returns>是否确认成功</returns>
    Task<bool> ConfirmBattleAsync(string battleId);

    /// <summary>
    /// 心跳（保持在线状态）
    /// </summary>
    /// <returns>服务器时间戳</returns>
    Task<long> HeartbeatAsync();

    /// <summary>
    /// 更新在线状态
    /// </summary>
    /// <param name="status">新状态</param>
    /// <returns>是否成功</returns>
    Task<bool> UpdateOnlineStatusAsync(OnlineStatus status);

    // ========== 邮件系统 ==========

    /// <summary>
    /// 发送邮件给其他玩家
    /// </summary>
    /// <param name="request">发送邮件请求</param>
    /// <returns>发送结果</returns>
    Task<SendMailResponse> SendMailAsync(SendMailRequest request);

    /// <summary>
    /// 获取邮件列表
    /// </summary>
    /// <param name="request">获取邮件列表请求</param>
    /// <returns>邮件列表</returns>
    Task<MailListResponse> GetMailListAsync(GetMailListRequest request);

    /// <summary>
    /// 读取邮件
    /// </summary>
    /// <param name="mailId">邮件ID</param>
    /// <returns>邮件详情</returns>
    Task<Mail?> ReadMailAsync(string mailId);

    /// <summary>
    /// 领取邮件附件
    /// </summary>
    /// <param name="mailId">邮件ID</param>
    /// <returns>是否成功</returns>
    Task<bool> ClaimMailAttachmentAsync(string mailId);

    /// <summary>
    /// 删除邮件
    /// </summary>
    /// <param name="mailId">邮件ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteMailAsync(string mailId);

    /// <summary>
    /// 获取未读邮件数量
    /// </summary>
    /// <returns>未读数量</returns>
    Task<int> GetUnreadMailCountAsync();
}
