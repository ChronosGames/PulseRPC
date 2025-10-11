using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Core;
using PulseRPC.Server.Extensions.DependencyInjection;
using PulseRPC.Server.Observability;
using PulseRPC.Server.Pipeline;
using System;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for PulseServerOptions and DI extensions (T067).
/// Tests configuration validation, defaults, and dependency injection registration.
/// </summary>
public class ServerOptionsTests
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

    #region DI Extension Tests

    [Fact]
    public void AddPulseRpcServer_ShouldRegisterAllComponents()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer();
        var provider = services.BuildServiceProvider();

        // Assert: Core pipeline components
        provider.GetService<IMessageReceiver>().Should().NotBeNull();
        provider.GetService<IMessageDispatcher>().Should().NotBeNull();
        provider.GetService<IServiceInvoker>().Should().NotBeNull();
        provider.GetService<ICompiledServiceInvoker>().Should().NotBeNull();
        provider.GetService<IResponseBuilder>().Should().NotBeNull();
        provider.GetService<IErrorResponseFactory>().Should().NotBeNull();
        provider.GetService<IResponseTransmitter>().Should().NotBeNull();

        // Assert: Core infrastructure
        provider.GetService<IConnectionManager>().Should().NotBeNull();
        provider.GetService<IServiceRegistry>().Should().NotBeNull();
        provider.GetService<IBackpressurePolicy>().Should().NotBeNull();

        // Assert: Observability
        provider.GetService<IPipelineMetricsCollector>().Should().NotBeNull();

        // Assert: ServerHost
        provider.GetService<IServerHost>().Should().NotBeNull();
    }

    [Fact]
    public void AddPulseRpcServer_ShouldRegisterOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer(opt =>
        {
            opt.Port = 7000;
            opt.ServerName = "TestServer";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<PulseServerOptions>();
        options.Should().NotBeNull();
        options!.Port.Should().Be(7000);
        options.ServerName.Should().Be("TestServer");
    }

    [Fact]
    public void AddPulseRpcServer_ShouldValidateOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ValidationException>(() =>
            services.AddPulseRpcServer(opt => opt.Port = 0));
    }

    [Fact]
    public void AddPulseRpcServer_ShouldRegisterAsSingletons()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer();
        var provider = services.BuildServiceProvider();

        // Assert: Same instances on multiple resolves
        var host1 = provider.GetService<IServerHost>();
        var host2 = provider.GetService<IServerHost>();
        host1.Should().BeSameAs(host2);

        var dispatcher1 = provider.GetService<IMessageDispatcher>();
        var dispatcher2 = provider.GetService<IMessageDispatcher>();
        dispatcher1.Should().BeSameAs(dispatcher2);
    }

    [Fact]
    public void AddPulseRpcServer_ShouldThrow_WhenServicesIsNull()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddPulseRpcServer());
    }

    [Fact]
    public void AddPulseService_ShouldRegisterServiceDescriptor()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseService<TestService>("TestService");
        var provider = services.BuildServiceProvider();

        // Assert
        var descriptor = provider.GetService<ServiceDescriptor<TestService>>();
        descriptor.Should().NotBeNull();
        descriptor!.ServiceName.Should().Be("TestService");
    }

    [Fact]
    public void AddPulseService_ShouldRegisterServiceAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseService<TestService>("TestService");
        var provider = services.BuildServiceProvider();

        // Assert: Different instances in different scopes
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var service1 = scope1.ServiceProvider.GetService<TestService>();
        var service2 = scope2.ServiceProvider.GetService<TestService>();

        service1.Should().NotBeNull();
        service2.Should().NotBeNull();
        service1.Should().NotBeSameAs(service2);
    }

    [Fact]
    public void AddPulseService_WithOptions_ShouldApplyOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new ServiceOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(10),
            Priority = MessagePriority.High
        };

        // Act
        services.AddPulseService<TestService>("TestService", options);
        var provider = services.BuildServiceProvider();

        // Assert
        var descriptor = provider.GetService<ServiceDescriptor<TestService>>();
        descriptor!.Options.Should().NotBeNull();
        descriptor.Options!.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(10));
        descriptor.Options.Priority.Should().Be(MessagePriority.High);
    }

    [Fact]
    public void AddPulseService_ShouldThrow_WhenServiceNameIsEmpty()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            services.AddPulseService<TestService>(""));
    }

    [Fact]
    public void AddPulseService_ShouldThrow_WhenServicesIsNull()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            services.AddPulseService<TestService>("TestService"));
    }

    [Fact]
    public void AddPulseSingletonService_ShouldRegisterServiceAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseSingletonService<TestService>("TestService");
        var provider = services.BuildServiceProvider();

        // Assert: Same instance across scopes
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var service1 = scope1.ServiceProvider.GetService<TestService>();
        var service2 = scope2.ServiceProvider.GetService<TestService>();

        service1.Should().NotBeNull();
        service2.Should().NotBeNull();
        service1.Should().BeSameAs(service2);
    }

    [Fact]
    public void AddPulseTransientService_ShouldRegisterServiceAsTransient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseTransientService<TestService>("TestService");
        var provider = services.BuildServiceProvider();

        // Assert: Different instances within same scope
        using var scope = provider.CreateScope();

        var service1 = scope.ServiceProvider.GetService<TestService>();
        var service2 = scope.ServiceProvider.GetService<TestService>();

        service1.Should().NotBeNull();
        service2.Should().NotBeNull();
        service1.Should().NotBeSameAs(service2);
    }

    [Fact]
    public void AddPulseRpcServer_WithTracingEnabled_ShouldRegisterTracing()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer(opt => opt.EnableDistributedTracing = true);
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<IDistributedTracingIntegration>().Should().NotBeNull();
    }

    [Fact]
    public void AddPulseRpcServer_WithTracingDisabled_ShouldNotRegisterTracing()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer(opt => opt.EnableDistributedTracing = false);
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<IDistributedTracingIntegration>().Should().BeNull();
    }

    [Fact]
    public void AddPulseRpcServer_WithDiagnosticsEnabled_ShouldRegisterDiagnostics()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer(opt => opt.EnableDiagnosticEndpoints = true);
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<IDiagnosticEndpoints>().Should().NotBeNull();
    }

    [Fact]
    public void AddPulseRpcServer_WithDiagnosticsDisabled_ShouldNotRegisterDiagnostics()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer(opt => opt.EnableDiagnosticEndpoints = false);
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<IDiagnosticEndpoints>().Should().BeNull();
    }

    [Fact]
    public void AddPulseRpcServer_ShouldAllowMultipleServiceRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPulseRpcServer();
        services.AddPulseService<TestService>("Service1");
        services.AddPulseService<AnotherTestService>("Service2");
        services.AddPulseSingletonService<ThirdTestService>("Service3");

        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ServiceDescriptor<TestService>>().Should().NotBeNull();
        provider.GetService<ServiceDescriptor<AnotherTestService>>().Should().NotBeNull();
        provider.GetService<ServiceDescriptor<ThirdTestService>>().Should().NotBeNull();
    }

    #endregion

    #region Test Services

    public class TestService
    {
        private int _value;

        public int GetValue() => _value;
        public void SetValue(int value) => _value = value;
    }

    public class AnotherTestService
    {
        public string Echo(string message) => message;
    }

    public class ThirdTestService
    {
        public double Calculate(double x, double y) => x + y;
    }

    #endregion
}
