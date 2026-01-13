using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PulseRPC.Server.Processing.Pipeline;

/// <summary>
/// Batches small responses together to reduce system calls and improve throughput.
/// Handles batching delay <1ms with throughput improvement.
/// </summary>
public sealed class ResponseBatcher
{
    private readonly int _maxBatchSize;
    private readonly int _maxDelayMs;

    public ResponseBatcher(int maxBatchSize = 50, int maxDelayMs = 1)
    {
        _maxBatchSize = maxBatchSize > 0 ? maxBatchSize : 50;
        _maxDelayMs = maxDelayMs > 0 ? maxDelayMs : 1;
    }

    /// <summary>
    /// Batches multiple responses into a single frame.
    /// </summary>
    public ReadOnlyMemory<byte> BatchResponses(IReadOnlyList<ReadOnlyMemory<byte>> responses)
    {
        if (responses == null || responses.Count == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (responses.Count == 1)
        {
            return responses[0];
        }

        // Calculate total size
        var totalSize = 0;
        foreach (var response in responses)
        {
            totalSize += 4 + response.Length; // 4 bytes for length prefix
        }

        // Allocate buffer
        var buffer = new byte[totalSize];
        var offset = 0;

        // Copy all responses with length prefixes
        foreach (var response in responses)
        {
            BitConverter.GetBytes(response.Length).CopyTo(buffer, offset);
            offset += 4;

            response.Span.CopyTo(buffer.AsSpan(offset));
            offset += response.Length;
        }

        return buffer;
    }

    /// <summary>
    /// Attempts to collect multiple responses within the delay window.
    /// </summary>
    public async Task<IReadOnlyList<T>> CollectBatchAsync<T>(
        Func<CancellationToken, Task<T>> readFunc,
        CancellationToken cancellationToken)
    {
        var batch = new List<T>(_maxBatchSize);
        var startTime = Stopwatch.GetTimestamp();

        // Read first item
        try
        {
            var firstItem = await readFunc(cancellationToken).ConfigureAwait(false);
            batch.Add(firstItem);
        }
        catch
        {
            return batch;
        }

        // Collect additional items within time window
        while (batch.Count < _maxBatchSize)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
            if (elapsedMs >= _maxDelayMs)
            {
                break;
            }

            var remainingMs = _maxDelayMs - (int)elapsedMs;
            if (remainingMs <= 0)
            {
                break;
            }

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(remainingMs);

                var item = await readFunc(timeoutCts.Token).ConfigureAwait(false);
                batch.Add(item);
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation - return current batch
                break;
            }
        }

        return batch;
    }

    /// <summary>
    /// Checks if responses should be batched based on size.
    /// </summary>
    public bool ShouldBatch(int responseSize)
    {
        // Batch small responses (<1KB)
        return responseSize < 1024;
    }
}
