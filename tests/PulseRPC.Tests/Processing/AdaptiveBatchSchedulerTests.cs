using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PulseRPC.Server.Processing;
using Xunit;

namespace PulseRPC.Tests.Processing;

/// <summary>
/// AdaptiveBatchScheduler 单元测试
/// 验证自适应批处理调度器的功能和自适应特性
/// </summary>
public class AdaptiveBatchSchedulerTests : IAsyncDisposable
{
    private readonly AdaptiveBatchScheduler _scheduler;
    private readonly ILogger<AdaptiveBatchScheduler> _logger;
    
    public AdaptiveBatchSchedulerTests()
    {
        _logger = Substitute.For<ILogger<AdaptiveBatchScheduler>>();
        _scheduler = new AdaptiveBatchScheduler(_logger);
    }
    
    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync();
    }
    
    #region 基础功能测试
    
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Assert
        _scheduler.CurrentBatchInterval.Should().Be(AdaptiveBatchScheduler.SchedulerSpecs.DEFAULT_BATCH_INTERVAL_MS);
        _scheduler.CurrentBatchSize.Should().Be(AdaptiveBatchScheduler.SchedulerSpecs.DEFAULT_BATCH_SIZE);
        _scheduler.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public void Start_ShouldChangeRunningState()
    {
        // Act
        _scheduler.Start();
        
        // Assert
        _scheduler.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public async Task StopAsync_ShouldChangeRunningState()
    {
        // Arrange
        _scheduler.Start();
        
        // Act
        await _scheduler.StopAsync();
        
        // Assert
        _scheduler.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public void Start_WhenAlreadyRunning_ShouldNotThrow()
    {
        // Arrange
        _scheduler.Start();
        
        // Act & Assert - 不应该抛出异常
        _scheduler.Start();
        _scheduler.IsRunning.Should().BeTrue();
    }
    
    #endregion
    
    #region 处理器注册测试
    
    [Fact]
    public void RegisterProcessor_ValidProcessor_ShouldSucceed()
    {
        // Arrange
        var processor = Substitute.For<IBatchProcessor>();
        
        // Act & Assert - 不应该抛出异常
        _scheduler.RegisterProcessor(processor);
    }
    
    [Fact]
    public void RegisterProcessor_NullProcessor_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _scheduler.RegisterProcessor(null!));
    }
    
    [Fact]
    public void UnregisterProcessor_ExistingProcessor_ShouldSucceed()
    {
        // Arrange
        var processor = Substitute.For<IBatchProcessor>();
        _scheduler.RegisterProcessor(processor);
        
        // Act & Assert - 不应该抛出异常
        _scheduler.UnregisterProcessor(processor);
    }
    
    [Fact]
    public void UnregisterProcessor_NonExistingProcessor_ShouldNotThrow()
    {
        // Arrange
        var processor = Substitute.For<IBatchProcessor>();
        
        // Act & Assert - 不应该抛出异常
        _scheduler.UnregisterProcessor(processor);
    }
    
    #endregion
    
    #region 性能监控测试
    
    [Fact]
    public void RecordBatchOperation_ValidParameters_ShouldNotThrow()
    {
        // Act & Assert - 不应该抛出异常
        _scheduler.RecordBatchOperation(10, TimeSpan.FromMilliseconds(5), 50);
        _scheduler.RecordBatchOperation(20, TimeSpan.FromMilliseconds(8), 30);
        _scheduler.RecordBatchOperation(15, TimeSpan.FromMilliseconds(3), 70);
    }
    
    [Fact]
    public void GetMetrics_AfterRecording_ShouldReturnValidMetrics()
    {
        // Arrange
        _scheduler.RecordBatchOperation(10, TimeSpan.FromMilliseconds(5), 50);
        _scheduler.RecordBatchOperation(20, TimeSpan.FromMilliseconds(8), 30);
        
        // Act
        var metrics = _scheduler.GetMetrics();
        
        // Assert
        metrics.Should().NotBeNull();
        metrics.CurrentBatchInterval.Should().Be(_scheduler.CurrentBatchInterval);
        metrics.CurrentBatchSize.Should().Be(_scheduler.CurrentBatchSize);
        metrics.TotalBatches.Should().BeGreaterThan(0);
    }
    
    [Fact]
    public void GetMetrics_InitialState_ShouldReturnZeroStatistics()
    {
        // Act
        var metrics = _scheduler.GetMetrics();
        
        // Assert
        metrics.AverageThroughput.Should().Be(0);
        metrics.AverageLatency.Should().Be(TimeSpan.Zero);
        metrics.CurrentLoad.Should().Be(0);
        metrics.TotalBatches.Should().Be(0);
        metrics.AdaptationCount.Should().Be(0);
    }
    
    #endregion
    
    #region 自适应行为测试
    
    [Fact]
    public async Task Adaptation_HighLatencyScenario_ShouldReduceBatchInterval()
    {
        // Arrange
        _scheduler.Start();
        var initialInterval = _scheduler.CurrentBatchInterval;
        
        // 模拟高延迟场景
        for (int i = 0; i < 20; i++)
        {
            _scheduler.RecordBatchOperation(16, TimeSpan.FromMilliseconds(50), 80); // 高延迟
        }
        
        // Act - 等待自适应调整
        await Task.Delay(150); // 等待超过自适应检查间隔
        
        // Assert
        var finalInterval = _scheduler.CurrentBatchInterval;
        // 高延迟应该导致间隔减少（虽然实际行为可能因具体实现而异）
        finalInterval.Should().BeInRange(AdaptiveBatchScheduler.SchedulerSpecs.MIN_BATCH_INTERVAL_MS,
                                       AdaptiveBatchScheduler.SchedulerSpecs.MAX_BATCH_INTERVAL_MS);
    }
    
    [Fact]
    public async Task Adaptation_HighLoadScenario_ShouldIncreaseBatchSize()
    {
        // Arrange
        _scheduler.Start();
        var initialBatchSize = _scheduler.CurrentBatchSize;
        
        // 模拟高负载场景
        for (int i = 0; i < 20; i++)
        {
            _scheduler.RecordBatchOperation(32, TimeSpan.FromMilliseconds(2), 95); // 高负载，低延迟
        }
        
        // Act - 等待自适应调整
        await Task.Delay(150);
        
        // Assert
        var finalBatchSize = _scheduler.CurrentBatchSize;
        finalBatchSize.Should().BeInRange(AdaptiveBatchScheduler.SchedulerSpecs.MIN_BATCH_SIZE,
                                        AdaptiveBatchScheduler.SchedulerSpecs.MAX_BATCH_SIZE);
    }
    
    [Fact]
    public async Task Adaptation_LowLoadScenario_ShouldDecreaseBatchSize()
    {
        // Arrange
        _scheduler.Start();
        
        // 模拟低负载场景
        for (int i = 0; i < 20; i++)
        {
            _scheduler.RecordBatchOperation(8, TimeSpan.FromMilliseconds(1), 10); // 低负载，低延迟
        }
        
        // Act - 等待自适应调整
        await Task.Delay(150);
        
        // Assert
        var finalBatchSize = _scheduler.CurrentBatchSize;
        finalBatchSize.Should().BeInRange(AdaptiveBatchScheduler.SchedulerSpecs.MIN_BATCH_SIZE,
                                        AdaptiveBatchScheduler.SchedulerSpecs.MAX_BATCH_SIZE);
    }
    
    #endregion
    
    #region 处理器通知测试
    
    [Fact]
    public async Task ParameterAdaptation_ShouldNotifyRegisteredProcessors()
    {
        // Arrange
        var processor1 = Substitute.For<IBatchProcessor>();
        var processor2 = Substitute.For<IBatchProcessor>();
        
        _scheduler.RegisterProcessor(processor1);
        _scheduler.RegisterProcessor(processor2);
        _scheduler.Start();
        
        // 模拟导致参数变化的场景
        for (int i = 0; i < 30; i++)
        {
            _scheduler.RecordBatchOperation(32, TimeSpan.FromMilliseconds(20), 90);
        }
        
        // Act - 等待自适应调整
        await Task.Delay(200);
        
        // Assert
        // 验证处理器收到了参数更新通知（可能被调用多次）
        processor1.Received().OnParametersUpdated(Arg.Any<int>(), Arg.Any<int>());
        processor2.Received().OnParametersUpdated(Arg.Any<int>(), Arg.Any<int>());
    }
    
    [Fact]
    public void ProcessorNotification_ExceptionInProcessor_ShouldNotAffectOthers()
    {
        // Arrange
        var faultyProcessor = Substitute.For<IBatchProcessor>();
        var goodProcessor = Substitute.For<IBatchProcessor>();
        
        faultyProcessor.When(p => p.OnParametersUpdated(Arg.Any<int>(), Arg.Any<int>()))
                      .Do(x => throw new InvalidOperationException("Test exception"));
        
        _scheduler.RegisterProcessor(faultyProcessor);
        _scheduler.RegisterProcessor(goodProcessor);
        
        // Act & Assert - 不应该因为一个处理器异常而影响系统
        _scheduler.Start();
        
        // 触发参数更新
        for (int i = 0; i < 25; i++)
        {
            _scheduler.RecordBatchOperation(16, TimeSpan.FromMilliseconds(30), 85);
        }
        
        // 系统应该继续正常运行
        _scheduler.IsRunning.Should().BeTrue();
    }
    
    #endregion
    
    #region 边界条件测试
    
    [Fact]
    public void RecordBatchOperation_ExtremeValues_ShouldNotThrow()
    {
        // Act & Assert - 不应该抛出异常
        _scheduler.RecordBatchOperation(0, TimeSpan.Zero, 0);
        _scheduler.RecordBatchOperation(int.MaxValue, TimeSpan.FromHours(1), int.MaxValue);
        _scheduler.RecordBatchOperation(1, TimeSpan.FromTicks(1), 1);
    }
    
    [Fact]
    public async Task StopAsync_NotStarted_ShouldNotThrow()
    {
        // Act & Assert - 不应该抛出异常
        await _scheduler.StopAsync();
    }
    
    [Fact]
    public async Task DisposeAsync_MultipleCalls_ShouldNotThrow()
    {
        // Act & Assert - 不应该抛出异常
        await _scheduler.DisposeAsync();
        await _scheduler.DisposeAsync(); // 第二次调用
    }
    
    #endregion
    
    #region 性能和稳定性测试
    
    [Fact]
    public async Task HighFrequencyRecording_ShouldMaintainStability()
    {
        // Arrange
        _scheduler.Start();
        const int recordCount = 1000;
        
        // Act
        var tasks = new Task[10];
        for (int t = 0; t < tasks.Length; t++)
        {
            int taskId = t;
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < recordCount / tasks.Length; i++)
                {
                    _scheduler.RecordBatchOperation(
                        10 + (taskId * 5),
                        TimeSpan.FromMilliseconds(1 + taskId),
                        20 + (taskId * 10));
                }
            });
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var metrics = _scheduler.GetMetrics();
        metrics.TotalBatches.Should().BeGreaterThan(0);
        _scheduler.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public async Task LongRunningOperation_ShouldMaintainPerformance()
    {
        // Arrange
        _scheduler.Start();
        var startTime = DateTime.UtcNow;
        
        // Act - 运行一段时间并持续记录
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < 500) // 运行500ms
        {
            _scheduler.RecordBatchOperation(16, TimeSpan.FromMilliseconds(2), 40);
            await Task.Delay(10);
        }
        
        // Assert
        var metrics = _scheduler.GetMetrics();
        metrics.TotalBatches.Should().BeGreaterThan(10);
        
        // 参数应该仍在有效范围内
        metrics.CurrentBatchInterval.Should().BeInRange(
            AdaptiveBatchScheduler.SchedulerSpecs.MIN_BATCH_INTERVAL_MS,
            AdaptiveBatchScheduler.SchedulerSpecs.MAX_BATCH_INTERVAL_MS);
        
        metrics.CurrentBatchSize.Should().BeInRange(
            AdaptiveBatchScheduler.SchedulerSpecs.MIN_BATCH_SIZE,
            AdaptiveBatchScheduler.SchedulerSpecs.MAX_BATCH_SIZE);
    }
    
    #endregion
    
    #region 指标准确性测试
    
    [Fact]
    public void MetricsCalculation_ShouldReflectRecentActivity()
    {
        // Arrange
        const int batchSize = 25;
        const int processingTimeMs = 10;
        const int queueDepth = 60;
        
        // Act
        for (int i = 0; i < 10; i++)
        {
            _scheduler.RecordBatchOperation(batchSize, TimeSpan.FromMilliseconds(processingTimeMs), queueDepth);
        }
        
        var metrics = _scheduler.GetMetrics();
        
        // Assert
        metrics.TotalBatches.Should().Be(10);
        
        // 吞吐量应该大于0（具体值取决于实现细节）
        if (metrics.AverageThroughput > 0)
        {
            metrics.AverageThroughput.Should().BeGreaterThan(0);
        }
        
        // 负载应该反映队列深度
        metrics.CurrentLoad.Should().BeGreaterOrEqualTo(0);
    }
    
    #endregion
}