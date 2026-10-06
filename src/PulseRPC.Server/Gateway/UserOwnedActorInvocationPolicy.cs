using PulseRPC.Server.Contexts;

namespace PulseRPC.Server.Gateway;

/// <summary>
/// 玩家自有 Actor 准入策略：仅允许白名单中的 (Hub, ProtocolId)，且 Actor key 必须等于已认证 UserId。
/// 房间成员、租户复合键、管理员等权限模型应使用业务自己的策略。
/// </summary>
public sealed class UserOwnedActorInvocationPolicy : IGatewayActorInvocationPolicy
{
    private readonly Dictionary<string, HashSet<ushort>> _allowed;

    /// <summary>复制允许从玩家入口调用的 canonical Hub 和显式协议号白名单。</summary>
    public UserOwnedActorInvocationPolicy(IReadOnlyDictionary<string, IReadOnlyCollection<ushort>> allowedMethods)
    {
        ArgumentNullException.ThrowIfNull(allowedMethods);
        _allowed = new Dictionary<string, HashSet<ushort>>(StringComparer.Ordinal);
        foreach (var pair in allowedMethods)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            _allowed.Add(pair.Key, new HashSet<ushort>(pair.Value));
        }
    }

    /// <inheritdoc />
    public ValueTask EvaluateAsync(GatewayActorInvocationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var caller = context.CallerContext;
        if (caller.SourceType != CallSourceType.ExternalUser || caller.IsExpired ||
            string.IsNullOrWhiteSpace(caller.UserId) ||
            !string.Equals(context.Key, caller.UserId, StringComparison.Ordinal) ||
            !_allowed.TryGetValue(context.Hub, out var methods) || !methods.Contains(context.ProtocolId))
            throw new UnauthorizedAccessException("This player cannot invoke the requested Actor operation.");
        return ValueTask.CompletedTask;
    }
}
