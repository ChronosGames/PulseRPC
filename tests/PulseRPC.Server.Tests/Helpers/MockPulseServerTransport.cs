using PulseRPC.Transport;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server.Tests;

/// <summary>
/// Mock implementation of IPulseServerTransport for testing.
/// </summary>
public class MockPulseServerTransport : IPulseServerTransport
{
    private readonly Dictionary<string, MockServerTransport> _connections = new();
    private bool _isListening;

    public string Name => "MockTransport";
    public TransportType Type => TransportType.TCP;
    public EndPoint LocalEndPoint => new IPEndPoint(IPAddress.Loopback, 8080);
    public bool IsListening => _isListening;

    public event EventHandler<ServerConnectionEventArgs>? ConnectionAccepted;
    public event EventHandler<ConnectionClosedEventArgs>? ConnectionClosed;

    public int ActiveConnectionCount => _connections.Count;
    public long TotalConnectionsAccepted { get; private set; }
    public long TotalConnectionsClosed { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _isListening = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isListening = false;
        _connections.Clear();
        return Task.CompletedTask;
    }

    public IServerTransport? GetConnection(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var conn) ? conn : null;
    }

    public void Dispose()
    {
        _connections.Clear();
    }

    // Test helper methods
    public void SimulateConnectionAccepted(string connectionId)
    {
        var mockTransport = new MockServerTransport(connectionId);
        _connections[connectionId] = mockTransport;
        TotalConnectionsAccepted++;

        ConnectionAccepted?.Invoke(this, new ServerConnectionEventArgs(mockTransport));
    }

    public void SimulateConnectionClosed(string connectionId)
    {
        if (_connections.Remove(connectionId))
        {
            TotalConnectionsClosed++;
            ConnectionClosed?.Invoke(this, new ConnectionClosedEventArgs
            {
                ConnectionId = connectionId,
                Reason = ConnectionCloseReason.ClientDisconnect
            });
        }
    }

    public void SimulateDataReceived(string connectionId, ReadOnlyMemory<byte> data)
    {
        if (_connections.TryGetValue(connectionId, out var conn))
        {
            conn.SimulateDataReceived(data);
        }
    }
}

/// <summary>
/// Mock implementation of IServerTransport for individual connections.
/// </summary>
public class MockServerTransport : IServerTransport
{
    public MockServerTransport(string id)
    {
        Id = id;
        RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12345);
    }

    public string Id { get; }
    public TransportType Type => TransportType.TCP;
    public bool IsConnected => State == PulseRPC.Transport.ConnectionState.Connected;
    public PulseRPC.Transport.ConnectionState State { get; private set; } = PulseRPC.Transport.ConnectionState.Connected;
    public EndPoint LocalEndPoint => new IPEndPoint(IPAddress.Loopback, 8080);
    public EndPoint RemoteEndPoint { get; }

    /// <summary>Captured frames passed to <see cref="SendAsync"/> (test helper).</summary>
    public List<byte[]> SentFrames { get; } = new();

    /// <summary>Value returned by <see cref="SendAsync"/> (test helper).</summary>
    public bool SendResult { get; set; } = true;

    public event EventHandler<TransportStateEventArgs>? StateChanged;
    public event EventHandler<TransportDataEventArgs>? DataReceived;

    public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        SentFrames.Add(data.ToArray());
        return Task.FromResult(SendResult);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        State = PulseRPC.Transport.ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        State = PulseRPC.Transport.ConnectionState.Disconnected;
    }

    // Test helper
    public void SimulateDataReceived(ReadOnlyMemory<byte> data)
    {
        DataReceived?.Invoke(this, new TransportDataEventArgs(data));
    }

    // Test helper: simulate a transport state transition (e.g., disconnect)
    public void SimulateStateChanged(PulseRPC.Transport.ConnectionState newState)
    {
        var previous = State;
        State = newState;
        StateChanged?.Invoke(this, new TransportStateEventArgs(Id, previous, newState));
    }
}
