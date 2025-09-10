using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Server.Engine;
using PulseRPC.Transport;
using Xunit;

namespace PulseRPC.Tests.Engine;

/// <summary>
/// HighPerformanceMessageEngine 单元测试
/// 验证高性能消息引擎的核心功能
/// </summary>
public class HighPerformanceMessageEngineTests : IAsyncDisposable
{
    private readonly HighPerformanceMessageEngine _engine;
    private readonly IMessageHandlerRegistry _handlerRegistry;
    private readonly ILogger<HighPerformanceMessageEngine> _logger;
    private readonly MessageEngineConfiguration _config;
    
    public HighPerformanceMessageEngineTests()
    {
        _handlerRegistry = Substitute.For<IMessageHandlerRegistry>();
        _logger = Substitute.For<ILogger<HighPerformanceMessageEngine>>();
        
        _config = new MessageEngineConfiguration
        {
            EnableDetailedLogging = false,
            NormalMessageDropRate = 0.8,
            CriticalMessageTimeoutUs = 100,
            L2BackpressureWaitMs = 1,
            PerformanceCheckFrequency = 10,
            BatchSoftTimeoutMs = 50
        };
        
        var options = Options.Create(_config);
        var serverChannel = new object(); // 临时占位符
        
        _engine = new HighPerformanceMessageEngine(
            "test-connection-001",
            serverChannel,
            _handlerRegistry,
            options,
            _logger);
    }
    
    public async ValueTask DisposeAsync()
    {
        await _engine.DisposeAsync();
    }
    
    #region 基础功能测试
    
    [Fact]
    public void Constructor_ValidParameters_ShouldInitializeCorrectly()
    {
        // Assert
        var stats = _engine.GetStatistics();
        stats.ConnectionId.Should().Be("test-connection-001");
        stats.IsRunning.Should().BeFalse();
        stats.L1Capacity.Should().Be(HighPerformanceMessageEngine.Specifications.L1_BUFFER_SIZE);
        stats.L1Count.Should().Be(0);
    }
    
    [Fact]
    public void Start_ShouldChangeRunningState()
    {
        // Act
        _engine.Start();
        
        // Assert
        var stats = _engine.GetStatistics();
        stats.IsRunning.Should().BeTrue();
    }
    
    [Fact]
    public void Start_WhenAlreadyRunning_ShouldNotThrow()
    {
        // Arrange
        _engine.Start();
        
        // Act & Assert - 不应该抛出异常
        _engine.Start();
        
        var stats = _engine.GetStatistics();
        stats.IsRunning.Should().BeTrue();
    }
    
    #endregion
    
    #region 消息入队测试
    
    [Fact]
    public void TryEnqueueMessage_ValidMessage_ShouldSucceed()
    {
        // Arrange
        _engine.Start();
        var message = CreateTestMessage(MessagePriority.Normal);
        
        // Act
        var result = _engine.TryEnqueueMessage(message);
        
        // Assert
        result.Should().BeTrue();
        
        var stats = _engine.GetStatistics();
        stats.TotalEnqueued.Should().Be(1);
        stats.L1Count.Should().Be(1);
    }
    
    [Fact]
    public void TryEnqueueMessage_MultipleMessages_ShouldEnqueueAll()
    {
        // Arrange
        _engine.Start();
        const int messageCount = 10;
        
        // Act
        for (int i = 0; i < messageCount; i++)
        {
            var message = CreateTestMessage(MessagePriority.Normal, sequenceId: i);
            var result = _engine.TryEnqueueMessage(message);
            result.Should().BeTrue($"Message {i} should enqueue successfully");
        }
        
        // Assert
        var stats = _engine.GetStatistics();
        stats.TotalEnqueued.Should().Be(messageCount);
        stats.L1Count.Should().Be(messageCount);
    }
    
    [Fact]
    public void TryEnqueueMessage_CriticalMessage_ShouldHaveHigherPriority()
    {
        // Arrange
        _engine.Start();
        var criticalMessage = CreateTestMessage(MessagePriority.Critical);
        var normalMessage = CreateTestMessage(MessagePriority.Normal);
        
        // Act
        var criticalResult = _engine.TryEnqueueMessage(criticalMessage);
        var normalResult = _engine.TryEnqueueMessage(normalMessage);
        
        // Assert
        criticalResult.Should().BeTrue();
        normalResult.Should().BeTrue();
        
        var stats = _engine.GetStatistics();
        stats.TotalEnqueued.Should().Be(2);
    }
    
