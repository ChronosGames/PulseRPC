using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PulseRPC.Server.Abstractions;
using PulseRPC.Server.ServiceManagement;
using Xunit;

namespace PulseRPC.Server.Tests.ServiceManagement;

public class PulseServiceFactoryTests : IDisposable
{
    private readonly PulseServiceFactory<TestService> _factory;
    private readonly PulseServiceFactoryOptions _options;

    public PulseServiceFactoryTests()
    {
        _options = new PulseServiceFactoryOptions
        {
            IdleTimeout = TimeSpan.FromSeconds(10),
            MaxCachedInstances = 100,
            EnableHealthCheck = false, // 禁用健康检查以简化测试
            EnableMetrics = true
        };

        _factory = new PulseServiceFactory<TestService>(
            serviceId => new TestService(serviceId),
            _options,
            NullLogger<PulseServiceFactory<TestService>>.Instance);
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCreateNewInstance_WhenNotExists()
    {
        // Act
        var service = await _factory.GetOrCreateAsync("TestService:test-1");

        // Assert
        service.Should().NotBeNull();
        service.ServiceId.Should().Be("TestService:test-1");
        _factory.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldReturnSameInstance_ForSameServiceId()
    {
        // Act
        var service1 = await _factory.GetOrCreateAsync("TestService:test-1");
        var service2 = await _factory.GetOrCreateAsync("TestService:test-1");

        // Assert
        service1.Should().BeSameAs(service2);
        _factory.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldCreateDifferentInstances_ForDifferentServiceIds()
    {
        // Act
        var service1 = await _factory.GetOrCreateAsync("TestService:test-1");
        var service2 = await _factory.GetOrCreateAsync("TestService:test-2");

        // Assert
        service1.Should().NotBeSameAs(service2);
        service1.ServiceId.Should().Be("TestService:test-1");
        service2.ServiceId.Should().Be("TestService:test-2");
        _factory.ActiveCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldThrowException_WhenServiceIdIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _factory.GetOrCreateAsync(null!).AsTask());
    }

    [Fact]
    public async Task GetOrCreateAsync_ShouldThrowException_WhenServiceIdIsWhitespace()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _factory.GetOrCreateAsync("   ").AsTask());
    }

    [Fact]
    public async Task TryGet_ShouldReturnFalse_WhenInstanceNotExists()
    {
        // Act
        var result = _factory.TryGet("TestService:test-1", out var service);

        // Assert
        result.Should().BeFalse();
        service.Should().BeNull();
    }

    [Fact]
    public async Task TryGet_ShouldReturnTrue_WhenInstanceExists()
    {
        // Arrange
        var createdService = await _factory.GetOrCreateAsync("TestService:test-1");

        // Act
        var result = _factory.TryGet("TestService:test-1", out var service);

        // Assert
        result.Should().BeTrue();
        service.Should().BeSameAs(createdService);
    }

