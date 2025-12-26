using PulseRPC.Server.Contexts;
using PulseRPC.Transport;

namespace PulseRPC.Server.Hubs;

/// <summary>
/// PulseHub 基类 - 提供常用的上下文访问属性
/// </summary>
/// <remarks>
/// <para>提供简化的上下文访问，减少 Hub 实现中的样板代码。</para>
/// <para>所有属性都是从 <see cref="PulseContext"/> 中获取的便捷访问器。</para>
/// <para>
/// <strong>使用示例</strong>：
/// </para>
/// <code>
/// public class GameHub : PulseHubBase, IGameHub
/// {
///     private readonly IServiceAccessor&lt;PlayerService&gt; _playerService;
///
///     public GameHub(IServiceAccessor&lt;PlayerService&gt; playerService)
///     {
///         _playerService = playerService;
///     }
///
///     public async Task&lt;PlayerInfo&gt; GetPlayerInfoAsync()
///     {
///         // 使用基类提供的 UserId 属性（自动验证认证状态）
///         return await _playerService.ExecuteAsync(UserId,
///             s => s.GetInfoAsync());
///     }
///
///     public async Task&lt;bool&gt; TryDoSomethingAsync()
///     {
///         // 使用可空的 CurrentUserId 进行条件检查
///         if (!IsAuthenticated)
///         {
///             return false;
///         }
///         // ...
///     }
/// }
/// </code>
/// </remarks>
public abstract class PulseHubBase
{
    /// <summary>
    /// 当前连接 ID
    /// </summary>
    /// <remarks>
    /// 每个客户端连接都有唯一的连接 ID，可用于：
    /// <list type="bullet">
    /// <item><description>向特定连接推送消息</description></item>
    /// <item><description>连接映射管理</description></item>
    /// <item><description>日志追踪</description></item>
    /// </list>
    /// </remarks>
    protected static string? ConnectionId => PulseContext.CurrentConnectionId;

    /// <summary>
    /// 当前用户 ID（可能为 null）
    /// </summary>
    /// <remarks>
    /// <para>如果用户未认证，此属性返回 null。</para>
    /// <para>适用于需要检查认证状态的场景，如 <c>[AllowAnonymous]</c> 方法。</para>
    /// </remarks>
    protected static string? CurrentUserId => PulseContext.CurrentUserId;

    /// <summary>
    /// 当前用户 ID（必须已认证，否则抛出异常）
    /// </summary>
    /// <remarks>
    /// <para>如果用户未认证，此属性抛出 <see cref="InvalidOperationException"/>。</para>
    /// <para>适用于需要认证的方法，可安全使用而无需手动检查 null。</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">用户未认证时抛出</exception>
    protected static string UserId => PulseContext.CurrentUserId
        ?? throw new InvalidOperationException("User not authenticated. This method requires authentication.");

    /// <summary>
    /// 当前请求上下文
    /// </summary>
    /// <remarks>
    /// 提供完整的请求上下文信息，包括：
    /// <list type="bullet">
    /// <item><description>用户身份信息</description></item>
    /// <item><description>连接信息</description></item>
    /// <item><description>传输层信息</description></item>
    /// <item><description>扩展数据</description></item>
    /// </list>
    /// </remarks>
    protected static IPulseContext? Context => PulseContext.Current;

    /// <summary>
    /// 当前传输连接
    /// </summary>
    /// <remarks>
    /// 提供底层传输连接的访问，可用于：
    /// <list type="bullet">
    /// <item><description>获取连接元数据</description></item>
    /// <item><description>直接发送数据</description></item>
    /// <item><description>连接状态检查</description></item>
    /// </list>
    /// </remarks>
    protected static IServerTransport? Transport => PulseContext.CurrentTransport;

    /// <summary>
    /// 检查当前用户是否已认证
    /// </summary>
    /// <remarks>
    /// 等价于 <c>CurrentUserId != null</c>，但语义更清晰。
    /// </remarks>
    protected static bool IsAuthenticated => PulseContext.CurrentUserId != null;

    /// <summary>
    /// 确保用户已认证，否则抛出异常
    /// </summary>
    /// <remarks>
    /// <para>适用于需要在方法开头进行认证检查的场景。</para>
    /// <para>等价于访问 <see cref="UserId"/> 属性但不使用其值。</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">用户未认证时抛出</exception>
    protected static void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("User not authenticated. This method requires authentication.");
        }
    }

    /// <summary>
    /// 尝试获取当前用户 ID
    /// </summary>
    /// <param name="userId">输出用户 ID</param>
    /// <returns>用户是否已认证</returns>
    /// <remarks>
    /// 适用于需要条件处理认证状态的场景：
    /// <code>
    /// if (TryGetUserId(out var userId))
    /// {
    ///     // 用户已认证，使用 userId
    /// }
    /// else
    /// {
    ///     // 用户未认证，执行匿名逻辑
    /// }
    /// </code>
    /// </remarks>
    protected static bool TryGetUserId(out string userId)
    {
        userId = PulseContext.CurrentUserId ?? string.Empty;
        return !string.IsNullOrEmpty(userId);
    }

    /// <summary>
    /// 获取当前连接 ID（必须存在，否则抛出异常）
    /// </summary>
    /// <exception cref="InvalidOperationException">连接 ID 不存在时抛出</exception>
    protected static string RequireConnectionId => PulseContext.CurrentConnectionId
        ?? throw new InvalidOperationException("No connection context available.");
}
