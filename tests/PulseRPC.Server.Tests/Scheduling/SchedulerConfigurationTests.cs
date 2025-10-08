using FluentAssertions;
using PulseRPC.Server.Scheduling;
using Xunit;

namespace PulseRPC.Server.Tests.Scheduling;

public class SchedulerConfigurationTests
{
    [Fact]
    public void DefaultConfiguration_HasCorrectDefaults()
    {
        // Arrange & Act
        var config = new SchedulerConfiguration();

        // Assert
        config.InitialThreadCount.Should().Be(Environment.ProcessorCount);
        config.MaxThreadCount.Should().Be(Environment.ProcessorCount * 2);
        config.ThreadIdleTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.ChannelCapacity.Should().Be(1024);
        config.EnablePriorityDroppingWhenFull.Should().BeTrue();
        config.EnableMetrics.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidConfiguration_DoesNotThrow()
    {
        // Arrange
        var config = new SchedulerConfiguration
        {
            InitialThreadCount = 4,
            MaxThreadCount = 8,
            ThreadIdleTimeout = TimeSpan.FromSeconds(60),
            ChannelCapacity = 512
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidInitialThreadCount_ThrowsArgumentException(int initialThreadCount)
    {
        // Arrange
        var config = new SchedulerConfiguration
        {
            InitialThreadCount = initialThreadCount
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(config.InitialThreadCount))
            .WithMessage("*must be greater than 0*");
    }

    [Fact]
    public void Validate_WithMaxThreadCountLessThanInitial_ThrowsArgumentException()
    {
        // Arrange
        var config = new SchedulerConfiguration
        {
            InitialThreadCount = 8,
            MaxThreadCount = 4
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(config.MaxThreadCount))
            .WithMessage("*must be >= InitialThreadCount*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidThreadIdleTimeout_ThrowsArgumentException(int seconds)
    {
        // Arrange
        var config = new SchedulerConfiguration
        {
            ThreadIdleTimeout = TimeSpan.FromSeconds(seconds)
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(config.ThreadIdleTimeout))
            .WithMessage("*must be positive*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidChannelCapacity_ThrowsArgumentException(int capacity)
    {
        // Arrange
        var config = new SchedulerConfiguration
        {
            ChannelCapacity = capacity
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName(nameof(config.ChannelCapacity))
            .WithMessage("*must be greater than 0*");
    }

    [Fact]
    public void Configuration_AllowsMaxThreadCountEqualToInitial()
    {
        // Arrange
        var config = new SchedulerConfiguration
        {
            InitialThreadCount = 4,
            MaxThreadCount = 4
        };

        // Act
        Action act = () => config.Validate();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Configuration_AllowsDisablingMetrics()
    {
        // Arrange & Act
        var config = new SchedulerConfiguration
        {
            EnableMetrics = false
        };

        // Assert
        config.EnableMetrics.Should().BeFalse();
        Action act = () => config.Validate();
        act.Should().NotThrow();
    }

    [Fact]
    public void Configuration_AllowsDisablingPriorityDropping()
    {
        // Arrange & Act
        var config = new SchedulerConfiguration
        {
            EnablePriorityDroppingWhenFull = false
        };

        // Assert
        config.EnablePriorityDroppingWhenFull.Should().BeFalse();
        Action act = () => config.Validate();
        act.Should().NotThrow();
    }
}