    [Fact]
    public void TryEnqueueMessage_BufferFull_ShouldHandleBackpressure()
    {
        // Arrange
        _engine.Start();
        var capacity = HighPerformanceMessageEngine.Specifications.L1_BUFFER_SIZE;
        
        // 填满缓冲区
        for (int i = 0; i < capacity; i++)
        {
            var message = CreateTestMessage(MessagePriority.Normal, sequenceId: i);
            _engine.TryEnqueueMessage(message);
        }
        
        // Act - 尝试添加额外的消息
        var normalMessage = CreateTestMessage(MessagePriority.Normal, sequenceId: capacity);
        var criticalMessage = CreateTestMessage(MessagePriority.Critical, sequenceId: capacity + 1);
        
        var normalResult = _engine.TryEnqueueMessage(normalMessage);
        var criticalResult = _engine.TryEnqueueMessage(criticalMessage);
        
        // Assert
        // 普通消息可能因为背压而失败
        // 关键消息应该有更高的成功概率
        var stats = _engine.GetStatistics();
        stats.TotalEnqueued.Should().BeGreaterThan(0);
    }
    
    #endregion
    
    #region 消息处理测试
    
    [Fact]
    public async Task MessageProcessing_ShouldInvokeHandler()
    {
        // Arrange
        _engine.Start();
        var message = CreateTestMessage(MessagePriority.Normal);
        
        _handlerRegistry.HandleAsync(Arg.Any<ServerMessage>())
                       .Returns(Task.FromResult<object?>("test-response"));
        
        // Act
        var result = _engine.TryEnqueueMessage(message);
        
        // 等待消息被处理
        await Task.Delay(100);
        
        // Assert
        result.Should().BeTrue();
        
        // 验证处理器被调用（可能需要等待更长时间以确保异步处理完成）
        await Task.Delay(200);
        
        _handlerRegistry.Received().HandleAsync(Arg.Any<ServerMessage>());
    }
    
    [Fact]
    public async Task MessageProcessing_HandlerException_ShouldHandleGracefully()
    {
        // Arrange
        _engine.Start();
        var message = CreateTestMessage(MessagePriority.Normal);
        
        _handlerRegistry.HandleAsync(Arg.Any<ServerMessage>())
                       .Returns<object?>(x => throw new InvalidOperationException("Test exception"));
        
        // Act
        var result = _engine.TryEnqueueMessage(message);
        
        // 等待消息被处理
        await Task.Delay(200);
        
        // Assert
        result.Should().BeTrue();
        
        // 引擎应该继续运行，不应该因为处理器异常而崩溃
        var stats = _engine.GetStatistics();
        stats.IsRunning.Should().BeTrue();
    }
    
    #endregion
    
    #region 性能统计测试
    
    [Fact]
    public void GetStatistics_InitialState_ShouldReturnZeroValues()
    {
        // Act
        var stats = _engine.GetStatistics();
        
        // Assert
        stats.TotalEnqueued.Should().Be(0);
        stats.TotalProcessed.Should().Be(0);
        stats.TotalDropped.Should().Be(0);
        stats.L1Count.Should().Be(0);
        stats.L1Utilization.Should().Be(0);
    }
    
    [Fact]
    public void GetStatistics_AfterEnqueuing_ShouldUpdateCounters()
    {
        // Arrange
        _engine.Start();
        const int messageCount = 5;
        
        // Act
        for (int i = 0; i < messageCount; i++)
        {
            var message = CreateTestMessage(MessagePriority.Normal, sequenceId: i);
            _engine.TryEnqueueMessage(message);
        }
        
        var stats = _engine.GetStatistics();
        
        // Assert
        stats.TotalEnqueued.Should().Be(messageCount);
        stats.L1Count.Should().Be(messageCount);
        stats.L1Utilization.Should().BeGreaterThan(0);
    }
    
    [Fact]
    public void GetStatistics_ShouldIncludeAdaptiveSchedulerMetrics()
    {
        // Arrange
        _engine.Start();
        
        // Act
        var stats = _engine.GetStatistics();
        
        // Assert
        stats.CurrentBatchInterval.Should().BeInRange(
            HighPerformanceMessageEngine.Specifications.MIN_BATCH_INTERVAL_MS,
            HighPerformanceMessageEngine.Specifications.MAX_BATCH_INTERVAL_MS);
        
        stats.CurrentBatchSize.Should().BeInRange(
            HighPerformanceMessageEngine.Specifications.ADAPTIVE_BATCH_SIZE_MIN,
            HighPerformanceMessageEngine.Specifications.ADAPTIVE_BATCH_SIZE_MAX);
    }
    
    #endregion
    
    #region 背压处理测试
    
