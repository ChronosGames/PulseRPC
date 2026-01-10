using System;
using System.Threading.Tasks;
using PulseRPC.Abstractions.Transport.Batching;
using Xunit;
using Xunit.Abstractions;

namespace PulseRPC.Tests.Transport
{
    /// <summary>
    /// TransportMetrics 单元测试
    /// </summary>
    public class TransportMetricsTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private TransportMetrics? _metrics;

        public TransportMetricsTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            _metrics?.Dispose();
        }

        [Fact]
        public void RecordBytesSent_ShouldAccumulateCorrectly()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act
            _metrics.RecordBytesSent(100);
            _metrics.RecordBytesSent(200);
            _metrics.RecordBytesSent(300);

            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(600, snapshot.BytesSent);
        }

        [Fact]
        public void RecordSendRequest_ShouldCountCorrectly()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act
            _metrics.RecordSendRequest();
            _metrics.RecordSendRequest();
            _metrics.RecordSendRequest();

            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(3, snapshot.SendRequests);
        }

        [Fact]
        public void RecordSendError_ShouldCountCorrectly()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act
            _metrics.RecordSendError();
            _metrics.RecordSendError();

            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(2, snapshot.SendErrors);
        }

        [Fact]
        public void RecordSendRejected_ShouldCountCorrectly()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act
            _metrics.RecordSendRejected();
            _metrics.RecordSendRejected();
            _metrics.RecordSendRejected();
            _metrics.RecordSendRejected();

            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(4, snapshot.SendRejected);
        }

        [Fact]
        public void RecordBatchFlushed_ShouldCountCorrectly()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act
            _metrics.RecordBatchFlushed(10);
            _metrics.RecordBatchFlushed(20);

            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(2, snapshot.BatchesFlushed);
        }

        [Fact]
        public void GetSnapshot_WithQueueDepthCallback_ShouldReturnCurrentDepth()
        {
            // Arrange
            int currentDepth = 42;
            _metrics = new TransportMetrics(
                "test",
                getPendingQueueDepth: () => currentDepth);

            // Act
            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(42, snapshot.PendingQueueDepth);
        }

        [Fact]
        public void GetSnapshot_WithBackpressureLevelCallback_ShouldReturnCurrentLevel()
        {
            // Arrange
            _metrics = new TransportMetrics(
                "test",
                getBackpressureLevel: () => (int)BackpressureLevel.Throttle);

            // Act
            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(BackpressureLevel.Throttle, snapshot.BackpressureLevel);
        }

        [Fact]
        public void GetSnapshot_ShouldReturnCorrectTransportId()
        {
            // Arrange
            _metrics = new TransportMetrics("my-transport-id");

            // Act
            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal("my-transport-id", snapshot.TransportId);
        }

        [Fact]
        public void Flush_ShouldTransferLocalCountersToTotals()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act
            _metrics.RecordBytesSent(100);
            _metrics.RecordSendRequest();
            _metrics.Flush();

            // Get snapshot should still show the flushed values
            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal(100, snapshot.BytesSent);
            Assert.Equal(1, snapshot.SendRequests);
        }

        [Fact]
        public void RecordSendLatency_ShouldNotThrow()
        {
            // Arrange
            _metrics = new TransportMetrics("test");

            // Act & Assert (should not throw)
            _metrics.RecordSendLatency(TimeSpan.FromMilliseconds(10));
            _metrics.RecordSendLatency(TimeSpan.FromMilliseconds(50));
            _metrics.RecordSendLatency(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void Constructor_WithNullTransportId_ShouldUseEmptyString()
        {
            // Arrange & Act
            _metrics = new TransportMetrics(null!);

            var snapshot = _metrics.GetSnapshot();

            // Assert
            Assert.Equal("", snapshot.TransportId);
        }

        [Fact]
        public void MeterName_ShouldBeCorrect()
        {
            // Assert
            Assert.Equal("PulseRPC.Transport", TransportMetrics.MeterName);
        }

        [Fact]
        public void Dispose_ShouldFlushBeforeDisposing()
        {
            // Arrange
            _metrics = new TransportMetrics("test");
            _metrics.RecordBytesSent(100);

            // Act
            var snapshot = _metrics.GetSnapshot();
            _metrics.Dispose();
            _metrics = null; // Prevent double dispose in Dispose()

            // Assert
            Assert.Equal(100, snapshot.BytesSent);
        }
    }
}
