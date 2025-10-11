using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using PulseRPC.Transport;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Receives network messages from transport connections and buffers incomplete messages.
/// Handles FR-001 to FR-006: message reception, buffering, parsing, and error handling.
/// </summary>
public sealed class MessageReceiver : IDisposable
{
    private readonly IPulseServerTransport _transport;
    private readonly MessageParser _parser;
    private readonly ConcurrentDictionary<string, ConnectionBuffer> _connectionBuffers;
    private readonly MessageReceiverOptions _options;
    private bool _disposed;

    /// <summary>
    /// Event raised when a complete message is received and parsed.
    /// </summary>
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    /// <summary>
    /// Event raised when message parsing fails.
    /// </summary>
    public event EventHandler<MessageParseErrorEventArgs>? ParseError;

    private long _totalMessagesReceived;
    private long _totalParseErrors;

    /// <summary>
    /// Gets the total number of messages received.
    /// </summary>
    public long TotalMessagesReceived => Interlocked.Read(ref _totalMessagesReceived);

    /// <summary>
    /// Gets the total number of parse errors.
    /// </summary>
    public long TotalParseErrors => Interlocked.Read(ref _totalParseErrors);

    public MessageReceiver(
        IPulseServerTransport transport,
        MessageReceiverOptions? options = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _parser = new MessageParser();
        _connectionBuffers = new ConcurrentDictionary<string, ConnectionBuffer>();
        _options = options ?? new MessageReceiverOptions();

        // Subscribe to transport events
        _transport.ConnectionAccepted += OnConnectionAccepted;
        _transport.ConnectionClosed += OnConnectionClosed;
    }

    /// <summary>
    /// Starts receiving messages from the transport.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MessageReceiver));

        if (!_transport.IsListening)
        {
            await _transport.StartAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Stops receiving messages and clears all buffers.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        // Clear all connection buffers
        _connectionBuffers.Clear();

        if (_transport.IsListening)
        {
            await _transport.StopAsync(cancellationToken);
        }
    }

    private void OnConnectionAccepted(object? sender, ServerConnectionEventArgs e)
    {
        var connectionId = e.Transport.Id;
        var buffer = new ConnectionBuffer(connectionId, _options.MaxBufferSize);
        _connectionBuffers.TryAdd(connectionId, buffer);

        // Subscribe to data received events
        e.Transport.DataReceived += OnDataReceived;
    }

    private void OnConnectionClosed(object? sender, ConnectionClosedEventArgs e)
    {
        // Remove and dispose buffer for this connection
        if (_connectionBuffers.TryRemove(e.ConnectionId, out var buffer))
        {
            buffer.Dispose();
        }
    }

