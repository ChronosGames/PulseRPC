using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC;
using PulseRPC.Server.Routing;
using Xunit;

namespace PulseRPC.Server.Tests.Routing;

/// <summary>
/// 回归测试：<see cref="DeliveryRetryExecutor"/>（§P6/§10.3）—— <see cref="DeliveryMode.AtMostOnce"/>
/// 必须保持"仅尝试一次、失败即上抛"的既有单节点行为；<see cref="DeliveryMode.AtLeastOnce"/>/
/// <see cref="DeliveryMode.ExactlyOnce"/> 必须在失败时按有界次数 + 退避重试，直至成功或次数耗尽。
/// </summary>
public class DeliveryRetryExecutorTests
{
    private static DeliveryRetryOptions FastOptions() => new()
    {
        MaxAttempts = 4,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        MaxDelay = TimeSpan.FromMilliseconds(5),
    };

    [Fact]
    public async Task AtMostOnce_OnSuccess_InvokesActionExactlyOnce()
    {
        var callCount = 0;

        await DeliveryRetryExecutor.ExecuteAsync(
            DeliveryMode.AtMostOnce, FastOptions(),
            _ => { callCount++; return default; },
            NullLogger.Instance, "test-op", CancellationToken.None);

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task AtMostOnce_OnFailure_ThrowsImmediately_WithoutRetrying()
    {
        var callCount = 0;

        var act = async () => await DeliveryRetryExecutor.ExecuteAsync(
            DeliveryMode.AtMostOnce, FastOptions(),
            _ =>
            {
                callCount++;
                throw new InvalidOperationException("boom");
            },
            NullLogger.Instance, "test-op", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(1, "AtMostOnce 不应重试，必须与既有单节点行为一致");
    }

    [Theory]
    [InlineData(DeliveryMode.AtLeastOnce)]
    [InlineData(DeliveryMode.ExactlyOnce)]
    public async Task RetryableModes_OnTransientFailureThenSuccess_MustRetryUntilSucceeding(DeliveryMode delivery)
    {
        var callCount = 0;

        await DeliveryRetryExecutor.ExecuteAsync(
            delivery, FastOptions(),
            _ =>
            {
                callCount++;
                if (callCount < 3)
                {
                    throw new InvalidOperationException("transient");
                }

                return default;
            },
            NullLogger.Instance, "test-op", CancellationToken.None);

        callCount.Should().Be(3, "前两次失败后应重试，第三次成功后不应再继续尝试");
    }

    [Theory]
    [InlineData(DeliveryMode.AtLeastOnce)]
    [InlineData(DeliveryMode.ExactlyOnce)]
    public async Task RetryableModes_WhenAllAttemptsFail_MustThrowLastExceptionAfterMaxAttempts(DeliveryMode delivery)
    {
        var callCount = 0;
        var options = FastOptions();

        var act = async () => await DeliveryRetryExecutor.ExecuteAsync(
            delivery, options,
            _ =>
            {
                callCount++;
                throw new InvalidOperationException($"failure-{callCount}");
            },
            NullLogger.Instance, "test-op", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be($"failure-{options.MaxAttempts}");
        callCount.Should().Be(options.MaxAttempts, "应恰好尝试 MaxAttempts 次（含首次），不多不少");
    }

    [Fact]
    public async Task RetryableMode_RespectsCancellation_AndStopsRetrying()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;

        var act = async () => await DeliveryRetryExecutor.ExecuteAsync(
            DeliveryMode.AtLeastOnce, FastOptions(),
            _ =>
            {
                callCount++;
                cts.Cancel();
                throw new InvalidOperationException("boom");
            },
            NullLogger.Instance, "test-op", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callCount.Should().Be(1, "取消令牌被触发后不应再继续重试");
    }

    [Fact]
    public async Task RetryableMode_BackoffDelaysGrowWithEachAttempt_UpToMaxDelay()
    {
        var options = new DeliveryRetryOptions
        {
            MaxAttempts = 4,
            BaseDelay = TimeSpan.FromMilliseconds(20),
            MaxDelay = TimeSpan.FromMilliseconds(35),
        };
        var attemptTimestamps = new List<long>();
        var callCount = 0;

        await DeliveryRetryExecutor.ExecuteAsync(
            DeliveryMode.AtLeastOnce, options,
            _ =>
            {
                attemptTimestamps.Add(Environment.TickCount64);
                callCount++;
                if (callCount < 4)
                {
                    throw new InvalidOperationException("transient");
                }

                return default;
            },
            NullLogger.Instance, "test-op", CancellationToken.None);

        attemptTimestamps.Should().HaveCount(4);
        // 退避是指数增长但有上限：第 1→2 次间隔应明显短于第 2→3 次（除非已触顶 MaxDelay），
        // 且没有任何一次间隔超过 MaxDelay 太多（预留调度抖动余量）。
        for (var i = 1; i < attemptTimestamps.Count; i++)
        {
            var gap = attemptTimestamps[i] - attemptTimestamps[i - 1];
            gap.Should().BeLessThan((long)options.MaxDelay.TotalMilliseconds + 200, "退避延迟不应无界增长，必须被 MaxDelay 封顶");
        }
    }
}
