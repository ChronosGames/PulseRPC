using FluentAssertions;
using Xunit;
using PulseRPC.Server;
using PulseRPC.Server.Configuration;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// ServiceQueueOptions 单元测试
/// </summary>
public class ServiceQueueOptionsTests
{
    [Fact(DisplayName = "默认配置应该是单线程 Actor 模型")]
    public void Default_ShouldBeActorModel()
    {
        // Arrange & Act
        var options = ServiceQueueOptions.Default;

        // Assert
        options.MaxConcurrency.Should().Be(1, "Actor 模型是单线程");
        options.QueueCapacity.Should().Be(10000);
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.Block);
    }

    [Fact(DisplayName = "ForActor 配置应该是单线程模型")]
    public void ForActor_ShouldBeSequential()
    {
        // Arrange & Act
        var options = ServiceQueueOptions.ForActor;

        // Assert
        options.MaxConcurrency.Should().Be(1);
        options.QueueCapacity.Should().Be(10000);
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.Block);
    }

    [Fact(DisplayName = "ForConcurrentIO 配置应该是高并发")]
    public void ForConcurrentIO_ShouldBeHighConcurrency()
    {
        // Arrange & Act
        var options = ServiceQueueOptions.ForConcurrentIO;

        // Assert
        options.MaxConcurrency.Should().Be(16, "IO 密集型推荐 16 并发");
        options.QueueCapacity.Should().Be(10000);
    }

    [Fact(DisplayName = "ForConcurrentCPU 配置应该使用 CPU 核心数")]
    public void ForConcurrentCPU_ShouldUseCpuCount()
    {
        // Arrange & Act
        var options = ServiceQueueOptions.ForConcurrentCPU;

        // Assert
        options.MaxConcurrency.Should().Be(Environment.ProcessorCount);
        options.QueueCapacity.Should().Be(5000);
    }

    [Fact(DisplayName = "ForBackpressureDropOldest 配置应该使用 DropOldest 策略")]
    public void ForBackpressureDropOldest_ShouldUseDropOldestStrategy()
    {
        // Arrange & Act
        var options = ServiceQueueOptions.ForBackpressureDropOldest;

        // Assert
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.DropOldest);
        options.QueueCapacity.Should().Be(5000);
        options.MaxConcurrency.Should().Be(1);
    }

    [Fact(DisplayName = "ForBackpressureReject 配置应该使用 Reject 策略")]
    public void ForBackpressureReject_ShouldUseRejectStrategy()
    {
        // Arrange & Act
        var options = ServiceQueueOptions.ForBackpressureReject;

        // Assert
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.Reject);
        options.QueueCapacity.Should().Be(100, "关键业务小队列");
        options.MaxConcurrency.Should().Be(1);
    }

    [Fact(DisplayName = "验证配置应该拒绝无效的并发度")]
    public void Validate_ShouldRejectInvalidConcurrency()
    {
        // Arrange
        var options = new ServiceQueueOptions { MaxConcurrency = 0 };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxConcurrency must be at least 1*");
    }

    [Fact(DisplayName = "验证配置应该拒绝过高的并发度")]
    public void Validate_ShouldRejectTooHighConcurrency()
    {
        // Arrange
        var options = new ServiceQueueOptions { MaxConcurrency = 1001 };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxConcurrency should not exceed 1000*");
    }

    [Fact(DisplayName = "验证配置应该拒绝无效的队列容量")]
    public void Validate_ShouldRejectInvalidQueueCapacity()
    {
        // Arrange
        var options = new ServiceQueueOptions { QueueCapacity = 0 };

        // Act & Assert
        var act = () => options.Validate();
        act.Should().Throw<ArgumentException>()
            .WithMessage("*QueueCapacity must be at least 1*");
    }

    [Fact(DisplayName = "克隆配置应该创建独立副本")]
    public void Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var original = new ServiceQueueOptions
        {
            MaxConcurrency = 8,
            QueueCapacity = 5000,
            BackpressureStrategy = BackpressureStrategy.DropOldest
        };

        // Act
        var cloned = original.Clone();
        cloned.MaxConcurrency = 16; // 修改克隆

        // Assert
        cloned.MaxConcurrency.Should().Be(16);
        original.MaxConcurrency.Should().Be(8, "原始配置不应受影响");
    }
}