    private async void OnDataReceived(object? sender, TransportDataEventArgs e)
    {
        if (sender is not IServerTransport transport)
            return;

        var connectionId = transport.Id;

        if (!_connectionBuffers.TryGetValue(connectionId, out var buffer))
        {
            // Connection not tracked (shouldn't happen)
            return;
        }

        try
        {
            // Append received data to connection buffer
            buffer.Append(e.Data);

            // Try to parse complete messages from buffer
            while (buffer.TryReadMessage(out var messageData))
            {
                var parseResult = await _parser.ParseAsync(messageData);

                if (parseResult.IsSuccess && parseResult.Message != null)
                {
                    // Successfully parsed message
                    Interlocked.Increment(ref _totalMessagesReceived);

                    var remoteEndpoint = transport.RemoteEndPoint?.ToString() ?? "unknown";
                    MessageReceived?.Invoke(this, new MessageReceivedEventArgs
                    {
                        Message = parseResult.Message,
                        ConnectionId = connectionId,
                        RemoteEndpoint = remoteEndpoint
                    });
                }
                else
                {
                    // Parse error
                    Interlocked.Increment(ref _totalParseErrors);

                    ParseError?.Invoke(this, new MessageParseErrorEventArgs
                    {
                        ConnectionId = connectionId,
                        ErrorType = parseResult.ErrorType ?? "Unknown",
                        ErrorMessage = parseResult.ErrorMessage ?? "Parse failed",
                        RawData = messageData
                    });
                }
            }
        }
        catch (Exception ex)
        {
            // Unexpected error during processing
            Interlocked.Increment(ref _totalParseErrors);

            ParseError?.Invoke(this, new MessageParseErrorEventArgs
            {
                ConnectionId = connectionId,
                ErrorType = "UnexpectedException",
                ErrorMessage = ex.Message,
                RawData = e.Data
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Unsubscribe from events
        _transport.ConnectionAccepted -= OnConnectionAccepted;
        _transport.ConnectionClosed -= OnConnectionClosed;

        // Clear buffers
        foreach (var buffer in _connectionBuffers.Values)
        {
            buffer.Dispose();
        }
        _connectionBuffers.Clear();
    }

    /// <summary>
    /// Buffer for incomplete messages from a single connection.
    /// </summary>
    private sealed class ConnectionBuffer : IDisposable
    {
        private readonly string _connectionId;
        private readonly int _maxBufferSize;
        private byte[] _buffer;
        private int _bufferOffset;
        private readonly object _lock = new();

        public ConnectionBuffer(string connectionId, int maxBufferSize)
        {
            _connectionId = connectionId;
            _maxBufferSize = maxBufferSize;
            _buffer = ArrayPool<byte>.Shared.Rent(4096); // Start with 4KB
            _bufferOffset = 0;
        }

        public void Append(ReadOnlyMemory<byte> data)
        {
            lock (_lock)
            {
                // Check if we need to grow the buffer
                if (_bufferOffset + data.Length > _buffer.Length)
                {
                    // Check max size limit
                    if (_bufferOffset + data.Length > _maxBufferSize)
                    {
                        throw new InvalidOperationException(
                            $"Connection {_connectionId} buffer would exceed max size of {_maxBufferSize} bytes");
                    }

                    // Grow buffer (double size or exact fit, whichever is larger)
                    var newSize = Math.Max(_buffer.Length * 2, _bufferOffset + data.Length);
                    var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                    Array.Copy(_buffer, newBuffer, _bufferOffset);
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _buffer = newBuffer;
                }

                // Append data
                data.Span.CopyTo(_buffer.AsSpan(_bufferOffset));
                _bufferOffset += data.Length;
            }
        }

        public bool TryReadMessage(out ReadOnlyMemory<byte> messageData)
        {
            lock (_lock)
            {
                // Need at least 4 bytes for message length header
                if (_bufferOffset < 4)
                {
                    messageData = default;
                    return false;
                }

                // Read message length (first 4 bytes, little-endian)
                var messageLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(
                    _buffer.AsSpan(0, 4));

                // Validate message length
                if (messageLength <= 0 || messageLength > _maxBufferSize)
                {
                    // Invalid message length - clear buffer
                    _bufferOffset = 0;
                    messageData = default;
                    return false;
                }

                // Check if we have the complete message
                var totalMessageSize = 4 + messageLength;
                if (_bufferOffset < totalMessageSize)
                {
                    // Incomplete message
                    messageData = default;
                    return false;
                }

                // Extract message data (excluding length header)
                var messageBytes = new byte[messageLength];
                Array.Copy(_buffer, 4, messageBytes, 0, messageLength);
                messageData = messageBytes;

                // Remove processed message from buffer
                if (_bufferOffset > totalMessageSize)
                {
                    // There's more data after this message - shift it to the beginning
                    Array.Copy(_buffer, totalMessageSize, _buffer, 0, _bufferOffset - totalMessageSize);
                    _bufferOffset -= totalMessageSize;
                }
                else
                {
                    // Buffer is now empty
                    _bufferOffset = 0;
                }

                return true;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _buffer = null!;
                }
            }
        }
    }
}

/// <summary>
/// Options for MessageReceiver configuration.
/// </summary>
public sealed class MessageReceiverOptions
{
    /// <summary>
    /// Maximum buffer size per connection (default: 16MB).
    /// </summary>
    public int MaxBufferSize { get; set; } = 16 * 1024 * 1024;
}

/// <summary>
/// Event arguments for successfully received messages.
/// </summary>
public sealed class MessageReceivedEventArgs : EventArgs
{
    public required RpcMessage Message { get; init; }
    public required string ConnectionId { get; init; }
    public required string RemoteEndpoint { get; init; }
}

/// <summary>
/// Event arguments for message parse errors.
/// </summary>
public sealed class MessageParseErrorEventArgs : EventArgs
{
    public required string ConnectionId { get; init; }
    public required string ErrorType { get; init; }
    public required string ErrorMessage { get; init; }
    public required ReadOnlyMemory<byte> RawData { get; init; }
}
