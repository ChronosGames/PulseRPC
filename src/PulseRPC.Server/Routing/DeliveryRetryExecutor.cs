using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PulseRPC.Server.Routing;

/// <summary>
/// <see cref="PulseRPC.DeliveryMode.AtLeastOnce"/>/<see cref="PulseRPC.DeliveryMode.ExactlyOnce"/> 的
/// 重试策略参数（设计文档 §10.3）。
/// </summary>
public sealed class DeliveryRetryOptions
{
    /// <summary>最大尝试次数（含首次），默认 4（即最多 3 次重试）。</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>首次重试前的基础退避延迟，默认 50ms；后续按指数退避翻倍，直至 <see cref="MaxDelay"/>。</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>单次退避延迟的上限，默认 2 秒。</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// 按 <see cref="PulseRPC.DeliveryMode"/> 执行一次投递动作，为 <c>AtLeastOnce</c>/<c>ExactlyOnce</c>
/// 提供"失败自动重试直至确认或次数耗尽"（有界指数退避）；<c>AtMostOnce</c> 保持现状——仅尝试一次，
/// 失败直接向上抛出（§10.3："发送后不重试，失败即上抛"，与既有单节点行为完全一致）。
/// </summary>
public static class DeliveryRetryExecutor
{
    /// <summary>执行一次投递动作，按 <paramref name="delivery"/> 决定是否重试。</summary>
    /// <remarks>
    /// 这是<strong>有界</strong>的"至少一次"：重试次数耗尽后仍失败会把最后一次异常向上抛出，
    /// 并不保证"最终一定送达"（真正无限重试/持久化重投属于 §10.2 在途请求表 + 可靠 Backplane 的
    /// 更大范畴，见路线图 P6 后续与 P7 故障接管硬化）。
    /// </remarks>
    public static async ValueTask ExecuteAsync(
        DeliveryMode delivery,
        DeliveryRetryOptions options,
        Func<CancellationToken, ValueTask> action,
        ILogger logger,
        string operationDescription,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(logger);

        if (delivery == DeliveryMode.AtMostOnce)
        {
            await action(cancellationToken).ConfigureAwait(false);
            return;
        }

        var maxAttempts = Math.Max(1, options.MaxAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var delay = ComputeBackoff(options, attempt);
                logger.LogWarning(
                    ex,
                    "{Operation} 第 {Attempt}/{MaxAttempts} 次投递失败，{DelayMs}ms 后重试（DeliveryMode={Delivery}）",
                    operationDescription, attempt, maxAttempts, (int)delay.TotalMilliseconds, delivery);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan ComputeBackoff(DeliveryRetryOptions options, int attempt)
    {
        var exponentialMillis = options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var cappedMillis = Math.Min(exponentialMillis, options.MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(cappedMillis);
    }
}