    [Fact]
    public async Task RemoveAsync_ShouldRemoveInstance_WhenExists()
    {
        // Arrange
        await _factory.GetOrCreateAsync("TestService:test-1");
        _factory.ActiveCount.Should().Be(1);

        // Act
        var removed = await _factory.RemoveAsync("TestService:test-1");

        // Assert
        removed.Should().BeTrue();
        _factory.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task RemoveAsync_ShouldReturnFalse_WhenInstanceNotExists()
    {
        // Act
        var removed = await _factory.RemoveAsync("TestService:nonexistent");

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveServiceIds_ShouldReturnAllActiveServiceIds()
    {
        // Arrange
        await _factory.GetOrCreateAsync("TestService:test-1");
        await _factory.GetOrCreateAsync("TestService:test-2");
        await _factory.GetOrCreateAsync("TestService:test-3");

        // Act
        var activeIds = _factory.GetActiveServiceIds();

        // Assert
        activeIds.Should().HaveCount(3);
        activeIds.Should().Contain("TestService:test-1");
        activeIds.Should().Contain("TestService:test-2");
        activeIds.Should().Contain("TestService:test-3");
    }

    [Fact]
    public async Task Metrics_ShouldTrackCacheHits()
    {
        // Arrange
        await _factory.GetOrCreateAsync("TestService:test-1"); // Miss
        await _factory.GetOrCreateAsync("TestService:test-1"); // Hit
        await _factory.GetOrCreateAsync("TestService:test-1"); // Hit

        // Act
        var metrics = (IPulseServiceFactoryMetrics)_factory;

        // Assert
        metrics.CacheHits.Should().Be(2);
        metrics.CacheMisses.Should().Be(1);
        metrics.CacheHitRate.Should().BeApproximately(0.6667, 0.01); // 2/3
    }

    [Fact]
    public async Task Metrics_ShouldTrackTotalCreatedAndRemoved()
    {
        // Arrange
        await _factory.GetOrCreateAsync("TestService:test-1");
        await _factory.GetOrCreateAsync("TestService:test-2");
        await _factory.RemoveAsync("TestService:test-1");

        // Act
        var metrics = (IPulseServiceFactoryMetrics)_factory;

        // Assert
        metrics.TotalCreated.Should().Be(2);
        metrics.TotalRemoved.Should().Be(1);
        metrics.ActiveInstances.Should().Be(1);
    }

    [Fact]
    public async Task ServiceLifecycle_OnActivateAsync_ShouldBeCalled()
    {
        // Arrange
        using var lifecycleFactory = new PulseServiceFactory<TestServiceWithLifecycle>(
            serviceId => new TestServiceWithLifecycle(serviceId),
            _options,
            NullLogger<PulseServiceFactory<TestServiceWithLifecycle>>.Instance);

        // Act
        var service = await lifecycleFactory.GetOrCreateAsync("TestService:test-1");

        // Assert
        service.ActivateCalled.Should().BeTrue();
        service.DeactivateCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ServiceLifecycle_OnDeactivateAsync_ShouldBeCalled_WhenRemoved()
    {
        // Arrange
        using var lifecycleFactory = new PulseServiceFactory<TestServiceWithLifecycle>(
            serviceId => new TestServiceWithLifecycle(serviceId),
            _options,
            NullLogger<PulseServiceFactory<TestServiceWithLifecycle>>.Instance);

        var service = await lifecycleFactory.GetOrCreateAsync("TestService:test-1");

        // Act
        await lifecycleFactory.RemoveAsync("TestService:test-1");

        // Assert
        service.DeactivateCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ServiceLifecycle_OnActivateAsync_Exception_ShouldThrowServiceActivationException()
    {
        // Arrange
        using var lifecycleFactory = new PulseServiceFactory<TestServiceWithFailingActivate>(
            serviceId => new TestServiceWithFailingActivate(serviceId),
            _options,
            NullLogger<PulseServiceFactory<TestServiceWithFailingActivate>>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ServiceActivationException>(
            () => lifecycleFactory.GetOrCreateAsync("TestService:test-1").AsTask());

        // 实例应该被移除
        lifecycleFactory.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task Dispose_ShouldRemoveAllInstances()
    {
        // Arrange
        using var testFactory = new PulseServiceFactory<TestService>(
            serviceId => new TestService(serviceId),
            _options,
            NullLogger<PulseServiceFactory<TestService>>.Instance);

        await testFactory.GetOrCreateAsync("TestService:test-1");
        await testFactory.GetOrCreateAsync("TestService:test-2");
        await testFactory.GetOrCreateAsync("TestService:test-3");

        testFactory.ActiveCount.Should().Be(3);

        // Act
        testFactory.Dispose();

        // Assert
        testFactory.ActiveCount.Should().Be(0);
    }
}

// ============================================================================
// 测试用的服务类
// ============================================================================

public class TestService : IPulseService
{
    public string ServiceName => "TestService";
    public string ServiceId { get; }

    public TestService(string serviceId)
    {
        ServiceId = serviceId;
    }
}

public class TestServiceWithLifecycle : IPulseService, IServiceLifecycle
{
    public string ServiceName => "TestService";
    public string ServiceId { get; }

    public bool ActivateCalled { get; private set; }
    public bool DeactivateCalled { get; private set; }
    public bool HealthCheckCalled { get; private set; }

    public TestServiceWithLifecycle(string serviceId)
    {
        ServiceId = serviceId;
    }

    public Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        ActivateCalled = true;
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        DeactivateCalled = true;
        return Task.CompletedTask;
    }

    public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        HealthCheckCalled = true;
        return Task.FromResult(true);
    }
}

public class TestServiceWithFailingActivate : IPulseService, IServiceLifecycle
{
    public string ServiceName => "TestService";
    public string ServiceId { get; }

    public TestServiceWithFailingActivate(string serviceId)
    {
        ServiceId = serviceId;
    }

    public Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Activation failed");
    }

    public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<bool> OnHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}
