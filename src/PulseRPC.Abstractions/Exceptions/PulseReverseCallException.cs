using System;

namespace PulseRPC;

/// <summary>
/// 服务端→客户端反向 Ask（Reverse Ask / Call）失败异常。
/// </summary>
/// <remarks>
/// <para>
/// 当服务端通过 <c>IHubContext&lt;TReceiver&gt;</c> 发起反向请求（<see cref="PulseRPC.Messaging.MessageType.ReverseRequest"/>）
/// 并等待客户端应答的过程中出现下列情况时抛出：
/// </para>
/// <list type="bullet">
/// <item><description>客户端处理器抛出异常（客户端以 <see cref="PulseRPC.Messaging.MessageType.Error"/> 回传）。</description></item>
/// <item><description>连接在客户端应答前断开（断线兜底）。</description></item>
/// <item><description>客户端未注册对应协议号的处理器。</description></item>
/// </list>
/// <para>超时场景抛出 <see cref="TimeoutException"/>；取消场景抛出 <see cref="OperationCanceledException"/>。</para>
/// </remarks>
public sealed class PulseReverseCallException : Exception
{
    /// <summary>
    /// 由客户端回传的错误代码（当异常源于客户端处理器时可用），否则为 <c>null</c>。
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// 创建反向 Ask 异常。
    /// </summary>
    public PulseReverseCallException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 创建反向 Ask 异常（带错误代码）。
    /// </summary>
    public PulseReverseCallException(string message, string? errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// 创建反向 Ask 异常（带内部异常）。
    /// </summary>
    public PulseReverseCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
