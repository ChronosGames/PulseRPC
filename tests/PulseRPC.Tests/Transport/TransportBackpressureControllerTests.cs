using System;
using PulseRPC.Abstractions.Transport.Batching;
using Xunit;

namespace PulseRPC.Tests.Transport
{
    /// <summary>
    /// TransportBackpressureController 单元测试
    /// </summary>
    public class TransportBackpressureControllerTests
    {
        [Fact]
        public void Update_BelowThrottleThreshold_ShouldReturnNone()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act - 60% utilization
            var level = controller.Update(60);

            // Assert
            Assert.Equal(BackpressureLevel.None, level);
            Assert.Equal(BackpressureLevel.None, controller.CurrentLevel);
        }

        [Fact]
        public void Update_AtThrottleThreshold_ShouldReturnThrottle()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act - 75% utilization (above 70%)
            var level = controller.Update(75);

            // Assert
            Assert.Equal(BackpressureLevel.Throttle, level);
        }

        [Fact]
        public void Update_AboveRejectThreshold_ShouldReturnReject()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act - 95% utilization
            var level = controller.Update(95);

            // Assert
            Assert.Equal(BackpressureLevel.Reject, level);
        }

        [Fact]
        public void Update_Hysteresis_ShouldPreventOscillation()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9,
                hysteresis: 0.1);

            // Act - 升级到 Throttle
            controller.Update(75); // 75% > 70%, 升级到 Throttle

            // 尝试降级但仍在滞后范围内 (需要 < 60% 才能降级)
            var level = controller.Update(65); // 65% 仍在 60-70% 滞后区间

            // Assert - 应保持 Throttle
            Assert.Equal(BackpressureLevel.Throttle, level);
        }

        [Fact]
        public void Update_BelowHysteresisThreshold_ShouldDowngrade()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9,
                hysteresis: 0.1);

            // Act - 升级到 Throttle
            controller.Update(75);

            // 降到滞后阈值以下 (< 60%)
            var level = controller.Update(55);

            // Assert - 应降级到 None
            Assert.Equal(BackpressureLevel.None, level);
        }

        [Fact]
        public void ShouldReject_BlockStrategy_ShouldNeverReject()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act & Assert - 即使在 Reject 等级，Block 策略也不拒绝
            Assert.False(controller.ShouldReject(95, TransportBackpressureStrategy.Block));
        }

        [Fact]
        public void ShouldReject_RejectStrategy_AtRejectLevel_ShouldReject()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act
            var result = controller.ShouldReject(95, TransportBackpressureStrategy.Reject);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ShouldReject_DropNewestStrategy_AtRejectLevel_ShouldReject()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act
            var result = controller.ShouldReject(95, TransportBackpressureStrategy.DropNewest);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ShouldReject_DropOldestStrategy_AtRejectLevel_ShouldReject()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act
            var result = controller.ShouldReject(95, TransportBackpressureStrategy.DropOldest);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ShouldReject_NoneLevel_ShouldNeverReject()
        {
            // Arrange
            var controller = new TransportBackpressureController(
                capacity: 100,
                throttleThreshold: 0.7,
                rejectThreshold: 0.9);

            // Act & Assert - None 等级下所有策略都不应拒绝
            Assert.False(controller.ShouldReject(50, TransportBackpressureStrategy.Reject));
            Assert.False(controller.ShouldReject(50, TransportBackpressureStrategy.DropNewest));
            Assert.False(controller.ShouldReject(50, TransportBackpressureStrategy.DropOldest));
            Assert.False(controller.ShouldReject(50, TransportBackpressureStrategy.Block));
        }

        [Fact]
        public void Reset_ShouldSetLevelToNone()
        {
            // Arrange
            var controller = new TransportBackpressureController(capacity: 100);
            controller.Update(95); // 升到 Reject

            // Act
            controller.Reset();

            // Assert
            Assert.Equal(BackpressureLevel.None, controller.CurrentLevel);
        }

        [Fact]
        public void Capacity_ShouldReturnConfiguredValue()
        {
            // Arrange
            var controller = new TransportBackpressureController(capacity: 500);

            // Assert
            Assert.Equal(500, controller.Capacity);
        }

        [Fact]
        public void FromOptions_ShouldCreateControllerWithCorrectSettings()
        {
            // Arrange
            var options = new BatchedTransportOptions
            {
                QueueCapacity = 200,
                ThrottleThreshold = 0.6,
                RejectThreshold = 0.8,
                HysteresisThreshold = 0.05
            };

            // Act
            var controller = TransportBackpressureController.FromOptions(options);

            // Assert
            Assert.Equal(200, controller.Capacity);
            // 测试阈值是否正确应用
            Assert.Equal(BackpressureLevel.None, controller.Update(100)); // 50%
            Assert.Equal(BackpressureLevel.Throttle, controller.Update(140)); // 70%
            Assert.Equal(BackpressureLevel.Reject, controller.Update(180)); // 90%
        }

        [Fact]
        public void Constructor_InvalidCapacity_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new TransportBackpressureController(capacity: 0));
        }

        [Fact]
        public void CurrentLevel_ShouldBeThreadSafe()
        {
            // Arrange
            var controller = new TransportBackpressureController(capacity: 100);

            // Act - 并发读写
            var tasks = new System.Threading.Tasks.Task[100];
            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                {
                    controller.Update(index);
                    var _ = controller.CurrentLevel;
                });
            }

            // Assert - 不应抛出异常
            System.Threading.Tasks.Task.WaitAll(tasks);
        }
    }
}
