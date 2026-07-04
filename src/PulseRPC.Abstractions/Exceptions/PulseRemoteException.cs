using System;

namespace PulseRPC;

/// <summary>
/// 客户端→服务端正向 RPC（Request/Response）失败异常。
/// </summary>
/// <remarks>
/// 当服务端 Hub 方法执行抛出异常时，服务端以 <see cref="PulseRPC.Messaging.MessageType.Error"/>
/// 回传 <see cref="PulseRPC.Messaging.ErrorResponse"/>；客户端据此在等待中的调用上抛出本异常，
/// 而不是让调用方一直等到 <see cref="TimeoutException"/>（见 §11 回归发现）。
/// </remarks>
public sealed class PulseRemoteException : Exception
{
    /// <summary>
    /// 服务端回传的错误代码（通常为异常类型名或自定义错误码）。
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// 服务端回传的错误详情（异常的完整类型名，可能附带堆栈信息，取决于服务端配置）。
    /// </summary>
    public string? ErrorDetails { get; }

    public PulseRemoteException(string message, string? errorCode = null, string? errorDetails = null)
        : base(message)
    {
        ErrorCode = errorCode;
        ErrorDetails = errorDetails;
    }
}
