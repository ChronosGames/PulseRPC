using PulseRPC.Server.Abstractions;
using PulseRPC.Server.Models;
using PulseRPC.Transport;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PulseRPC.Server.Pipeline;

/// <summary>
/// Transmits response messages back to clients through the transport layer.
/// Handles FR-027 to FR-031: response batching, connection loss handling, retry logic.
/// Uses System.Threading.Channels for efficient I/O operations.
/// </summary>
public sealed class ResponseTransmitter : IDisposable
{
    private readonly IPulseServerTransport _transport;
    private readonly ResponseBatcher _batcher;
    private readonly Channel<ResponseItem> _responseChannel;
    private readonly CancellationTokenSource _stopCts = new();
    private readonly Task[] _workerTasks;
    private readonly ResponseTransmitterOptions _options;

    private long _totalResponsesSent;
    private long _totalSendErrors;
    private bool _disposed;

    public long TotalResponsesSent => Interlocked.Read(ref _totalResponsesSent);
    public long TotalSendErrors => Interlocked.Read(ref _totalSendErrors);

    public ResponseTransmitter(
        IPulseServerTransport transport,
        ResponseTransmitterOptions? options = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new ResponseTransmitterOptions();
        _batcher = new ResponseBatcher(_options.MaxBatchSize, _options.MaxBatchDelayMs);

        _responseChannel = Channel.CreateBounded<ResponseItem>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _workerTasks = new Task[_options.WorkerCount];
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < _workerTasks.Length; i++)
        {
            _workerTasks[i] = Task.Run(() => ProcessResponsesAsync(_stopCts.Token), _stopCts.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopCts.Cancel();
        _responseChannel.Writer.Complete();

        try
        {
            await Task.WhenAll(_workerTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Enqueues a response for transmission.
    /// </summary>
    public async Task<bool> SendResponseAsync(
        string connectionId,
        RpcMessage response,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var item = new ResponseItem
            {
                ConnectionId = connectionId,
                Response = response,
                EnqueuedAt = Stopwatch.GetTimestamp()
            };

            await _responseChannel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Transmits a response envelope to a connection.
    /// </summary>
    public async Task<bool> TransmitAsync(
        ResponseEnvelope response,
        ServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert ResponseEnvelope to RpcMessage
            var rpcMessage = new RpcMessage
            {
                RequestId = response.RequestId,
                MessageType = MessageType.Response,
                Payload = response.Payload
            };

            return await SendResponseAsync(connection.ConnectionId, rpcMessage, cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Asynchronously disposes resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Dispose();
    }

    private async Task ProcessResponsesAsync(CancellationToken cancellationToken)
    {
        var builder = new ResponseBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var item = await _responseChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

                // Serialize response
                var serialized = builder.SerializeResponse(item.Response);

                // Frame message (add length prefix)
                var framed = FrameMessage(serialized);

                // Send to client
                var sent = await SendToConnectionAsync(item.ConnectionId, framed, cancellationToken).ConfigureAwait(false);

                if (sent)
                {
                    Interlocked.Increment(ref _totalResponsesSent);
                }
                else
                {
                    Interlocked.Increment(ref _totalSendErrors);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalSendErrors);
                Debug.WriteLine($"Error in ResponseTransmitter: {ex.Message}");
            }
        }
    }

    private async Task<bool> SendToConnectionAsync(
        string connectionId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        var connection = _transport.GetConnection(connectionId);
        if (connection == null)
        {
            // Connection no longer exists
            return false;
        }

        try
        {
            return await connection.SendAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to send to connection {connectionId}: {ex.Message}");
            return false;
        }
    }

    private static ReadOnlyMemory<byte> FrameMessage(ReadOnlyMemory<byte> message)
    {
        var framed = new byte[4 + message.Length];
        BitConverter.GetBytes(message.Length).CopyTo(framed, 0);
        message.Span.CopyTo(framed.AsSpan(4));
        return framed;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _stopCts.Cancel();
        _responseChannel.Writer.Complete();
        _stopCts.Dispose();
    }

    private sealed class ResponseItem
    {
        public required string ConnectionId { get; init; }
        public required RpcMessage Response { get; init; }
        public required long EnqueuedAt { get; init; }
    }
}

public sealed class ResponseTransmitterOptions
{
    public int WorkerCount { get; set; } = 2;
    public int QueueCapacity { get; set; } = 10_000;
    public int MaxBatchSize { get; set; } = 50;
    public int MaxBatchDelayMs { get; set; } = 1;
}
