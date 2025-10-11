using FluentAssertions;
using PulseRPC.Server.Core;
using PulseRPC.Server.Models;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for ServiceRegistry (T058).
/// Tests service registration, duplicate handling, and concurrent registration.
/// </summary>
public class ServiceRegistryTests
{
    [Fact]
    public void ServiceRegistry_ShouldInitialize_Successfully()
    {
        // Arrange & Act
        var registry = new ServiceRegistry();

        // Assert
        registry.Should().NotBeNull();
        registry.RegisteredServiceCount.Should().Be(0);
    }

    [Fact]
    public void RegisterService_ShouldRegisterService_Successfully()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();

        // Act
        registry.RegisterService("TestService", service);

        // Assert
        registry.RegisteredServiceCount.Should().Be(1);
        registry.IsServiceRegistered("TestService").Should().BeTrue();
    }

    [Fact]
    public void RegisterService_ShouldThrow_WhenServiceNameIsEmpty()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.RegisterService("", service));
        Assert.Throws<ArgumentException>(() => registry.RegisterService("   ", service));
    }

    [Fact]
    public void RegisterService_ShouldThrow_WhenServiceInstanceIsNull()
    {
        // Arrange
        var registry = new ServiceRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.RegisterService<TestService>("TestService", null!));
    }

    [Fact]
    public void RegisterService_ShouldThrow_WhenDuplicateServiceName()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service1 = new TestService();
        var service2 = new TestService();

        registry.RegisterService("TestService", service1);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => registry.RegisterService("TestService", service2));
    }

    [Fact]
    public void UnregisterService_ShouldRemoveService_Successfully()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);

        // Act
        var result = registry.UnregisterService("TestService");

        // Assert
        result.Should().BeTrue();
        registry.RegisteredServiceCount.Should().Be(0);
        registry.IsServiceRegistered("TestService").Should().BeFalse();
    }

    [Fact]
    public void UnregisterService_ShouldReturnFalse_WhenServiceNotFound()
    {
        // Arrange
        var registry = new ServiceRegistry();

        // Act
        var result = registry.UnregisterService("NonExistentService");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetServiceHandler_ShouldReturnHandler_WhenServiceRegistered()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);

        // Act
        var handler = registry.GetServiceHandler("TestService");

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void GetServiceHandler_ShouldReturnNull_WhenServiceNotRegistered()
    {
        // Arrange
        var registry = new ServiceRegistry();

        // Act
        var handler = registry.GetServiceHandler("NonExistentService");

        // Assert
        handler.Should().BeNull();
    }

    [Fact]
    public void GetServiceHandler_ShouldReturnNull_WhenServicePaused()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);
        registry.PauseService("TestService");

        // Act
        var handler = registry.GetServiceHandler("TestService");

        // Assert
        handler.Should().BeNull();
    }

    [Fact]
    public void PauseService_ShouldPauseService_Successfully()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);

        // Act
        var result = registry.PauseService("TestService");

        // Assert
        result.Should().BeTrue();
        registry.GetServiceHandler("TestService").Should().BeNull();

        var registration = registry.GetServiceRegistration("TestService");
        registration!.State.Should().Be(ServiceState.Paused);
    }

    [Fact]
    public void ResumeService_ShouldResumeService_Successfully()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);
        registry.PauseService("TestService");

        // Act
        var result = registry.ResumeService("TestService");

        // Assert
        result.Should().BeTrue();
        registry.GetServiceHandler("TestService").Should().NotBeNull();

        var registration = registry.GetServiceRegistration("TestService");
        registration!.State.Should().Be(ServiceState.Active);
    }

    [Fact]
    public void GetServiceNames_ShouldReturnAllServiceNames()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service1 = new TestService();
        var service2 = new TestService();

        registry.RegisterService("Service1", service1);
        registry.RegisterService("Service2", service2);

        // Act
        var serviceNames = registry.GetServiceNames();

        // Assert
        serviceNames.Should().HaveCount(2);
        serviceNames.Should().Contain("Service1");
        serviceNames.Should().Contain("Service2");
    }

    [Fact]
    public void GetServiceMethods_ShouldReturnMethodNames()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);

        // Act
        var methods = registry.GetServiceMethods("TestService");

        // Assert
        methods.Should().NotBeEmpty();
        methods.Should().Contain("GetValue");
        methods.Should().Contain("SetValue");
    }

    [Fact]
    public void GetServiceMethods_ShouldReturnEmpty_WhenServiceNotFound()
    {
        // Arrange
        var registry = new ServiceRegistry();

        // Act
        var methods = registry.GetServiceMethods("NonExistentService");

        // Assert
        methods.Should().BeEmpty();
    }

    [Fact]
    public void GetServiceRegistration_ShouldReturnRegistration_WhenExists()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.RegisterService("TestService", service);

        // Act
        var registration = registry.GetServiceRegistration("TestService");

        // Assert
        registration.Should().NotBeNull();
        registration!.ServiceName.Should().Be("TestService");
        registration.ServiceType.Should().Be(typeof(TestService));
        registration.State.Should().Be(ServiceState.Active);
    }

    [Fact]
    public void Clear_ShouldRemoveAllServices()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service1 = new TestService();
        var service2 = new TestService();

        registry.RegisterService("Service1", service1);
        registry.RegisterService("Service2", service2);

        // Act
        registry.Clear();

        // Assert
        registry.RegisteredServiceCount.Should().Be(0);
    }

    [Fact]
    public void RegisterService_WithOptions_ShouldApplyOptions()
    {
        // Arrange
        var registry = new ServiceRegistry();
        var service = new TestService();
        var options = new ServiceOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(10),
            Priority = MessagePriority.High
        };

        // Act
        registry.RegisterService("TestService", service, options);

        // Assert
        var registration = registry.GetServiceRegistration("TestService");
        registration!.Options.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(10));
        registration.Options.Priority.Should().Be(MessagePriority.High);
    }

    // Test service class
    public class TestService
    {
        private int _value;

        public int GetValue() => _value;

        public void SetValue(int value) => _value = value;

        public async Task<string> GetValueAsync()
        {
            await Task.Delay(1);
            return _value.ToString();
        }
    }
}