/// <summary>
/// ServiceQueueOptionsBuilder 单元测试
/// </summary>
public class ServiceQueueOptionsBuilderTests
{
    [Fact(DisplayName = "构建器应该支持 Fluent API")]
    public void Builder_ShouldSupportFluentApi()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .WithQueueCapacity(5000)
            .WithMaxConcurrency(8)
            .WithBackpressureStrategy(BackpressureStrategy.DropOldest)
            .Build();

        // Assert
        options.QueueCapacity.Should().Be(5000);
        options.MaxConcurrency.Should().Be(8);
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.DropOldest);
    }

    [Fact(DisplayName = "AsActor 应该设置单线程模型")]
    public void AsActor_ShouldSetSequentialModel()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .AsActor()
            .Build();

        // Assert
        options.MaxConcurrency.Should().Be(1);
    }

    [Fact(DisplayName = "AsConcurrent 应该设置并发模型")]
    public void AsConcurrent_ShouldSetConcurrentModel()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .AsConcurrent(maxConcurrency: 16)
            .Build();

        // Assert
        options.MaxConcurrency.Should().Be(16);
    }

    [Fact(DisplayName = "WithBackpressureDropOldest 应该设置 DropOldest 策略")]
    public void WithBackpressureDropOldest_ShouldSetDropOldestStrategy()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .WithBackpressureDropOldest()
            .Build();

        // Assert
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.DropOldest);
    }

    [Fact(DisplayName = "WithBackpressureDropNewest 应该设置 DropNewest 策略")]
    public void WithBackpressureDropNewest_ShouldSetDropNewestStrategy()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .WithBackpressureDropNewest()
            .Build();

        // Assert
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.DropNewest);
    }

    [Fact(DisplayName = "WithBackpressureReject 应该设置 Reject 策略")]
    public void WithBackpressureReject_ShouldSetRejectStrategy()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .WithBackpressureReject()
            .Build();

        // Assert
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.Reject);
    }

    [Fact(DisplayName = "Build 应该验证配置有效性")]
    public void Build_ShouldValidateOptions()
    {
        // Arrange
        var builder = new ServiceQueueOptionsBuilder()
            .WithMaxConcurrency(0); // 无效配置

        // Act & Assert
        var act = () => builder.Build();
        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "From 应该从现有配置开始构建")]
    public void From_ShouldStartFromExistingOptions()
    {
        // Arrange
        var original = new ServiceQueueOptions
        {
            MaxConcurrency = 8,
            QueueCapacity = 5000,
            BackpressureStrategy = BackpressureStrategy.DropOldest
        };

        // Act
        var options = ServiceQueueOptionsBuilder.From(original)
            .WithMaxConcurrency(16) // 修改并发度
            .Build();

        // Assert
        options.MaxConcurrency.Should().Be(16);
        options.QueueCapacity.Should().Be(5000, "保留原始值");
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.DropOldest, "保留原始值");
    }

    [Fact(DisplayName = "链式调用应该支持混合配置")]
    public void ChainedCalls_ShouldSupportMixedConfiguration()
    {
        // Arrange & Act
        var options = new ServiceQueueOptionsBuilder()
            .AsConcurrent(8)                  // 并发模型
            .WithBackpressureDropOldest()     // 背压策略
            .WithQueueCapacity(5000)          // 队列容量
            .Build();

        // Assert
        options.MaxConcurrency.Should().Be(8);
        options.BackpressureStrategy.Should().Be(BackpressureStrategy.DropOldest);
        options.QueueCapacity.Should().Be(5000);
    }
}
