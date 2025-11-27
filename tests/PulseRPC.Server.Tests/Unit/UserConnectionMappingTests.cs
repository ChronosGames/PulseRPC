using FluentAssertions;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// UserConnectionMapping 单元测试
/// </summary>
public class UserConnectionMappingTests
{
    private readonly UserConnectionMapping _mapping;

    public UserConnectionMappingTests()
    {
        _mapping = new UserConnectionMapping();
    }

    [Fact]
    public void Add_ShouldAddUserConnection()
    {
        // Arrange
        const string userId = "user-1";
        const string connectionId = "conn-1";

        // Act
        _mapping.Add(userId, connectionId);

        // Assert
        _mapping.GetConnections(userId).Should().Contain(connectionId);
        _mapping.GetUserId(connectionId).Should().Be(userId);
        _mapping.IsUserOnline(userId).Should().BeTrue();
    }

    [Fact]
    public void Add_ShouldSupportMultipleConnectionsPerUser()
    {
        // Arrange
        const string userId = "user-1";

        // Act
        _mapping.Add(userId, "conn-1");
        _mapping.Add(userId, "conn-2");
        _mapping.Add(userId, "conn-3");

        // Assert
        var connections = _mapping.GetConnections(userId);
        connections.Should().HaveCount(3);
        connections.Should().Contain("conn-1");
        connections.Should().Contain("conn-2");
        connections.Should().Contain("conn-3");
    }

    [Fact]
    public void Remove_ShouldRemoveConnection()
    {
        // Arrange
        const string userId = "user-1";
        _mapping.Add(userId, "conn-1");
        _mapping.Add(userId, "conn-2");

        // Act
        _mapping.Remove(userId, "conn-1");

        // Assert
        var connections = _mapping.GetConnections(userId);
        connections.Should().HaveCount(1);
        connections.Should().Contain("conn-2");
        _mapping.IsUserOnline(userId).Should().BeTrue();
    }

    [Fact]
    public void Remove_ShouldCleanupWhenLastConnectionRemoved()
    {
        // Arrange
        const string userId = "user-1";
        _mapping.Add(userId, "conn-1");

        // Act
        _mapping.Remove(userId, "conn-1");

        // Assert
        _mapping.GetConnections(userId).Should().BeEmpty();
        _mapping.IsUserOnline(userId).Should().BeFalse();
        _mapping.OnlineUserCount.Should().Be(0);
    }

    [Fact]
    public void RemoveByConnection_ShouldRemoveAndReturnUserId()
    {
        // Arrange
        const string userId = "user-1";
        const string connectionId = "conn-1";
        _mapping.Add(userId, connectionId);

        // Act
        var result = _mapping.RemoveByConnection(connectionId);

        // Assert
        result.Should().Be(userId);
        _mapping.GetUserId(connectionId).Should().BeNull();
        _mapping.IsUserOnline(userId).Should().BeFalse();
    }

    [Fact]
    public void GetConnections_WithMultipleUserIds_ShouldReturnAllConnections()
    {
        // Arrange
        _mapping.Add("user-1", "conn-1");
        _mapping.Add("user-1", "conn-2");
        _mapping.Add("user-2", "conn-3");
        _mapping.Add("user-3", "conn-4");

        // Act
        var connections = _mapping.GetConnections(new[] { "user-1", "user-2" });

        // Assert
        connections.Should().HaveCount(3);
        connections.Should().Contain("conn-1");
        connections.Should().Contain("conn-2");
        connections.Should().Contain("conn-3");
    }

    [Fact]
    public void GetOnlineUsers_ShouldReturnAllOnlineUsers()
    {
        // Arrange
        _mapping.Add("user-1", "conn-1");
        _mapping.Add("user-2", "conn-2");
        _mapping.Add("user-3", "conn-3");

        // Act
        var users = _mapping.GetOnlineUsers();

        // Assert
        users.Should().HaveCount(3);
        users.Should().Contain("user-1");
        users.Should().Contain("user-2");
        users.Should().Contain("user-3");
    }

    [Fact]
    public void OnlineUserCount_ShouldReturnCorrectCount()
    {
        // Arrange
        _mapping.Add("user-1", "conn-1");
        _mapping.Add("user-2", "conn-2");

        // Act & Assert
        _mapping.OnlineUserCount.Should().Be(2);

        // Add another connection to same user
        _mapping.Add("user-1", "conn-3");
        _mapping.OnlineUserCount.Should().Be(2); // Still 2 users
    }

    [Fact]
    public void Operations_WithNullOrEmpty_ShouldHandleGracefully()
    {
        // GetConnections with null
        _mapping.GetConnections((string)null!).Should().BeEmpty();
        _mapping.GetConnections("").Should().BeEmpty();

        // GetUserId with null
        _mapping.GetUserId(null!).Should().BeNull();
        _mapping.GetUserId("").Should().BeNull();

        // IsUserOnline with null
        _mapping.IsUserOnline(null!).Should().BeFalse();
        _mapping.IsUserOnline("").Should().BeFalse();

        // RemoveByConnection with null
        _mapping.RemoveByConnection(null!).Should().BeNull();
        _mapping.RemoveByConnection("").Should().BeNull();
    }
}

