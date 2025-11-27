using FluentAssertions;
using PulseRPC.Server.Services;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// GroupManager 单元测试
/// </summary>
public class GroupManagerTests
{
    private readonly GroupManager _groupManager;

    public GroupManagerTests()
    {
        _groupManager = new GroupManager();
    }

    [Fact]
    public async Task AddToGroupAsync_ShouldAddConnectionToGroup()
    {
        // Arrange
        const string connectionId = "conn-1";
        const string groupName = "room-1";

        // Act
        await _groupManager.AddToGroupAsync(connectionId, groupName);

        // Assert
        _groupManager.IsInGroup(connectionId, groupName).Should().BeTrue();
        _groupManager.GetGroupConnections(groupName).Should().Contain(connectionId);
        _groupManager.GetConnectionGroups(connectionId).Should().Contain(groupName);
    }

    [Fact]
    public async Task AddToGroupAsync_ShouldSupportMultipleConnectionsInGroup()
    {
        // Arrange
        const string groupName = "room-1";

        // Act
        await _groupManager.AddToGroupAsync("conn-1", groupName);
        await _groupManager.AddToGroupAsync("conn-2", groupName);
        await _groupManager.AddToGroupAsync("conn-3", groupName);

        // Assert
        var connections = _groupManager.GetGroupConnections(groupName);
        connections.Should().HaveCount(3);
        _groupManager.GetGroupSize(groupName).Should().Be(3);
    }

    [Fact]
    public async Task AddToGroupAsync_ShouldSupportConnectionInMultipleGroups()
    {
        // Arrange
        const string connectionId = "conn-1";

        // Act
        await _groupManager.AddToGroupAsync(connectionId, "room-1");
        await _groupManager.AddToGroupAsync(connectionId, "room-2");
        await _groupManager.AddToGroupAsync(connectionId, "room-3");

        // Assert
        var groups = _groupManager.GetConnectionGroups(connectionId);
        groups.Should().HaveCount(3);
        groups.Should().Contain("room-1");
        groups.Should().Contain("room-2");
        groups.Should().Contain("room-3");
    }

    [Fact]
    public async Task RemoveFromGroupAsync_ShouldRemoveConnectionFromGroup()
    {
        // Arrange
        const string connectionId = "conn-1";
        const string groupName = "room-1";
        await _groupManager.AddToGroupAsync(connectionId, groupName);

        // Act
        await _groupManager.RemoveFromGroupAsync(connectionId, groupName);

        // Assert
        _groupManager.IsInGroup(connectionId, groupName).Should().BeFalse();
        _groupManager.GetGroupConnections(groupName).Should().NotContain(connectionId);
    }

    [Fact]
    public async Task RemoveFromGroupAsync_ShouldCleanupEmptyGroup()
    {
        // Arrange
        const string groupName = "room-1";
        await _groupManager.AddToGroupAsync("conn-1", groupName);

        // Act
        await _groupManager.RemoveFromGroupAsync("conn-1", groupName);

        // Assert
        _groupManager.GroupExists(groupName).Should().BeFalse();
        _groupManager.GetGroupSize(groupName).Should().Be(0);
    }

    [Fact]
    public async Task RemoveFromAllGroupsAsync_ShouldRemoveConnectionFromAllGroups()
    {
        // Arrange
        const string connectionId = "conn-1";
        await _groupManager.AddToGroupAsync(connectionId, "room-1");
        await _groupManager.AddToGroupAsync(connectionId, "room-2");
        await _groupManager.AddToGroupAsync(connectionId, "room-3");

        // Act
        await _groupManager.RemoveFromAllGroupsAsync(connectionId);

        // Assert
        _groupManager.GetConnectionGroups(connectionId).Should().BeEmpty();
        _groupManager.IsInGroup(connectionId, "room-1").Should().BeFalse();
        _groupManager.IsInGroup(connectionId, "room-2").Should().BeFalse();
        _groupManager.IsInGroup(connectionId, "room-3").Should().BeFalse();
    }

    [Fact]
    public async Task GetGroupConnections_WithMultipleGroups_ShouldReturnUnion()
    {
        // Arrange
        await _groupManager.AddToGroupAsync("conn-1", "room-1");
        await _groupManager.AddToGroupAsync("conn-2", "room-1");
        await _groupManager.AddToGroupAsync("conn-3", "room-2");
        await _groupManager.AddToGroupAsync("conn-4", "room-2");

        // Act
        var connections = _groupManager.GetGroupConnections(new[] { "room-1", "room-2" });

        // Assert
        connections.Should().HaveCount(4);
        connections.Should().Contain("conn-1");
        connections.Should().Contain("conn-2");
        connections.Should().Contain("conn-3");
        connections.Should().Contain("conn-4");
    }

    [Fact]
    public async Task GetGroupConnections_WithOverlappingGroups_ShouldReturnDeduplicatedResults()
    {
        // Arrange - conn-1 is in both groups
        await _groupManager.AddToGroupAsync("conn-1", "room-1");
        await _groupManager.AddToGroupAsync("conn-1", "room-2");
        await _groupManager.AddToGroupAsync("conn-2", "room-1");

        // Act
        var connections = _groupManager.GetGroupConnections(new[] { "room-1", "room-2" });

        // Assert
        connections.Should().HaveCount(2); // conn-1 and conn-2, deduplicated
    }

    [Fact]
    public void GroupExists_ShouldReturnCorrectResult()
    {
        // Assert - non-existent group
        _groupManager.GroupExists("room-1").Should().BeFalse();
    }

    [Fact]
    public async Task GroupExists_AfterAdding_ShouldReturnTrue()
    {
        // Arrange
        await _groupManager.AddToGroupAsync("conn-1", "room-1");

        // Assert
        _groupManager.GroupExists("room-1").Should().BeTrue();
    }

    [Fact]
    public void Operations_WithNullOrEmpty_ShouldHandleGracefully()
    {
        // GetGroupConnections with null
        _groupManager.GetGroupConnections((string)null!).Should().BeEmpty();
        _groupManager.GetGroupConnections("").Should().BeEmpty();

        // GetConnectionGroups with null
        _groupManager.GetConnectionGroups(null!).Should().BeEmpty();
        _groupManager.GetConnectionGroups("").Should().BeEmpty();

        // IsInGroup with null
        _groupManager.IsInGroup(null!, "room").Should().BeFalse();
        _groupManager.IsInGroup("conn", null!).Should().BeFalse();

        // GetGroupSize with null
        _groupManager.GetGroupSize(null!).Should().Be(0);
        _groupManager.GetGroupSize("").Should().Be(0);

        // GroupExists with null
        _groupManager.GroupExists(null!).Should().BeFalse();
        _groupManager.GroupExists("").Should().BeFalse();
    }
}

