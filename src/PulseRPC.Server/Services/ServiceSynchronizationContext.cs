using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server;

/// <summary>
/// Service 同步上下文 - 让 await 自动让出队列执行权
/// </summary>
/// <remarks>
/// 核心机制：
/// 1. 当方法中的 await 完成时，CLR 会调用 Post 方法来调度延续
/// 2. 我们在 Post 中将延续重新排队到消息队列
/// 3. 这样 await 后的代码会重新进入队列，而不是在线程池执行
/// 4. 保证了 Actor 模型的线程安全性
///
/// 示例：
/// <code>
/// public async Task ProcessAsync()
/// {
///     // 步骤 1：在队列线程执行
///     Console.WriteLine($"Before await: Thread={Thread.CurrentThread.ManagedThreadId}");
///
///     // 步骤 2：await 时让出队列，其他消息可以处理
///     var result = await _repository.GetDataAsync();
///
///     // 步骤 3：IO 完成后，Post 被调用，延续重新排队
///     // 步骤 4：延续从队列中取出，仍在队列线程执行
///     Console.WriteLine($"After await: Thread={Thread.CurrentThread.ManagedThreadId}");
///     // 两次 Thread ID 相同（逻辑上的同一线程）
/// }
/// </code>
/// </remarks>
public sealed class ServiceSynchronizationContext : SynchronizationContext
{
    private readonly ChannelWriter<(SendOrPostCallback Callback, object? State)> _continuationWriter;
    private readonly string _serviceName;
    private readonly PID _servicePID;
    private readonly ILogger _logger;
    private volatile int _continuationCount;

    public ServiceSynchronizationContext(
        ChannelWriter<(SendOrPostCallback, object?)> continuationWriter,
        string serviceName,
        PID servicePID,
        ILogger logger)
    {
        _continuationWriter = continuationWriter ?? throw new ArgumentNullException(nameof(continuationWriter));
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        _servicePID = servicePID;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Post 方法会在 await 完成后被 CLR 调用，用于调度延续
    /// </summary>
    /// <remarks>
    /// 这是整个机制的核心：
    /// - 当 await task 完成时，CLR 会调用 SynchronizationContext.Post
    /// - 我们将延续排队到消息队列，而不是让它在线程池执行
    /// - 这样恢复执行时仍在队列线程，保证线程安全
    /// </remarks>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        var continuationId = Interlocked.Increment(ref _continuationCount);

        _logger.LogTrace(
            "Posting continuation #{ContinuationId} - Service: {ServiceName}, PID: {PID}",
            continuationId, _serviceName, _servicePID);

        // ✅ 关键：将延续重新排队到消息队列
        if (!_continuationWriter.TryWrite((d, state)))
        {
            _logger.LogWarning(
                "Failed to enqueue continuation #{ContinuationId} - Service: {ServiceName}, PID: {PID}. " +
                "Falling back to ThreadPool.",
                continuationId, _serviceName, _servicePID);

            // 回退：如果队列满了，使用线程池（这会失去线程安全保证）
            ThreadPool.QueueUserWorkItem(s => d(s), state);
        }
    }

    /// <summary>
    /// Send 方法（同步执行）- 直接在当前线程执行
    /// </summary>
    /// <remarks>
    /// Send 用于同步执行，通常不会被 async/await 调用
    /// </remarks>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        _logger.LogTrace(
            "Sending (sync) callback - Service: {ServiceName}, PID: {PID}",
            _serviceName, _servicePID);

        d(state);
    }

    /// <summary>
    /// 创建副本 - 用于嵌套的同步上下文
    /// </summary>
    public override SynchronizationContext CreateCopy()
    {
        return new ServiceSynchronizationContext(
            _continuationWriter,
            _serviceName,
            _servicePID,
            _logger);
    }

    /// <summary>
    /// 获取当前延续数量（用于监控）
    /// </summary>
    public int ContinuationCount => _continuationCount;
}
