using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Models;

namespace PulseRPC.Server.Response;

/// <summary>
/// Batched network writes using System.Threading.Channels.
/// Reduces syscall overhead by batching small responses.
/// </summary>
public sealed class ResponseTransmitter : IAsyncDisposable
{
    private readonly Channel<ResponseMessage> _channel;
    private readonly ResponseSerializer _serializer;
    private readonly ILogger<ResponseTransmitter> _logger;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly Task _processingTask;
    private readonly int _batchSize;
    private readonly TimeSpan _batchMaxWait;

    private record ResponseMessage(
        ResponseEnvelope Envelope,
        ServerConnection Connection,
        TaskCompletionSource<bool> CompletionSource
    );

    public ResponseTransmitter(
        int maxQueueDepth = 10000,
        int batchSize = 100,
        TimeSpan? batchMaxWait = null,
        ILogger<ResponseTransmitter>? logger = null)
    {
        _channel = Channel.CreateBounded<ResponseMessage>(new BoundedChannelOptions(maxQueueDepth)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

        _serializer = new ResponseSerializer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ResponseTransmitter>.Instance;
        _shutdownCts = new CancellationTokenSource();
        _batchSize = batchSize;
        _batchMaxWait = batchMaxWait ?? TimeSpan.FromMilliseconds(1);

        // Start background processing
        _processingTask = ProcessResponsesAsync(_shutdownCts.Token);
    }

    /// <summary>
    /// Queues a response for transmission.
    /// </summary>
    public async Task<bool> TransmitAsync(ResponseEnvelope response, ServerConnection connection, CancellationToken cancellationToken = default)
    {
        if (!connection.IsActive)
        {
            _logger.LogWarning("Cannot transmit response - connection {ConnectionId} is not active", connection.ConnectionId);
            return false;
        }

        var tcs = new TaskCompletionSource<bool>();
        var message = new ResponseMessage(response, connection, tcs);

        try
        {
            await _channel.Writer.WriteAsync(message, cancellationToken);
            return await tcs.Task;
        }
        catch (ChannelClosedException)
        {
            _logger.LogWarning("Cannot transmit response - channel is closed");
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Response transmission cancelled for RequestId {RequestId}", response.RequestId);
            return false;
        }
    }

    private async Task ProcessResponsesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ResponseTransmitter started");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = new List<ResponseMessage>(_batchSize);

                // Read first message (blocking wait)
                if (await _channel.Reader.WaitToReadAsync(cancellationToken))
                {
                    if (_channel.Reader.TryRead(out var firstMessage))
                    {
                        batch.Add(firstMessage);

                        // Try to read more messages for batching (non-blocking)
                        var deadline = DateTime.UtcNow.Add(_batchMaxWait);
                        while (batch.Count < _batchSize && DateTime.UtcNow < deadline)
                        {
                            if (_channel.Reader.TryRead(out var message))
                            {
                                batch.Add(message);
                            }
                            else
                            {
                                break;
                            }
                        }

                        // Process batch
                        await ProcessBatchAsync(batch, cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("ResponseTransmitter shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResponseTransmitter encountered fatal error");
        }

        _logger.LogInformation("ResponseTransmitter stopped");
    }

    private async Task ProcessBatchAsync(List<ResponseMessage> batch, CancellationToken cancellationToken)
    {
        foreach (var message in batch)
        {
            try
            {
                // Serialize response
                var bytes = _serializer.Serialize(message.Envelope);

                // TODO: Replace with actual network transmission when transport layer is integrated
                // For now, simulate successful transmission
                await SimulateNetworkWriteAsync(message.Connection, bytes, cancellationToken);

                // Update connection statistics
                message.Connection.RecordMessageSent(bytes.Length);

                // Signal success
                message.CompletionSource.TrySetResult(true);

                _logger.LogDebug(
                    "Transmitted response for RequestId {RequestId} to Connection {ConnectionId} ({Bytes} bytes)",
                    message.Envelope.RequestId,
                    message.Connection.ConnectionId,
                    bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to transmit response for RequestId {RequestId} to Connection {ConnectionId}",
                    message.Envelope.RequestId,
                    message.Connection.ConnectionId);

                message.Connection.RecordError();
                message.CompletionSource.TrySetResult(false);
            }
        }
    }

    private async Task SimulateNetworkWriteAsync(ServerConnection connection, byte[] data, CancellationToken cancellationToken)
    {
        // TODO: Integrate with actual transport layer (ServerTransportChannel)
        // For now, simulate I/O delay
        await Task.Delay(1, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("ResponseTransmitter disposing");

        // Signal shutdown
        _shutdownCts.Cancel();

        // Complete the channel
        _channel.Writer.Complete();

        // Wait for processing to complete
        try
        {
            await _processingTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("ResponseTransmitter processing task did not complete within timeout");
        }

        _shutdownCts.Dispose();
        _logger.LogInformation("ResponseTransmitter disposed");
    }
}
