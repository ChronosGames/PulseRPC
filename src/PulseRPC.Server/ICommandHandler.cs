using PulseRPC.Network;

namespace PulseRPC.Server;

public interface ICommandHandler<in TCommand> : IPacketHandler
{
    Task HandleAsync(NetworkSession session, TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// 带上下文的命令处理器接口
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
/// <typeparam name="TContext">上下文类型</typeparam>
public interface IContextualCommandHandler<in TCommand, in TContext> : IPacketHandler
{
    /// <summary>
    /// 处理带上下文的命令
    /// </summary>
    /// <param name="session">网络会话</param>
    /// <param name="command">命令对象</param>
    /// <param name="context">上下文对象</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task HandleAsync(NetworkSession session, TCommand command, TContext context, CancellationToken cancellationToken);
}