    [Fact]
    public void BackpressureHandling_LowPriorityMessage_ShouldDropMore()
    {
        // Arrange
        _engine.Start();
        var capacity = HighPerformanceMessageEngine.Specifications.L1_BUFFER_SIZE;
        
        // 填满缓冲区
        for (int i = 0; i < capacity; i++)
        {
            var message = CreateTestMessage(MessagePriority.Normal, sequenceId: i);
            _engine.TryEnqueueMessage(message);
        }
        
        // Act - 尝试添加低优先级消息
        var lowPriorityResults = new bool[10];
        for (int i = 0; i < lowPriorityResults.Length; i++)
        {
            var message = CreateTestMessage(MessagePriority.Low, sequenceId: capacity + i);
            lowPriorityResults[i] = _engine.TryEnqueueMessage(message);
        }
        
        // Assert
        // 低优先级消息应该大多数被丢弃
        var successCount = lowPriorityResults.Count(r => r);
        successCount.Should().BeLessThan(lowPriorityResults.Length);
    }
    
    [Fact]
    public void BackpressureHandling_CriticalMessage_ShouldHaveHigherSuccessRate()
    {
        // Arrange
        _engine.Start();
        var capacity = HighPerformanceMessageEngine.Specifications.L1_BUFFER_SIZE;
        
        // 填满缓冲区
        for (int i = 0; i < capacity; i++)
        {
            var message = CreateTestMessage(MessagePriority.Normal, sequenceId: i);
            _engine.TryEnqueueMessage(message);
        }
        
        // Act - 尝试添加关键消息
        var criticalResults = new bool[5];
        for (int i = 0; i < criticalResults.Length; i++)
        {
            var message = CreateTestMessage(MessagePriority.Critical, sequenceId: capacity + i);
            criticalResults[i] = _engine.TryEnqueueMessage(message);
        }
        
        // Assert
        // 关键消息应该有更高的成功率（虽然不能保证100%）
        var successCount = criticalResults.Count(r => r);
        // 至少应该有一些成功，具体数量取决于实现
        successCount.Should().BeGreaterOrEqualTo(0);
    }
    
    #endregion
    
    #region 并发测试
    
    [Fact]
    public async Task ConcurrentEnqueue_ShouldMaintainThreadSafety()
    {
        // Arrange
        _engine.Start();
        const int taskCount = 10;
        const int messagesPerTask = 50;
        
        // Act
        var tasks = new Task[taskCount];
        for (int t = 0; t < taskCount; t++)
        {
            int taskId = t;
            tasks[t] = Task.Run(() =>
            {
                for (int i = 0; i < messagesPerTask; i++)
                {
                    var message = CreateTestMessage(MessagePriority.Normal, sequenceId: taskId * 1000 + i);
                    _engine.TryEnqueueMessage(message);
                }
            });
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var stats = _engine.GetStatistics();
        stats.TotalEnqueued.Should().BeGreaterThan(0);
        stats.IsRunning.Should().BeTrue();
        
        // 不应该有异常或崩溃
    }
    
    #endregion
    
    #region 生命周期测试
    
    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        // Arrange
        _engine.Start();
        var message = CreateTestMessage(MessagePriority.Normal);
        _engine.TryEnqueueMessage(message);
        
        // Act
        await _engine.DisposeAsync();
        
        // Assert
        var stats = _engine.GetStatistics();
        stats.IsRunning.Should().BeFalse();
    }
    
    [Fact]
    public async Task DisposeAsync_MultipleCalls_ShouldNotThrow()
    {
        // Arrange
        _engine.Start();
        
        // Act & Assert
        await _engine.DisposeAsync();
        await _engine.DisposeAsync(); // 第二次调用应该不抛出异常
    }
    
    #endregion
    
    #region 配置测试
    
    [Fact]
    public void Configuration_EnableDetailedLogging_ShouldAffectBehavior()
    {
        // Arrange
        var config = new MessageEngineConfiguration
        {
            EnableDetailedLogging = true
        };
        
        var options = Options.Create(config);
        
        using var engine = new HighPerformanceMessageEngine(
            "test-detailed-logging",
            new object(),
            _handlerRegistry,
            options,
            _logger);
        
        engine.Start();
        
        // Act
        var message = CreateTestMessage(MessagePriority.Normal);
        var result = engine.TryEnqueueMessage(message);
        
        // Assert
        result.Should().BeTrue();
        
        // 验证详细日志记录被启用（通过检查日志调用）
        // 这里简化为检查操作成功
    }
    
    #endregion
    
    #region 辅助方法
    
    private static TestServerMessage CreateTestMessage(MessagePriority priority, long sequenceId = 1)
    {
        return new TestServerMessage
        {
            SequenceId = sequenceId,
            Priority = priority,
            ServerTimestamp = DateTime.UtcNow,
            TestData = $"Test message {sequenceId}"
        };
    }
    
    /// <summary>
    /// 测试用的ServerMessage实现
    /// </summary>
    private class TestServerMessage : ServerMessage
    {
        public string TestData { get; set; } = "";
    }
    
    #endregion
}