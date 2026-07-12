namespace PulseRPC.Server.Configuration;

/// <summary>
/// 未接入服务器消息主路径的历史背压策略。
/// </summary>
/// <remarks>
/// 固定 shard 队列当前采用有界、立即拒绝语义；此枚举不会改变运行时行为。
/// </remarks>
[Obsolete("This enum is not wired to the fixed-shard message engine. Queue-full behavior is bounded immediate rejection.", false)]
public enum BackpressureStrategy
{
    /// <summary>
    /// 阻塞等待 - 当队列满时，阻塞发送者直到队列有空间
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 不能丢失消息的业务（如支付、订单）
    /// - 可以容忍发送者暂时阻塞
    ///
    /// 特点：
    /// - ✅ 保证消息不丢失
    /// - ⚠️ 可能导致调用者阻塞
    /// - ⚠️ 高负载时可能造成级联阻塞
    /// </remarks>
    Block = 0,

    /// <summary>
    /// 丢弃最旧消息 - 当队列满时，移除队列中最旧的消息，插入新消息
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 日志收集服务（旧日志可丢弃）
    /// - 历史监控数据（最新数据更重要）
    /// - 缓存更新通知
    ///
    /// 特点：
    /// - ✅ 不阻塞发送者
    /// - ✅ 保证最新消息被处理
    /// - ⚠️ 可能丢失旧消息
    /// </remarks>
    DropOldest = 1,

    /// <summary>
    /// 丢弃最新消息 - 当队列满时，拒绝新消息，保留队列中的消息
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 任务队列（保证已入队任务完成）
    /// - 顺序敏感的业务（如聊天消息）
    /// - 先到先服务的场景
    ///
    /// 特点：
    /// - ✅ 不阻塞发送者
    /// - ✅ 保证已入队消息被处理
    /// - ⚠️ 可能丢失新消息
    /// </remarks>
    DropNewest = 2,

    /// <summary>
    /// 拒绝新消息 - 当队列满时，拒绝新消息并抛出异常
    /// </summary>
    /// <remarks>
    /// 适用场景：
    /// - 需要明确知道消息是否成功入队
    /// - 高可靠性业务（如支付服务）
    /// - 需要重试逻辑的场景
    ///
    /// 特点：
    /// - ✅ 明确的失败反馈
    /// - ✅ 调用者可以实现重试逻辑
    /// - ⚠️ 需要调用者处理异常
    /// </remarks>
    Reject = 3
}
