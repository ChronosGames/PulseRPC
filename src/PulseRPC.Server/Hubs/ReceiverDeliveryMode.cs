namespace PulseRPC.Server;

/// <summary>
/// 控制生成的 Receiver 推送代理如何处理单个目标的非取消发送错误。
/// </summary>
/// <remarks>
/// 无论选择哪种模式，<see cref="System.OperationCanceledException"/> 都会传播给调用方。
/// 返回响应的 Receiver Ask 调用也始终传播错误，不受此模式影响。
/// </remarks>
public enum ReceiverDeliveryMode : byte
{
    /// <summary>
    /// 尽力投递：继续向其它目标发送，并忽略单个目标的非取消发送错误。
    /// </summary>
    BestEffort = 0,

    /// <summary>
    /// 严格投递：任一目标发送失败都会使返回的任务失败。
    /// </summary>
    Strict = 1,
}
