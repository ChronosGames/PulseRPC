using System.IO.Pipelines;
using PulseRPC.Protocol.Network;
using PulseRPC.Protocol.Messages;
using PulseRPC.Protocol.Compression;
using Xunit;

namespace PulseRPC.Tests.Protocol.Network;

public class MessageBatcherTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldAddMessageToBatch()
    {
        // Arrange
        var pipe = new Pipe();
        var options = new MessageBatcherOptions
        {
            BatchDelay = 100,
            BatchSizeThreshold = 1024
        };
        using var batcher = new MessageBatcher(pipe.Writer, options);

        var message = new MessageBatch
        {
            Data = new byte[100],
            MessageId = 1
        };

        // Act
        await batcher.EnqueueAsync(message);

        // Assert
        var metrics = batcher.Metrics;
        var counters = metrics.GetCounters();
        Assert.Equal(1, counters.EnqueuedMessages);
    }

    [Fact]
    public async Task SendBatchAsync_ShouldCompressLargeMessages()
    {
        // Arrange
        var pipe = new Pipe();
        var options = new MessageBatcherOptions
        {
            BatchDelay = 100,
            BatchSizeThreshold = 1024 * 64
        };
        var compressorOptions = new MessageCompressorOptions
        {
            CompressionThreshold = 1024
        };
        using var batcher = new MessageBatcher(pipe.Writer, options, compressorOptions);

        // 创建一个大消息（重复数据，便于压缩）
        var data = new byte[2048];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 256);
        }

        var message = new MessageBatch
        {
            Data = data,
            MessageId = 1
        };

        // Act
        await batcher.EnqueueAsync(message);

        // 等待批处理
        await Task.Delay(150);

        // Assert
        var result = await pipe.Reader.ReadAsync();
        Assert.True(result.Buffer.Length > 0);

        // 检查是否包含压缩标志
        var flags = BitConverter.ToInt32(result.Buffer.Slice(8, 4).ToArray());
        Assert.True((flags & (int)MessageFlags.Compressed) != 0);
    }

    [Fact]
    public async Task SendBatchAsync_ShouldBatchMultipleMessages()
    {
        // Arrange
        var pipe = new Pipe();
        var options = new MessageBatcherOptions
        {
            BatchDelay = 50,
            BatchSizeThreshold = 1024
        };
        using var batcher = new MessageBatcher(pipe.Writer, options);

        var messages = new List<MessageBatch>();
        for (var i = 0; i < 5; i++)
        {
            messages.Add(new MessageBatch
            {
                Data = new byte[100],
                MessageId = i + 1
            });
        }

        // Act
        foreach (var message in messages)
        {
            await batcher.EnqueueAsync(message);
        }

        // 等待批处理
        await Task.Delay(100);

        // Assert
        var metrics = batcher.Metrics;
        var counters = metrics.GetCounters();
        Assert.Equal(5, counters.EnqueuedMessages);
        Assert.True(counters.SentBatches > 0);

        var result = await pipe.Reader.ReadAsync();
        Assert.True(result.Buffer.Length > 0);

        // 检查是否包含批处理标志
        var flags = BitConverter.ToInt32(result.Buffer.Slice(8, 4).ToArray());
        Assert.True((flags & (int)MessageFlags.Batched) != 0);
    }

    [Fact]
    public async Task MessageBatcher_ShouldRespectPriority()
    {
        // Arrange
        var pipe = new Pipe();
        var options = new MessageBatcherOptions
        {
            BatchDelay = 50,
            BatchSizeThreshold = 1024
        };
        using var batcher = new MessageBatcher(pipe.Writer, options);

        // 创建不同优先级的消息
        var lowPriorityMessage = new MessageBatch
        {
            Data = new byte[100],
            MessageId = 1
        };

        var highPriorityMessage = new MessageBatch
        {
            Data = new byte[100],
            MessageId = 2
        };

        // Act
        await batcher.EnqueueAsync(lowPriorityMessage, MessagePriority.Low);
        await batcher.EnqueueAsync(highPriorityMessage, MessagePriority.High);

        // 等待批处理
        await Task.Delay(100);

        // Assert
        var result = await pipe.Reader.ReadAsync();
        var messageId = BitConverter.ToInt32(result.Buffer.Slice(12, 4).ToArray());
        Assert.Equal(2, messageId); // 高优先级消息应该先发送
    }

    [Fact]
    public async Task MessageBatcher_ShouldTrackPerformanceMetrics()
    {
        // Arrange
        var pipe = new Pipe();
        var options = new MessageBatcherOptions
        {
            BatchDelay = 50,
            BatchSizeThreshold = 1024
        };
        using var batcher = new MessageBatcher(pipe.Writer, options);

        // Act
        for (var i = 0; i < 10; i++)
        {
            await batcher.EnqueueAsync(new MessageBatch
            {
                Data = new byte[100],
                MessageId = i + 1
            });
        }

        // 等待批处理
        await Task.Delay(100);

        // Assert
        var metrics = batcher.Metrics;
        var counters = metrics.GetCounters();
        var rates = metrics.GetRates(TimeSpan.FromSeconds(1));
        var sizes = metrics.GetAverageSizes(TimeSpan.FromSeconds(1));

        Assert.Equal(10, counters.EnqueuedMessages);
        Assert.True(counters.SentBatches > 0);
        Assert.True(rates.MessagesPerSecond > 0);
        Assert.Equal(100, sizes.AverageMessageSize);
    }
}
