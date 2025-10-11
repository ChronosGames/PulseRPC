using FluentAssertions;
using PulseRPC.Server.Core;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for BackpressurePolicy (T059).
/// Tests three-level backpressure strategy and hysteresis recovery.
/// </summary>
public class BackpressurePolicyTests
{
    [Fact]
    public void BackpressurePolicy_ShouldInitialize_WithNoneLevel()
    {
        // Arrange & Act
        var policy = new BackpressurePolicy();

        // Assert
        policy.CurrentLevel.Should().Be(BackpressureLevel.None);
        policy.CurrentQueueDepth.Should().Be(0);
    }

    [Fact]
    public void UpdateQueueDepth_ShouldStayAtNone_WhenBelowThrottleThreshold()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Act: Queue at 60% (below 70% threshold)
        var level = policy.UpdateQueueDepth(600, maxCapacity);

        // Assert
        level.Should().Be(BackpressureLevel.None);
        policy.CurrentQueueDepth.Should().Be(600);
    }

    [Fact]
    public void UpdateQueueDepth_ShouldTransitionToThrottle_WhenExceedingThreshold()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Act: Queue at 75% (exceeds 70% threshold)
        var level = policy.UpdateQueueDepth(750, maxCapacity);

        // Assert
        level.Should().Be(BackpressureLevel.Throttle);
    }

    [Fact]
    public void UpdateQueueDepth_ShouldTransitionToReject_WhenExceedingRejectThreshold()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Transition to Throttle first
        policy.UpdateQueueDepth(750, maxCapacity);

        // Act: Queue at 95% (exceeds 90% threshold)
        var level = policy.UpdateQueueDepth(950, maxCapacity);

        // Assert
        level.Should().Be(BackpressureLevel.Reject);
    }

    [Fact]
    public void UpdateQueueDepth_ShouldApplyHysteresis_WhenRecovering()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Get to Throttle level (75%)
        policy.UpdateQueueDepth(750, maxCapacity);
        policy.CurrentLevel.Should().Be(BackpressureLevel.Throttle);

        // Act: Drop to 65% (below 70% but above hysteresis boundary of 60%)
        var level = policy.UpdateQueueDepth(650, maxCapacity);

        // Assert: Should still be Throttle (hysteresis prevents immediate drop)
        level.Should().Be(BackpressureLevel.Throttle);

        // Act: Drop to 55% (below hysteresis boundary)
        level = policy.UpdateQueueDepth(550, maxCapacity);

        // Assert: Now should transition to None
        level.Should().Be(BackpressureLevel.None);
    }

    [Fact]
    public void ShouldAcceptRequest_ShouldAcceptAll_WhenLevelIsNone()
    {
        // Arrange
        var policy = new BackpressurePolicy();

        // Act
        var decision = policy.ShouldAcceptRequest();

        // Assert
        decision.Accept.Should().BeTrue();
        decision.Level.Should().Be(BackpressureLevel.None);
        decision.Reason.Should().BeNull();
    }

    [Fact]
    public void ShouldAcceptRequest_ShouldThrottle_WhenLevelIsThrottle()
    {
        // Arrange
        var options = new BackpressurePolicyOptions
        {
            ThrottleRate = 1.0 // 100% throttle rate for deterministic testing
        };
        var policy = new BackpressurePolicy(options);

        // Transition to Throttle
        policy.UpdateQueueDepth(750, 1000);

        // Act
        var decision = policy.ShouldAcceptRequest();

        // Assert
        decision.Accept.Should().BeFalse();
        decision.Level.Should().Be(BackpressureLevel.Throttle);
        decision.Reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ShouldAcceptRequest_ShouldRejectAll_WhenLevelIsReject()
    {
        // Arrange
        var policy = new BackpressurePolicy();

        // Transition to Reject
        policy.UpdateQueueDepth(750, 1000); // Throttle
        policy.UpdateQueueDepth(950, 1000); // Reject

        // Act
        var decision = policy.ShouldAcceptRequest();

        // Assert
        decision.Accept.Should().BeFalse();
        decision.Level.Should().Be(BackpressureLevel.Reject);
        decision.Reason.Should().Contain("overloaded");
    }

    [Fact]
    public void UpdateQueueDepth_ShouldRecoverFromRejectToThrottle_WithHysteresis()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Get to Reject level
        policy.UpdateQueueDepth(950, maxCapacity);
        policy.CurrentLevel.Should().Be(BackpressureLevel.Reject);

        // Act: Drop to 85% (below 90% but above hysteresis boundary of 80%)
        var level = policy.UpdateQueueDepth(850, maxCapacity);

        // Assert: Should still be Reject
        level.Should().Be(BackpressureLevel.Reject);

        // Act: Drop to 75% (below hysteresis boundary)
        level = policy.UpdateQueueDepth(750, maxCapacity);

        // Assert: Now should transition to Throttle
        level.Should().Be(BackpressureLevel.Throttle);
    }

    [Fact]
    public void GetStatistics_ShouldReturnCurrentStatus()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        policy.UpdateQueueDepth(750, 1000);

        // Act
        var stats = policy.GetStatistics();

        // Assert
        stats.CurrentLevel.Should().Be(BackpressureLevel.Throttle);
        stats.CurrentQueueDepth.Should().Be(750);
    }

    [Fact]
    public void BackpressurePolicy_ShouldHandleCustomOptions()
    {
        // Arrange
        var options = new BackpressurePolicyOptions
        {
            ThrottleThreshold = 0.5,  // 50%
            RejectThreshold = 0.8,    // 80%
            Hysteresis = 0.15,        // 15%
            ThrottleRate = 0.25       // 25% rejection rate
        };

        var policy = new BackpressurePolicy(options);
        var maxCapacity = 1000L;

        // Act & Assert: Test custom thresholds
        policy.UpdateQueueDepth(400, maxCapacity).Should().Be(BackpressureLevel.None);
        policy.UpdateQueueDepth(600, maxCapacity).Should().Be(BackpressureLevel.Throttle);
        policy.UpdateQueueDepth(850, maxCapacity).Should().Be(BackpressureLevel.Reject);
    }

    [Fact]
    public void ShouldAcceptRequest_ShouldAcceptSome_WhenThrottleRateIsPartial()
    {
        // Arrange
        var options = new BackpressurePolicyOptions
        {
            ThrottleRate = 0.5 // 50% throttle rate
        };
        var policy = new BackpressurePolicy(options);

        // Transition to Throttle
        policy.UpdateQueueDepth(750, 1000);

        // Act: Make 100 requests
        var acceptedCount = 0;
        var rejectedCount = 0;

        for (int i = 0; i < 100; i++)
        {
            var decision = policy.ShouldAcceptRequest();
            if (decision.Accept)
                acceptedCount++;
            else
                rejectedCount++;
        }

        // Assert: Should accept approximately 50% (allow some variance due to randomness)
        acceptedCount.Should().BeInRange(30, 70, "50% throttle rate with randomness variance");
        rejectedCount.Should().BeInRange(30, 70, "50% throttle rate with randomness variance");
    }

    [Fact]
    public void BackpressurePolicy_ShouldNotOscillate_BetweenLevels()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Get to Throttle
        policy.UpdateQueueDepth(750, maxCapacity);
        policy.CurrentLevel.Should().Be(BackpressureLevel.Throttle);

        // Act: Fluctuate around threshold without crossing hysteresis
        for (int i = 0; i < 10; i++)
        {
            policy.UpdateQueueDepth(650, maxCapacity); // 65%
            policy.CurrentLevel.Should().Be(BackpressureLevel.Throttle, "hysteresis should prevent oscillation");
        }

        // Assert: Should remain in Throttle due to hysteresis
        policy.CurrentLevel.Should().Be(BackpressureLevel.Throttle);
    }

    [Fact]
    public void UpdateQueueDepth_ShouldHandleZeroCapacity_Gracefully()
    {
        // Arrange
        var policy = new BackpressurePolicy();

        // Act: Edge case - zero capacity
        var level = policy.UpdateQueueDepth(0, 1);

        // Assert: Should be at None (0% utilization)
        level.Should().Be(BackpressureLevel.None);
    }

    [Fact]
    public void UpdateQueueDepth_ShouldHandleFullCapacity()
    {
        // Arrange
        var policy = new BackpressurePolicy();
        var maxCapacity = 1000L;

        // Act: 100% utilization
        var level = policy.UpdateQueueDepth(1000, maxCapacity);

        // Assert: Should be at Reject (100% > 90%)
        level.Should().Be(BackpressureLevel.Reject);
    }
}
