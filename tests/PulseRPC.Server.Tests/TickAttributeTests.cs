using FluentAssertions;
using PulseRPC.Server.Contexts;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 验证 [Tick(hz)] 固定帧驱动运行时（对应「契约即接口·HubActor 统一模型」§4.2 声明式注解）：
/// <list type="bullet">
/// <item>标注 [Tick] 的服务在 StartAsync 后会以指定频率周期性调用 OnTickAsync；</item>
/// <item>StopAsync 后不再触发 tick；</item>
/// <item>tick 回调在串行邮箱内执行，彼此不重叠；</item>
/// <item>未标注 [Tick] 的服务不会触发 OnTickAsync。</item>
/// </list>
/// </summary>
public class TickAttributeTests
{
    [Tick(50)] // 每秒 50 帧（20ms 一帧）
    private sealed class TickingService : PulseServiceBase
    {
        private int _tickCount;
        private int _concurrentTicks;
        private int _maxConcurrentTicks;

        public int TickCount => Volatile.Read(ref _tickCount);
        public int MaxConcurrentTicks => Volatile.Read(ref _maxConcurrentTicks);
        public CallSourceType? ObservedSourceType;

        public TickingService()
            : base("Ticking", "ticking-1", logger: null, executionOptions: ServiceExecutionOptions.Actor)
        {
        }

        protected override async Task OnTickAsync(CancellationToken cancellationToken)
        {
            ObservedSourceType = GetCurrentContext()?.SourceType;

            var concurrent = Interlocked.Increment(ref _concurrentTicks);
            int current;
            while (concurrent > (current = Volatile.Read(ref _maxConcurrentTicks)))
            {
                Interlocked.CompareExchange(ref _maxConcurrentTicks, concurrent, current);
            }

            try
            {
                await Task.Delay(5, cancellationToken);
                Interlocked.Increment(ref _tickCount);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentTicks);
            }
        }
    }

    private sealed class NonTickingService : PulseServiceBase
    {
        private int _tickCount;
        public int TickCount => Volatile.Read(ref _tickCount);

        public NonTickingService()
            : base("NonTicking", "nonticking-1", logger: null, executionOptions: ServiceExecutionOptions.Actor)
        {
        }

        protected override Task OnTickAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _tickCount);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TickAnnotatedService_InvokesOnTickPeriodically()
    {
        await using var svc = new TickingService();
        await svc.StartAsync();

        await Task.Delay(250);

        await svc.StopAsync();

        // 250ms @ 50Hz 理论约 12 帧；放宽下界以避免 CI 抖动导致的偶发失败。
        svc.TickCount.Should().BeGreaterThan(2, "标注 [Tick(50)] 的服务应被周期性驱动");
    }

    [Fact]
    public async Task TickCallbacks_DoNotOverlap()
    {
        await using var svc = new TickingService();
        await svc.StartAsync();

        await Task.Delay(250);

        await svc.StopAsync();

        svc.MaxConcurrentTicks.Should().Be(1, "tick 回调经串行邮箱投递，必须彼此串行、不重叠");
    }

    [Fact]
    public async Task TickStops_AfterStopAsync()
    {
        var svc = new TickingService();
        await svc.StartAsync();
        await Task.Delay(120);
        await svc.StopAsync();

        var countAfterStop = svc.TickCount;
        await Task.Delay(120);

        svc.TickCount.Should().Be(countAfterStop, "StopAsync 之后不应再有新的 tick");

        await svc.DisposeAsync();
    }

    [Fact]
    public async Task ServiceWithoutTickAttribute_NeverTicks()
    {
        await using var svc = new NonTickingService();
        await svc.StartAsync();

        await Task.Delay(120);

        await svc.StopAsync();

        svc.TickCount.Should().Be(0, "未标注 [Tick] 的服务不应触发 OnTickAsync");
    }

    [Fact]
    public async Task TickCallbacks_RunUnderSystemTimerContext()
    {
        await using var svc = new TickingService();
        await svc.StartAsync();

        await Task.Delay(150);

        await svc.StopAsync();

        svc.ObservedSourceType.Should().Be(CallSourceType.SystemTimer,
            "tick 回调应作为系统定时器调用执行，以接入既有权限绕过设计");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TickAttribute_RejectsNonPositiveOrNonFiniteHz(double hz)
    {
        var act = () => new TickAttribute(hz);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TickAttribute_ComputesInterval()
    {
        new TickAttribute(50).Interval.Should().Be(TimeSpan.FromSeconds(1.0 / 50));
        new TickAttribute(0.5).Interval.Should().Be(TimeSpan.FromSeconds(2));
    }
}
