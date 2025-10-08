using FluentAssertions;
using PulseRPC.Scheduling;
using Xunit;

namespace PulseRPC.Server.Tests.Scheduling;

public class ServiceSchedulingKeyTests
{
    [Fact]
    public void Constructor_WithValidInputs_CreatesKey()
    {
        // Arrange
        var serviceName = "PlayerService";
        var serviceId = "player123";

        // Act
        var key = new ServiceSchedulingKey(serviceName, serviceId);

        // Assert
        key.ServiceName.Should().Be(serviceName);
        key.ServiceId.Should().Be(serviceId);
    }

    [Theory]
    [InlineData(null, "service123")]
    [InlineData("", "service123")]
    [InlineData(" ", "service123")]
    public void Constructor_WithInvalidServiceName_ThrowsArgumentException(string? serviceName, string serviceId)
    {
        // Act
        Action act = () => new ServiceSchedulingKey(serviceName!, serviceId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceName");
    }

    [Theory]
    [InlineData("PlayerService", null)]
    [InlineData("PlayerService", "")]
    [InlineData("PlayerService", " ")]
    public void Constructor_WithInvalidServiceId_ThrowsArgumentException(string serviceName, string? serviceId)
    {
        // Act
        Action act = () => new ServiceSchedulingKey(serviceName, serviceId!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("serviceId");
    }

    [Fact]
    public void Equals_WithSameServiceNameAndServiceId_ReturnsTrue()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player123");

        // Act & Assert
        key1.Equals(key2).Should().BeTrue();
        (key1 == key2).Should().BeTrue();
        (key1 != key2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithDifferentServiceName_ReturnsFalse()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("ChatService", "player123");

        // Act & Assert
        key1.Equals(key2).Should().BeFalse();
        (key1 == key2).Should().BeFalse();
        (key1 != key2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WithDifferentServiceId_ReturnsFalse()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player456");

        // Act & Assert
        key1.Equals(key2).Should().BeFalse();
        (key1 == key2).Should().BeFalse();
        (key1 != key2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_WithSameValues_ReturnsSameHashCode()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player123");

        // Act & Assert
        key1.GetHashCode().Should().Be(key2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_WithDifferentValues_ReturnsDifferentHashCode()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player456");

        // Act & Assert
        key1.GetHashCode().Should().NotBe(key2.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsCorrectFormat()
    {
        // Arrange
        var key = new ServiceSchedulingKey("PlayerService", "player123");

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("PlayerService:player123");
    }

    [Fact]
    public void ServiceSchedulingKey_CanBeUsedAsDictionaryKey()
    {
        // Arrange
        var dictionary = new Dictionary<ServiceSchedulingKey, string>();
        var key1 = new ServiceSchedulingKey("PlayerService", "player123");
        var key2 = new ServiceSchedulingKey("PlayerService", "player123");
        var key3 = new ServiceSchedulingKey("ChatService", "player123");

        // Act
        dictionary[key1] = "value1";
        dictionary[key3] = "value2";

        // Assert
        dictionary[key2].Should().Be("value1"); // Same as key1
        dictionary.Should().HaveCount(2);
    }
}