using FluentAssertions;
using PulseRPC.Scheduling;
using System;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for ServiceSchedulingKey (T011)
/// Tests hash consistency, equality, and key generation
/// </summary>
public class ServiceSchedulingKeyTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldCreateKey_WithValidInputs()
    {
        // Arrange
        var serviceName = "ChatRoom";
        var serviceId = "room-123";

        // Act
        var key = new ServiceSchedulingKey(serviceName, serviceId);

        // Assert
        key.ServiceName.Should().Be(serviceName);
        key.ServiceId.Should().Be(serviceId);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenServiceNameIsNull()
    {
        // Arrange
        string? serviceName = null;
        var serviceId = "room-123";

        // Act
        Action act = () => new ServiceSchedulingKey(serviceName!, serviceId);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*serviceName*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenServiceIdIsNull()
    {
        // Arrange
        var serviceName = "ChatRoom";
        string? serviceId = null;

        // Act
        Action act = () => new ServiceSchedulingKey(serviceName, serviceId!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*serviceId*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenServiceNameIsWhitespace()
    {
        // Arrange
        var serviceName = "   ";
        var serviceId = "room-123";

        // Act
        Action act = () => new ServiceSchedulingKey(serviceName, serviceId);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Equals_ShouldReturnTrue_ForIdenticalKeys()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        var key2 = new ServiceSchedulingKey("ChatRoom", "room-123");

        // Act & Assert
        key1.Equals(key2).Should().BeTrue();
        (key1 == key2).Should().BeTrue();
        (key1 != key2).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentServiceIds()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        var key2 = new ServiceSchedulingKey("ChatRoom", "room-456");

        // Act & Assert
        key1.Equals(key2).Should().BeFalse();
        (key1 == key2).Should().BeFalse();
        (key1 != key2).Should().BeTrue();
    }

    [Fact]
    public void Equals_ShouldReturnFalse_ForDifferentServiceNames()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        var key2 = new ServiceSchedulingKey("GameRoom", "room-123");

        // Act & Assert
        key1.Equals(key2).Should().BeFalse();
    }

    [Fact]
    public void Equals_ShouldWorkWith_ObjectOverload()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        object key2 = new ServiceSchedulingKey("ChatRoom", "room-123");

        // Act & Assert
        key1.Equals(key2).Should().BeTrue();
    }

    #endregion

    #region GetHashCode Tests

    [Fact]
    public void GetHashCode_ShouldBeConsistent_ForSameKey()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");

        // Act
        var hash1 = key.GetHashCode();
        var hash2 = key.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void GetHashCode_ShouldBeSame_ForEqualKeys()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        var key2 = new ServiceSchedulingKey("ChatRoom", "room-123");

        // Act
        var hash1 = key1.GetHashCode();
        var hash2 = key2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void GetHashCode_ShouldBeDifferent_ForDifferentKeys()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        var key2 = new ServiceSchedulingKey("ChatRoom", "room-456");

        // Act
        var hash1 = key1.GetHashCode();
        var hash2 = key2.GetHashCode();

        // Assert - Different keys should (almost certainly) have different hashes
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void GetHashCode_ShouldDistribute_WellAcrossRange()
    {
        // Arrange - Create many keys with sequential IDs
        var hashes = new System.Collections.Generic.HashSet<int>();

        // Act - Generate 1000 keys with different IDs
        for (int i = 0; i < 1000; i++)
        {
            var key = new ServiceSchedulingKey("ChatRoom", $"room-{i}");
            hashes.Add(key.GetHashCode());
        }

        // Assert - Should have close to 1000 unique hashes (collision rate should be very low)
        hashes.Count.Should().BeGreaterThan(990, "hash collisions should be rare");
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("ChatRoom:room-123");
    }

    [Fact]
    public void ToString_ShouldHandle_SpecialCharacters()
    {
        // Arrange
        var key = new ServiceSchedulingKey("Order-Processor", "order:123-456");

        // Act
        var result = key.ToString();

        // Assert
        result.Should().Be("Order-Processor:order:123-456");
    }

    #endregion

    #region Dictionary Usage Tests

    [Fact]
    public void ServiceSchedulingKey_ShouldWorkAs_DictionaryKey()
    {
        // Arrange
        var dict = new System.Collections.Generic.Dictionary<ServiceSchedulingKey, string>();
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-123");
        var key2 = new ServiceSchedulingKey("ChatRoom", "room-123"); // Same values
        var key3 = new ServiceSchedulingKey("ChatRoom", "room-456"); // Different

        // Act
        dict[key1] = "value1";
        dict[key3] = "value3";

        // Assert
        dict.Should().HaveCount(2);
        dict[key2].Should().Be("value1", "key2 should match key1");
        dict.ContainsKey(key1).Should().BeTrue();
        dict.ContainsKey(key2).Should().BeTrue();
        dict.ContainsKey(key3).Should().BeTrue();
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void ServiceSchedulingKey_ShouldHandle_LongStrings()
    {
        // Arrange
        var longServiceName = new string('A', 100);
        var longServiceId = new string('B', 500);

        // Act
        var key = new ServiceSchedulingKey(longServiceName, longServiceId);

        // Assert
        key.ServiceName.Should().Be(longServiceName);
        key.ServiceId.Should().Be(longServiceId);
        key.GetHashCode().Should().NotBe(0);
    }

    [Fact]
    public void ServiceSchedulingKey_ShouldHandle_UnicodeCharacters()
    {
        // Arrange
        var serviceName = "聊天室";
        var serviceId = "房间-123";

        // Act
        var key = new ServiceSchedulingKey(serviceName, serviceId);

        // Assert
        key.ServiceName.Should().Be(serviceName);
        key.ServiceId.Should().Be(serviceId);
        key.ToString().Should().Be("聊天室:房间-123");
    }

    #endregion
}
