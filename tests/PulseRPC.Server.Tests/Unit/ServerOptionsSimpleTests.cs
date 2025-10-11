using FluentAssertions;
using PulseRPC.Server.Configuration;
using System;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Simple unit tests for PulseServerOptions configuration validation (T067).
/// Tests only configuration validation without full DI integration.
/// </summary>
public class ServerOptionsSimpleTests
{
    #region PulseServerOptions Tests

    [Fact]
    public void PulseServerOptions_ShouldInitialize_WithDefaults()
    {
        // Arrange & Act
        var options = new PulseServerOptions();

        // Assert
        options.ServerName.Should().Be("PulseRPC-Server");
        options.Version.Should().Be("1.0.0");
        options.BindAddress.Should().Be("0.0.0.0");
        options.Port.Should().Be(5000);
        options.MaxConnections.Should().Be(10_000);
        options.WorkerThreadCount.Should().Be(Environment.ProcessorCount);
        options.MaxQueueDepthPerPriority.Should().Be(10_000);
        options.DefaultInvocationTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.ConnectionIdleTimeout.Should().Be(TimeSpan.FromMinutes(5));
        options.MaxMessageSize.Should().Be(10 * 1024 * 1024);
        options.ReceiveBufferSize.Should().Be(8192);
        options.SendBufferSize.Should().Be(8192);
        options.NoDelay.Should().BeTrue();
        options.BacklogSize.Should().Be(100);
        options.EnableDistributedTracing.Should().BeFalse();
        options.EnableDiagnosticEndpoints.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldSucceed_WithDefaultOptions()
    {
        // Arrange
        var options = new PulseServerOptions();

        // Act & Assert (should not throw)
        options.Validate();
    }

    [Fact]
    public void Validate_ShouldThrow_WhenServerNameIsEmpty()
    {
        // Arrange
        var options = new PulseServerOptions { ServerName = "" };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenPortIsInvalid()
    {
        // Arrange
        var options = new PulseServerOptions { Port = 0 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());

        // Arrange
        options.Port = 70000;

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMaxConnectionsIsInvalid()
    {
        // Arrange
        var options = new PulseServerOptions { MaxConnections = 0 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenWorkerThreadCountIsInvalid()
    {
        // Arrange
        var options = new PulseServerOptions { WorkerThreadCount = 0 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMaxQueueDepthPerPriorityIsTooSmall()
    {
        // Arrange
        var options = new PulseServerOptions { MaxQueueDepthPerPriority = 50 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDefaultInvocationTimeoutIsTooShort()
    {
        // Arrange
        var options = new PulseServerOptions { DefaultInvocationTimeout = TimeSpan.FromMilliseconds(500) };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMaxMessageSizeIsTooSmall()
    {
        // Arrange
        var options = new PulseServerOptions { MaxMessageSize = 512 };

        // Act & Assert
        Assert.Throws<ValidationException>(() => options.Validate());
    }

    [Fact]
    public void ToServerHostOptions_ShouldApplyTopLevelSettings()
    {
        // Arrange
        var options = new PulseServerOptions
        {
            WorkerThreadCount = 8,
            MaxQueueDepthPerPriority = 5000,
            DefaultInvocationTimeout = TimeSpan.FromSeconds(60),
            MaxMessageSize = 5 * 1024 * 1024,
            MaxConnections = 1000,
            ConnectionIdleTimeout = TimeSpan.FromMinutes(10)
        };

        // Act
        var hostOptions = options.ToServerHostOptions();

        // Assert
        hostOptions.MessageDispatcherOptions.WorkerThreadCount.Should().Be(8);
        hostOptions.MessageDispatcherOptions.MaxQueueDepthPerPriority.Should().Be(5000);
        hostOptions.MessageDispatcherOptions.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(60));
        hostOptions.MessageReceiverOptions.MaxBufferSize.Should().Be(5 * 1024 * 1024);
        hostOptions.ConnectionManagerOptions.MaxConnections.Should().Be(1000);
        hostOptions.ConnectionManagerOptions.InactivityTimeout.Should().Be(TimeSpan.FromMinutes(10));
        hostOptions.ServiceRegistryOptions.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ToServerHostOptions_ShouldReturnComponentOptions()
    {
        // Arrange
        var options = new PulseServerOptions();

        // Act
        var hostOptions = options.ToServerHostOptions();

        // Assert
        hostOptions.MessageReceiverOptions.Should().NotBeNull();
        hostOptions.MessageDispatcherOptions.Should().NotBeNull();
        hostOptions.ResponseTransmitterOptions.Should().NotBeNull();
        hostOptions.ConnectionManagerOptions.Should().NotBeNull();
        hostOptions.ServiceRegistryOptions.Should().NotBeNull();
        hostOptions.BackpressurePolicyOptions.Should().NotBeNull();
    }

    [Fact]
    public void PulseServerOptions_ShouldAllowCustomization()
    {
        // Arrange
        var options = new PulseServerOptions
        {
            ServerName = "CustomServer",
            Version = "2.0.0",
            BindAddress = "127.0.0.1",
            Port = 6000,
            MaxConnections = 5000,
            WorkerThreadCount = 16,
            EnableDistributedTracing = true,
            EnableDiagnosticEndpoints = false
        };

        // Act
        options.Validate();

        // Assert
        options.ServerName.Should().Be("CustomServer");
        options.Version.Should().Be("2.0.0");
        options.BindAddress.Should().Be("127.0.0.1");
        options.Port.Should().Be(6000);
        options.MaxConnections.Should().Be(5000);
        options.WorkerThreadCount.Should().Be(16);
        options.EnableDistributedTracing.Should().BeTrue();
        options.EnableDiagnosticEndpoints.Should().BeFalse();
    }

    #endregion
}
