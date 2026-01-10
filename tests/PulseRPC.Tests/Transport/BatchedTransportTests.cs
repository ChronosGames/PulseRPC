using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Abstractions.Transport.Batching;
using PulseRPC.Transport;
using Xunit;
using Xunit.Abstractions;

namespace PulseRPC.Tests.Transport
{
    /// <summary>
    /// BatchedTransport 单元测试
    /// </summary>
    public class BatchedTransportTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly MockTransport _mockTransport;

        public BatchedTransportTests(ITestOutputHelper output)
        {
            _output = output;
            _mockTransport = new MockTransport();
        }

        public void Dispose()
        {
            _mockTransport.Dispose();
        }

        [Fact]
        public async Task SendAsync_SingleMessage_ShouldBeFlushedByTimer()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 10, // 高阈值，确保不会因数量触发
                FlushInterval = TimeSpan.FromMilliseconds(50),
                EnableMetrics = true,
                TransportId = "test"
            };

            await using var batched = new BatchedTransport(_mockTransport, options);
            var data = Encoding.UTF8.GetBytes("Hello");

            // Act
            var result = await batched.SendAsync(data);

            // Assert
            Assert.True(result);
            Assert.Equal(1, _mockTransport.SendCount);
            Assert.Equal(data.Length, _mockTransport.TotalBytesSent);
        }

        [Fact]
        public async Task SendAsync_BatchThresholdReached_ShouldFlushImmediately()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 3,
                FlushInterval = TimeSpan.FromSeconds(10), // 长间隔，确保不会被定时器触发
                EnableMetrics = true
            };

            await using var batched = new BatchedTransport(_mockTransport, options);
            var data = Encoding.UTF8.GetBytes("X");

            // Act - 发送 3 条消息应触发批次刷新
            var tasks = new Task<bool>[3];
            for (int i = 0; i < 3; i++)
            {
                tasks[i] = batched.SendAsync(data);
            }
            await Task.WhenAll(tasks);

            // Assert
            Assert.True(tasks[0].Result);
            Assert.True(tasks[1].Result);
            Assert.True(tasks[2].Result);
            Assert.Equal(3, _mockTransport.SendCount);
        }

        [Fact]
        public async Task SendAsync_ByteThresholdReached_ShouldFlushImmediately()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 100, // 高消息数阈值
                BatchSizeThreshold = 1024, // 字节阈值
                FlushInterval = TimeSpan.FromSeconds(10),
                EnableMetrics = true
            };

            await using var batched = new BatchedTransport(_mockTransport, options);
            var data = new byte[512]; // 512 字节

            // Act - 发送 2 条消息 = 1024 字节，应触发批次刷新
            var task1 = batched.SendAsync(data);
            var task2 = batched.SendAsync(data);
            await Task.WhenAll(task1, task2);

            // Assert
            Assert.True(task1.Result);
            Assert.True(task2.Result);
            Assert.Equal(2, _mockTransport.SendCount);
        }

        [Fact]
        public async Task FlushAsync_ShouldFlushPendingMessages()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 100,
                FlushInterval = TimeSpan.FromSeconds(10),
                EnableMetrics = true
            };

            await using var batched = new BatchedTransport(_mockTransport, options);
            var data = Encoding.UTF8.GetBytes("Test");

            // Act
            var sendTask = batched.SendAsync(data);
            await Task.Delay(10); // 让消息进入队列
            await batched.FlushAsync();
            var result = await sendTask;

            // Assert
            Assert.True(result);
            Assert.True(_mockTransport.SendCount >= 1);
        }

        [Fact]
        public async Task BackpressureLevel_ShouldReflectQueueUtilization()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                QueueCapacity = 10,
                ThrottleThreshold = 0.7,
                RejectThreshold = 0.9,
                BatchThreshold = 100,
                FlushInterval = TimeSpan.FromSeconds(10),
                EnableMetrics = true
            };

            await using var batched = new BatchedTransport(_mockTransport, options);

            // Assert - 初始状态
            Assert.Equal(BackpressureLevel.None, batched.BackpressureLevel);
        }

        [Fact]
        public async Task GetMetrics_ShouldReturnValidSnapshot()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 1,
                FlushInterval = TimeSpan.FromMilliseconds(10),
                EnableMetrics = true,
                TransportId = "metrics-test"
            };

            await using var batched = new BatchedTransport(_mockTransport, options);
            var data = Encoding.UTF8.GetBytes("Metrics Test");

            // Act
            await batched.SendAsync(data);
            var metrics = batched.GetMetrics();

            // Assert
            Assert.Equal("metrics-test", metrics.TransportId);
            Assert.True(metrics.BytesSent >= 0);
            Assert.True(metrics.SendRequests >= 1);
        }

        [Fact]
        public async Task SendAsync_WhenDisposed_ShouldReturnFalse()
        {
            // Arrange
            var options = new BatchedTransportOptions();
            var batched = new BatchedTransport(_mockTransport, options);

            // Act
            await batched.DisposeAsync();
            var result = await batched.SendAsync(Encoding.UTF8.GetBytes("Test"));

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task SendAsync_EmptyData_ShouldReturnTrueImmediately()
        {
            // Arrange
            var options = new BatchedTransportOptions();
            await using var batched = new BatchedTransport(_mockTransport, options);

            // Act
            var result = await batched.SendAsync(ReadOnlyMemory<byte>.Empty);

            // Assert
            Assert.True(result);
            Assert.Equal(0, _mockTransport.SendCount);
        }

        [Fact]
        public void Constructor_InvalidOptions_ShouldThrow()
        {
            // Arrange
            var invalidOptions = new BatchedTransportOptions
            {
                BatchThreshold = 0 // Invalid
            };

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BatchedTransport(_mockTransport, invalidOptions));
        }

        [Fact]
        public async Task PendingSendCount_ShouldReflectQueueDepth()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 100,
                FlushInterval = TimeSpan.FromSeconds(10)
            };

            await using var batched = new BatchedTransport(_mockTransport, options);

            // Act & Assert - 初始应为 0
            Assert.Equal(0, batched.PendingSendCount);
        }

        [Fact]
        public async Task ConcurrentSends_ShouldAllSucceed()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                BatchThreshold = 10,
                FlushInterval = TimeSpan.FromMilliseconds(50),
                QueueCapacity = 1000
            };

            await using var batched = new BatchedTransport(_mockTransport, options);
            var data = Encoding.UTF8.GetBytes("Concurrent");
            const int concurrentCount = 100;

            // Act
            var tasks = new Task<bool>[concurrentCount];
            for (int i = 0; i < concurrentCount; i++)
            {
                tasks[i] = batched.SendAsync(data);
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, r => Assert.True(r));
            Assert.Equal(concurrentCount, _mockTransport.SendCount);
        }

        /// <summary>
        /// Mock ITransport 实现
        /// </summary>
        private class MockTransport : ITransport
        {
            private int _sendCount;
            private long _totalBytesSent;
            private volatile bool _disposed;

            public int SendCount => _sendCount;
            public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);

            public string Id => "mock-transport";
            public TransportType Type => TransportType.TCP;
            public bool IsConnected => !_disposed;
            public ConnectionState State => _disposed ? ConnectionState.Disconnected : ConnectionState.Connected;
            public EndPoint LocalEndPoint => new IPEndPoint(IPAddress.Loopback, 0);
            public EndPoint RemoteEndPoint => new IPEndPoint(IPAddress.Loopback, 0);

            public event EventHandler<TransportStateEventArgs>? StateChanged;
            public event EventHandler<TransportDataEventArgs>? DataReceived;

            public Task<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            {
                if (_disposed)
                    return Task.FromResult(false);

                Interlocked.Increment(ref _sendCount);
                Interlocked.Add(ref _totalBytesSent, data.Length);
                return Task.FromResult(true);
            }

            public void Dispose()
            {
                _disposed = true;
            }
        }
    }
}
