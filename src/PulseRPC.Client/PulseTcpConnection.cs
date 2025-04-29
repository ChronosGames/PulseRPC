using MemoryPack;
using PulseRPC.Protocol;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace PulseRPC.Client;

/// <summary>
/// Represents a TCP connection to a PulseRPC server.
/// </summary>
public class PulseTcpConnection : IPulseConnection
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private PipeReader? _reader;
    private PipeWriter? _writer;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PulseResponse>> _pendingRequests = new();
    private Task? _receiveTask;
    private CancellationTokenSource? _cts;
    private readonly object _connectionLock = new object();
    private volatile bool _isConnectedFlag;

    /// <inheritdoc />
    public bool IsConnected => _isConnectedFlag && _client?.Connected == true;

    /// <inheritdoc />
    public event Func<PulseEvent, Task>? OnEventReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="PulseTcpConnection"/> class.
    /// </summary>
    /// <param name="host">Server hostname or IP address.</param>
    /// <param name="port">Server port.</param>
    public PulseTcpConnection(string host, int port)
    {
        _host = host;
        _port = port;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_connectionLock)
        {
            if (IsConnected)
            {
                // Already connected or connecting
                return;
            }

            _isConnectedFlag = true; // Set flag early to prevent race conditions
        }

        try
        {
            _client = new TcpClient();
            // Use cancellation token for connect timeout
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // TODO: Make connect timeout configurable
            connectCts.CancelAfter(TimeSpan.FromSeconds(15));

            await _client.ConnectAsync(_host, _port, connectCts.Token);

            if (!_client.Connected)
            {
                _isConnectedFlag = false;
                throw new SocketException((int)SocketError.NotConnected);
            }

            _stream = _client.GetStream();
            _reader = PipeReader.Create(_stream);
            _writer = PipeWriter.Create(_stream);
            _cts = new CancellationTokenSource();

            // Start the message receiving loop
            _receiveTask = ReceiveMessagesAsync(_cts.Token);

            // Start heartbeat loop
            // TODO: Make heartbeat configurable
            _ = StartHeartbeatLoopAsync(_cts.Token, TimeSpan.FromSeconds(15));
        }
        catch
        {
            _isConnectedFlag = false; // Reset flag on failure
            await CleanupConnectionAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await CleanupConnectionAsync();
    }

    private async Task CleanupConnectionAsync()
    {
        if (!_isConnectedFlag) return; // Already cleaned up

        lock (_connectionLock)
        {
            if (!_isConnectedFlag) return;
            _isConnectedFlag = false;
        }

        _cts?.Cancel(); // Signal cancellation to loops

        // Wait for the receive loop to finish
        if (_receiveTask != null && !_receiveTask.IsCompleted)
        {
            try
            {
                // Give it a short time to complete gracefully
                await Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromMilliseconds(500)));
            }
            catch (OperationCanceledException)
            {
                /* Expected */
            }
            catch (Exception ex)
            {
                // Log error from receive task if needed
                Console.WriteLine($"Error during receive task shutdown: {ex.Message}");
            }
        }

        // Fail pending requests
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetException(new TaskCanceledException("Connection closed."));
        }

        _pendingRequests.Clear();

        // Complete pipes
        try { await (_reader?.CompleteAsync() ?? Task.CompletedTask); }
        catch
        {
            /* Ignore */
        }

        try { await (_writer?.CompleteAsync() ?? Task.CompletedTask); }
        catch
        {
            /* Ignore */
        }

        // Close client and dispose stream
        _client?.Close(); // Closes the stream implicitly
        _stream = null;
        _client = null;
        _reader = null;
        _writer = null;

        _cts?.Dispose();
        _cts = null;
    }

    /// <inheritdoc />
    public async Task<PulseResponse> SendRequestAsync(PulseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _writer == null)
            throw new InvalidOperationException("Client is not connected.");

        var tcs = new TaskCompletionSource<PulseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(request.RequestId, tcs))
        {
            // Should not happen with Guid, but handle just in case
            throw new InvalidOperationException("Duplicate request ID detected.");
        }

        // Link the provided token with the connection's token
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts?.Token ?? CancellationToken.None);
        // TODO: Make request timeout configurable
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            var requestBytes = MemoryPackSerializer.Serialize(request);
            var envelope = new MessageEnvelope { Type = MessageType.Request, Payload = requestBytes };
            await SendEnvelopeAsync(envelope, linkedCts.Token);

            // 等待响应（带超时和取消）
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("Request cancelled by caller.", cancellationToken);
            else if (_cts?.IsCancellationRequested ?? true)
                throw new InvalidOperationException("Connection closed while waiting for response.");
            else
                throw new TimeoutException("Request timed out.");
        }
        catch (Exception ex)
        {
            // Ensure TCS is completed exceptionally if sending failed
            tcs.TrySetException(ex);
            throw; // Re-throw the original exception
        }
        finally
        {
            _pendingRequests.TryRemove(request.RequestId, out _);
        }
    }

    /// <summary>
    /// Loop that continuously reads messages from the server.
    /// </summary>
    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                ReadResult result = await _reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                try
                {
                    while (TryParseMessage(ref buffer, out var messageData))
                    {
                        // Process messages asynchronously
                        _ = ProcessMessageAsync(messageData); // Fire-and-forget processing
                    }

                    if (result.IsCanceled || result.IsCompleted)
                    {
                        break; // Exit loop if connection is closed or cancelled
                    }
                }
                finally
                {
                    // Advance the reader
                    _reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Receive loop cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in receive loop: {ex.Message}");
            // Connection likely lost, trigger cleanup
            await CleanupConnectionAsync();
        }
        finally
        {
            Console.WriteLine("Receive loop finished.");
            // Ensure connection cleanup happens if loop exits unexpectedly
            await CleanupConnectionAsync();
        }
    }

    /// <summary>
    /// Tries to parse a length-prefixed message from the buffer.
    /// </summary>
    private bool TryParseMessage(ref ReadOnlySequence<byte> buffer, out byte[] message)
    {
        message = default!;
        if (buffer.Length < 4)
            return false;

        Span<byte> lengthBytes = stackalloc byte[4];
        buffer.Slice(0, 4).CopyTo(lengthBytes);
        int messageLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (messageLength <= 0 || messageLength > 1024 * 1024 * 16) // Basic sanity check (e.g., max 16MB)
        {
            Console.WriteLine($"Invalid message length received: {messageLength}");
            // Consider closing the connection due to protocol error
            _ = CleanupConnectionAsync();
            // Consume the invalid length prefix to avoid infinite loop
            buffer = buffer.Slice(4);
            return false;
        }

        if (buffer.Length < 4 + messageLength)
            return false;

        var messageBuffer = buffer.Slice(4, messageLength);
        message = messageBuffer.ToArray();
        buffer = buffer.Slice(messageBuffer.End);
        return true;
    }

    /// <summary>
    /// Processes a received message payload based on its envelope type.
    /// </summary>
    private async Task ProcessMessageAsync(byte[] messageData)
    {
        try
        {
            var envelope = MemoryPackSerializer.Deserialize<MessageEnvelope>(messageData);

            switch (envelope.Type)
            {
                case MessageType.Response:
                    HandleResponse(envelope.Payload);
                    break;
                case MessageType.Event:
                    await HandleEventAsync(envelope.Payload);
                    break;
                case MessageType.Heartbeat:
                    HandleHeartbeat(envelope.Payload);
                    break;
                default:
                    Console.WriteLine($"Received unknown message type: {envelope.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing received message: {ex.Message}");
        }
    }

    /// <summary>
    /// Matches a received response with a pending request.
    /// </summary>
    private void HandleResponse(byte[] payload)
    {
        try
        {
            var response = MemoryPackSerializer.Deserialize<PulseResponse>(payload);
            if (_pendingRequests.TryGetValue(response.RequestId, out var tcs))
            {
                if (response.IsSuccess)
                {
                    tcs.TrySetResult(response);
                }
                else
                {
                    // TODO: Create specific exception type?
                    tcs.TrySetException(new RpcException(response.ErrorMessage ?? "RPC call failed."));
                }
            }
            else
            {
                Console.WriteLine($"Received response for unknown request ID: {response.RequestId}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling response: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles a received event by invoking the OnEventReceived delegate.
    /// </summary>
    private async Task HandleEventAsync(byte[] payload)
    {
        try
        {
            var eventPacket = MemoryPackSerializer.Deserialize<PulseEvent>(payload);
            var handler = OnEventReceived;
            if (handler != null)
            {
                try
                {
                    await handler.Invoke(eventPacket);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in OnEventReceived handler: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling event: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles a received heartbeat (currently does nothing).
    /// </summary>
    private void HandleHeartbeat(byte[] payload)
    {
        // Potential RTT calculation or connection health update
        try
        {
            var heartbeat = MemoryPackSerializer.Deserialize<PulseHeartbeat>(payload);
            // Console.WriteLine($"Received heartbeat response with server timestamp: {heartbeat.Timestamp}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling heartbeat: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a heartbeat message periodically.
    /// </summary>
    private async Task StartHeartbeatLoopAsync(CancellationToken cancellationToken, TimeSpan interval)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);

                if (!IsConnected || _writer == null)
                    continue;

                try
                {
                    var heartbeat = new PulseHeartbeat { Timestamp = DateTime.UtcNow.Ticks };
                    var heartbeatBytes = MemoryPackSerializer.Serialize(heartbeat);
                    var envelope = new MessageEnvelope { Type = MessageType.Heartbeat, Payload = heartbeatBytes };
                    await SendEnvelopeAsync(envelope, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break; // Exit loop if cancelled during send
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send heartbeat: {ex.Message}");
                    // Consider disconnecting if heartbeats fail repeatedly
                    await CleanupConnectionAsync();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Heartbeat loop cancelled.");
        }
        finally
        {
            Console.WriteLine("Heartbeat loop finished.");
        }
    }

    /// <summary>
    /// Serializes and sends a message envelope with length prefix.
    /// </summary>
    private async Task SendEnvelopeAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!IsConnected || _writer == null)
        {
            throw new InvalidOperationException("Cannot send, client is not connected.");
        }

        try
        {
            byte[] envelopeBytes = MemoryPackSerializer.Serialize(envelope);
            int messageLength = envelopeBytes.Length;

            // Get memory, write length, write payload
            var buffer = _writer.GetMemory(4 + messageLength);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Span.Slice(0, 4), messageLength);
            envelopeBytes.CopyTo(buffer.Slice(4));
            _writer.Advance(4 + messageLength);

            // Flush the writer
            FlushResult result = await _writer.FlushAsync(cancellationToken);

            if (result.IsCanceled || result.IsCompleted)
            {
                // Connection might be closing
                throw new OperationCanceledException("Send operation cancelled or completed unexpectedly.");
            }
        }
        catch (ObjectDisposedException ex) // Catch if PipeWriter is disposed
        {
            throw new InvalidOperationException("Cannot send, connection is disposed.", ex);
        }
        catch (IOException ex)
        {
            // Network error during send
            await CleanupConnectionAsync(); // Assume connection is lost
            throw new RpcException("Network error during send.", ex);
        }
        catch (Exception ex)
        {
            // Catch any other unexpected error during send
            await CleanupConnectionAsync(); // Assume connection is lost
            throw new RpcException("Failed to send message.", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CleanupConnectionAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Exception thrown for RPC specific errors.
/// </summary>
public class RpcException : Exception
{
    public RpcException(string message) : base(message) { }
    public RpcException(string message, Exception innerException) : base(message, innerException) { }
}
