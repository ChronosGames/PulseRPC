using FluentAssertions;
using PulseRPC.Scheduling;
using Xunit;

namespace PulseRPC.Server.Tests.Scheduling;

public class ServiceContextTests
{
    [Fact]
    public void ServiceId_CanBeNullInitially()
    {
        // Arrange & Act
        var context = CreateTestContext(serviceId: null);

        // Assert
        context.ServiceId.Should().BeNull();
    }

    [Fact]
    public void ServiceId_CanBeSetDuringAuthentication()
    {
        // Arrange
        var context = CreateTestContext(serviceId: null);

        // Act
        context.ServiceId = "player123";

        // Assert
        context.ServiceId.Should().Be("player123");
    }

    [Fact]
    public void IsAuthenticated_ReturnsTrueWhenServiceIdIsSet()
    {
        // Arrange
        var context = CreateTestContext(serviceId: "player123");

        // Act & Assert
        context.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_ReturnsFalseWhenServiceIdIsNull()
    {
        // Arrange
        var context = CreateTestContext(serviceId: null);

        // Act & Assert
        context.IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  ")]
    public void IsAuthenticated_ReturnsFalseWhenServiceIdIsWhitespace(string serviceId)
    {
        // Arrange
        var context = CreateTestContext(serviceId: null);
        context.ServiceId = serviceId;

        // Act & Assert
        context.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void ConnectionId_IsAlwaysAvailable()
    {
        // Arrange
        var connectionId = "conn-12345";
        var context = CreateTestContext(connectionId: connectionId);

        // Act & Assert
        context.ConnectionId.Should().Be(connectionId);
        context.ConnectionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ServiceName_IsPopulatedFromChannelAttribute()
    {
        // Arrange
        var serviceName = "PlayerService";
        var context = CreateTestContext(serviceName: serviceName);

        // Act & Assert
        context.ServiceName.Should().Be(serviceName);
    }

    [Fact]
    public void ServiceContext_AllPropertiesAccessibleAfterConstruction()
    {
        // Arrange
        var connectionId = "conn-123";
        var serviceName = "PlayerService";
        var serviceId = "player456";

        // Act
        var context = CreateTestContext(
            connectionId: connectionId,
            serviceName: serviceName,
            serviceId: serviceId);

        // Assert
        context.ConnectionId.Should().Be(connectionId);
        context.ServiceName.Should().Be(serviceName);
        context.ServiceId.Should().Be(serviceId);
        context.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void ServiceId_CanBeChangedFromNullToValue()
    {
        // Arrange
        var context = CreateTestContext(serviceId: null);
        context.IsAuthenticated.Should().BeFalse();

        // Act
        context.ServiceId = "player789";

        // Assert
        context.ServiceId.Should().Be("player789");
        context.IsAuthenticated.Should().BeTrue();
    }

    // Helper method to create a test implementation of IServiceContext
    private IServiceContext CreateTestContext(
        string connectionId = "test-conn",
        string serviceName = "TestService",
        string? serviceId = null)
    {
        return new TestServiceContext(connectionId, serviceName, serviceId);
    }

    // Test implementation of IServiceContext
    private class TestServiceContext : IServiceContext
    {
        public TestServiceContext(string connectionId, string serviceName, string? serviceId)
        {
            ConnectionId = connectionId;
            ServiceName = serviceName;
            ServiceId = serviceId;
        }

        public string? ServiceId { get; set; }
        public string ConnectionId { get; }
        public string ServiceName { get; }
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(ServiceId);
    }
}