using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PulseRPC.Client.Events;

/// <summary>
/// 批量事件项
/// </summary>
public readonly struct BatchEventItem
{
    public string EventName { get; }
    public object EventData { get; }
    public Func<object, Task> Handler { get; }

    public BatchEventItem(string eventName, object eventData, Func<object, Task> handler)
    {
        EventName = eventName;
        EventData = eventData;
        Handler = handler;
    }
}

/// <summary>
/// 客户端批量事件处理器 - 优化高频事件处理性能
/// </summary>
public sealed class BatchProcessor : IDisposable
{
    private readonly EventMetrics _metrics;
    private readonly Channel<BatchEventItem> _eventQueue;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private volatile bool _disposed;

    public BatchProcessor(EventMetrics metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _eventQueue = Channel.CreateBounded<BatchEventItem>(options);
        _processingTask = ProcessBatchEventsAsync(_cancellationTokenSource.Token);
    }

    public async ValueTask QueueEventAsync<T>(string eventName, T eventData, Func<T, Task> handler)
    {
        if (_disposed) return;

        var item = new BatchEventItem(eventName, eventData!, async data =>
        {
            if (data is T typedData)
            {
                await handler(typedData).ConfigureAwait(false);
            }
        });

        try
        {
            await _eventQueue.Writer.WriteAsync(item, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 已取消，忽略
        }
    }

    private async Task ProcessBatchEventsAsync(CancellationToken cancellationToken)
    {
        var batch = new List<BatchEventItem>(50);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 收集事件到批次中
                while (batch.Count < 50 && _eventQueue.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count > 0)
                {
                    await ProcessBatch(batch).ConfigureAwait(false);
                    batch.Clear();
                }
                else
                {
                    // 等待新事件
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _metrics.RecordBatchProcessingError(ex);
            }
        }
    }

    private async Task ProcessBatch(List<BatchEventItem> batch)
    {
        var stopwatch = Stopwatch.StartNew();

        var tasks = batch.Select(async item =>
        {
            try
            {
                await item.Handler(item.EventData).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _metrics.RecordEventError(item.EventName, ex, TimeSpan.Zero);
                return false;
            }
        }).ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        var successCount = results.Count(r => r);

        _metrics.RecordBatchProcessed(batch.Count, successCount, stopwatch.Elapsed);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cancellationTokenSource.Cancel();
        _eventQueue.Writer.Complete();

        try
        {
            _processingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // 忽略超时异常
        }

        _cancellationTokenSource.Dispose();
    }
}
