using FluentAssertions;
using PulseRPC.Server.Core;
using PulseRPC.Server.Models;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for ConnectionManager (T057).
/// Tests connection lifecycle, state transitions, and resource leak detection.
/// </summary>
public class ConnectionManagerTests
{
    [Fact]
    public void ConnectionManager_ShouldInitialize_Successfully()
    {
        // Arrange & Act
        var manager = new ConnectionManager();

        // Assert
        manager.Should().NotBeNull();
        manager.ActiveConnectionCount.Should().Be(0);
        manager.TotalConnectionsAccepted.Should().Be(0);
    }

    [Fact]
    public void TryAddConnection_ShouldAddConnection_Successfully()
    {
        // Arrange
        var manager = new ConnectionManager();
        var connection = CreateTestConnection("conn1");

        // Act
        var result = manager.TryAddConnection(connection);

        // Assert
        result.Should().BeTrue();
        manager.ActiveConnectionCount.Should().Be(1);
        manager.TotalConnectionsAccepted.Should().Be(1);
        connection.State.Should().Be(ConnectionState.Active);
    }

    [Fact]
    public void TryAddConnection_ShouldThrow_WhenConnectionIsNull()
    {
        // Arrange
        var manager = new ConnectionManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.TryAddConnection(null!));
    }

    [Fact]
    public void TryAddConnection_ShouldRejectDuplicate_WhenSameConnectionIdExists()
    {
        // Arrange
        var manager = new ConnectionManager();
        var connection1 = CreateTestConnection("conn1");
        var connection2 = CreateTestConnection("conn1"); // Same ID

        manager.TryAddConnection(connection1);

        // Act
        var result = manager.TryAddConnection(connection2);

        // Assert
        result.Should().BeFalse();
        manager.ActiveConnectionCount.Should().Be(1);
    }

    [Fact]
    public void TryAddConnection_ShouldReject_WhenMaxConnectionsReached()
    {
        // Arrange
        var options = new ConnectionManagerOptions { MaxConnections = 2 };
        var manager = new ConnectionManager(options);

        var conn1 = CreateTestConnection("conn1");
        var conn2 = CreateTestConnection("conn2");
        var conn3 = CreateTestConnection("conn3");

        // Act
        manager.TryAddConnection(conn1).Should().BeTrue();
        manager.TryAddConnection(conn2).Should().BeTrue();
        var result3 = manager.TryAddConnection(conn3);

        // Assert
        result3.Should().BeFalse();
        manager.ActiveConnectionCount.Should().Be(2);
        manager.TotalConnectionsFailed.Should().Be(1);
    }

    [Fact]
    public void GetConnection_ShouldReturnConnection_WhenExists()
    {
        // Arrange
        var manager = new ConnectionManager();
        var connection = CreateTestConnection("conn1");
        manager.TryAddConnection(connection);

        // Act
        var retrieved = manager.GetConnection("conn1");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.ConnectionId.Should().Be("conn1");
    }

    [Fact]
    public void GetConnection_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var manager = new ConnectionManager();

        // Act
        var retrieved = manager.GetConnection("nonexistent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void TryRemoveConnection_ShouldRemoveConnection_Successfully()
    {
        // Arrange
        var manager = new ConnectionManager();
        var connection = CreateTestConnection("conn1");
        manager.TryAddConnection(connection);

        // Act
        var result = manager.TryRemoveConnection("conn1");

        // Assert
        result.Should().BeTrue();
        manager.ActiveConnectionCount.Should().Be(0);
        manager.TotalConnectionsClosed.Should().Be(1);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public void TryRemoveConnection_ShouldReturnFalse_WhenNotExists()
    {
        // Arrange
        var manager = new ConnectionManager();

        // Act
        var result = manager.TryRemoveConnection("nonexistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CloseConnectionAsync_ShouldCloseConnection_Successfully()
    {
        // Arrange
        var manager = new ConnectionManager();
        var connection = CreateTestConnection("conn1");
        manager.TryAddConnection(connection);

        // Act
        var result = await manager.CloseConnectionAsync("conn1");

        // Assert
        result.Should().BeTrue();
        manager.ActiveConnectionCount.Should().Be(0);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task CloseAllConnectionsAsync_ShouldCloseAllConnections()
    {
        // Arrange
        var manager = new ConnectionManager();
        var conn1 = CreateTestConnection("conn1");
        var conn2 = CreateTestConnection("conn2");
        var conn3 = CreateTestConnection("conn3");

        manager.TryAddConnection(conn1);
        manager.TryAddConnection(conn2);
        manager.TryAddConnection(conn3);

        // Act
        await manager.CloseAllConnectionsAsync();

        // Assert
        manager.ActiveConnectionCount.Should().Be(0);
        manager.TotalConnectionsClosed.Should().Be(3);
    }

    [Fact]
    public void GetStatistics_ShouldReturnAggregatedStats()
    {
        // Arrange
        var manager = new ConnectionManager();
        var conn1 = CreateTestConnection("conn1");
        var conn2 = CreateTestConnection("conn2");

        manager.TryAddConnection(conn1);
        manager.TryAddConnection(conn2);

        // Simulate activity
        conn1.RecordMessageReceived(100);
        conn1.RecordMessageSent(50);
        conn2.RecordMessageReceived(200);
        conn2.RecordMessageSent(150);

        // Act
        var stats = manager.GetStatistics();

        // Assert
        stats.ActiveConnections.Should().Be(2);
        stats.TotalConnectionsAccepted.Should().Be(2);
        stats.TotalMessagesReceived.Should().Be(2); // 2 messages total
        stats.TotalMessagesSent.Should().Be(2);
        stats.TotalBytesReceived.Should().Be(300);
        stats.TotalBytesSent.Should().Be(200);
    }

    [Fact]
    public void DetectLeakedConnections_ShouldDetectInactiveConnections()
    {
        // Arrange
        var options = new ConnectionManagerOptions
        {
            InactivityTimeout = TimeSpan.FromMilliseconds(100)
        };
        var manager = new ConnectionManager(options);

        var connection = CreateTestConnection("conn1");
        manager.TryAddConnection(connection);

        // Act: Wait for connection to become stale
        System.Threading.Thread.Sleep(150);
        var leakedConnections = manager.DetectLeakedConnections();

        // Assert
        leakedConnections.Should().Contain("conn1");
    }

    [Fact]
    public void GetAllConnections_ShouldReturnAllConnections()
    {
        // Arrange
        var manager = new ConnectionManager();
        var conn1 = CreateTestConnection("conn1");
        var conn2 = CreateTestConnection("conn2");

        manager.TryAddConnection(conn1);
        manager.TryAddConnection(conn2);

        // Act
        var allConnections = manager.GetAllConnections();

        // Assert
        allConnections.Should().HaveCount(2);
        allConnections.Should().Contain(c => c.ConnectionId == "conn1");
        allConnections.Should().Contain(c => c.ConnectionId == "conn2");
    }

    [Fact]
    public void ConnectionManager_ShouldDisposeCleanly()
    {
        // Arrange
        var manager = new ConnectionManager();
        var connection = CreateTestConnection("conn1");
        manager.TryAddConnection(connection);

        // Act
        manager.Dispose();

        // Assert: Should not throw
        manager.ActiveConnectionCount.Should().Be(0);
    }

    // Helper method
    private static ServerConnection CreateTestConnection(string connectionId)
    {
        return new ServerConnection(
            connectionId,
            new IPEndPoint(IPAddress.Loopback, 12345),
            TransportType.TCP);
    }
}
