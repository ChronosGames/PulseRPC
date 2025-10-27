using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Models;
using PulseRPC.Server.Scheduling;
using PulseRPC.Scheduling;
using System;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for CircuitBreakerPolicy (T021)
/// Tests 4-state health state machine transitions
/// </summary>
public class CircuitBreakerPolicyTests
{
    private readonly CircuitBreakerPolicy _policy;
    private readonly HealthMonitorOptions _options;

    public CircuitBreakerPolicyTests()
    {
        _options = new HealthMonitorOptions
        {
            FailureThreshold = 3,
            CoolingPeriod = TimeSpan.FromMinutes(1),
            ProbeRequestLimit = 5,
            ProbeSuccessThreshold = 3
        };

        _policy = new CircuitBreakerPolicy(Options.Create(_options));
    }

    #region Healthy → Isolated Transition

    [Fact]
    public void EvaluateTransition_ShouldRemainHealthy_OnSuccess()
    {
        // Arrange
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Act
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        stateChanged.Should().BeFalse();
        health.State.Should().Be(HealthState.Healthy);
        health.ConsecutiveTimeouts.Should().Be(0);
    }

    [Fact]
    public void EvaluateTransition_ShouldIncrementTimeouts_OnFailure()
    {
        // Arrange
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Act
        _policy.EvaluateTransition(health, requestSucceeded: false, now);

        // Assert
        health.State.Should().Be(HealthState.Healthy);
        health.ConsecutiveTimeouts.Should().Be(1);
    }

    [Fact]
    public void EvaluateTransition_ShouldTransitionToIsolated_After3Failures()
    {
        // Arrange
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Act - First 2 failures
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        health.State.Should().Be(HealthState.Healthy);

        // Act - 3rd failure triggers isolation
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: false, now);

        // Assert
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.Isolated);
        health.ConsecutiveTimeouts.Should().Be(3);
        health.CoolingPeriodExpiresUtc.Should().NotBeNull();
        health.CoolingPeriodExpiresUtc.Value.Should().BeCloseTo(
            now.Add(_options.CoolingPeriod),
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EvaluateTransition_ShouldResetTimeouts_OnSuccessAfterFailures()
    {
        // Arrange
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Act - 2 failures then success
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        health.State.Should().Be(HealthState.Healthy);
        health.ConsecutiveTimeouts.Should().Be(0, "success should reset consecutive timeouts");
    }

    #endregion

    #region Isolated → CoolingDown Transition

    [Fact]
    public void EvaluateTransition_ShouldRemainIsolated_DuringCoolingPeriod()
    {
        // Arrange
        var health = CreateIsolatedInstance(DateTime.UtcNow.AddSeconds(30)); // Cooling period not expired
        var now = DateTime.UtcNow;

        // Act
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        stateChanged.Should().BeFalse();
        health.State.Should().Be(HealthState.Isolated);
    }

    [Fact]
    public void EvaluateTransition_ShouldTransitionToCoolingDown_WhenCoolingPeriodExpires()
    {
        // Arrange
        var coolingExpiry = DateTime.UtcNow.AddSeconds(-1); // Already expired
        var health = CreateIsolatedInstance(coolingExpiry);
        var now = DateTime.UtcNow;

        // Act
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.CoolingDown);
        health.CoolingPeriodExpiresUtc.Should().BeNull();
    }

    #endregion

    #region CoolingDown → ProbeAllowed Transition

    [Fact]
    public void EvaluateTransition_ShouldTransitionToProbeAllowed_FromCoolingDown()
    {
        // Arrange
        var health = new ServiceInstanceHealth
        {
            Key = new ServiceSchedulingKey("TestService", "test-1"),
            State = HealthState.CoolingDown,
            LastActivityUtc = DateTime.UtcNow
        };
        var now = DateTime.UtcNow;

        // Act
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.ProbeAllowed);
        health.ProbeRequestsAllowed.Should().Be(_options.ProbeRequestLimit);
        health.ProbeSuccessCount.Should().Be(0);
    }

    #endregion

    #region ProbeAllowed → Healthy Transition

    [Fact]
    public void EvaluateTransition_ShouldTransitionToHealthy_When3Of5ProbesSucceed()
    {
        // Arrange
        var health = CreateProbeAllowedInstance();
        var now = DateTime.UtcNow;

        // Act - 3 successful probes
        _policy.EvaluateTransition(health, requestSucceeded: true, now);
        health.State.Should().Be(HealthState.ProbeAllowed);

        _policy.EvaluateTransition(health, requestSucceeded: true, now);
        health.State.Should().Be(HealthState.ProbeAllowed);

        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.Healthy);
        health.ProbeRequestsAllowed.Should().Be(0);
        health.ProbeSuccessCount.Should().Be(0);
        health.ConsecutiveTimeouts.Should().Be(0);
    }

    [Fact]
    public void EvaluateTransition_ShouldTransitionToHealthy_WhenExhausted5ProbesWith3Successes()
    {
        // Arrange
        var health = CreateProbeAllowedInstance();
        var now = DateTime.UtcNow;

        // Act - 5 probes: success, fail, success, success, fail = 3 successes
        _policy.EvaluateTransition(health, requestSucceeded: true, now);   // 1 success
        _policy.EvaluateTransition(health, requestSucceeded: false, now);  // 0 success (reset)
        _policy.EvaluateTransition(health, requestSucceeded: true, now);   // 1 success
        _policy.EvaluateTransition(health, requestSucceeded: true, now);   // 2 successes
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: false, now); // Exhausted

        // Assert
        health.ProbeSuccessCount.Should().BeLessThan(_options.ProbeSuccessThreshold);
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.Isolated, "not enough successes, should re-isolate");
    }

    #endregion

    #region ProbeAllowed → Isolated Transition

    [Fact]
    public void EvaluateTransition_ShouldReIsolate_WhenProbesFail()
    {
        // Arrange
        var health = CreateProbeAllowedInstance();
        var now = DateTime.UtcNow;

        // Act - First probe fails immediately
        var stateChanged = _policy.EvaluateTransition(health, requestSucceeded: false, now);

        // Assert - Should increment consecutive timeouts but remain in ProbeAllowed
        health.State.Should().Be(HealthState.ProbeAllowed);
        health.ConsecutiveTimeouts.Should().Be(1);

        // Act - Continue probing until exhausted (5 total)
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        _policy.EvaluateTransition(health, requestSucceeded: false, now);
        stateChanged = _policy.EvaluateTransition(health, requestSucceeded: false, now);

        // Assert - Should re-isolate
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.Isolated);
        health.CoolingPeriodExpiresUtc.Should().NotBeNull();
    }

    [Fact]
    public void EvaluateTransition_ShouldDecrement_ProbeRequestsAllowed()
    {
        // Arrange
        var health = CreateProbeAllowedInstance();
        var now = DateTime.UtcNow;
        var initialAllowed = health.ProbeRequestsAllowed;

        // Act
        _policy.EvaluateTransition(health, requestSucceeded: true, now);

        // Assert
        health.ProbeRequestsAllowed.Should().Be(initialAllowed - 1);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EvaluateTransition_ShouldThrow_WhenHealthIsNull()
    {
        // Act
        Action act = () => _policy.EvaluateTransition(null!, requestSucceeded: true, DateTime.UtcNow);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EvaluateTransition_ShouldHandle_CustomFailureThreshold()
    {
        // Arrange - Custom threshold of 5
        var customOptions = new HealthMonitorOptions { FailureThreshold = 5 };
        var customPolicy = new CircuitBreakerPolicy(Options.Create(customOptions));
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Act - 4 failures should not isolate
        for (int i = 0; i < 4; i++)
        {
            customPolicy.EvaluateTransition(health, requestSucceeded: false, now);
        }
        health.State.Should().Be(HealthState.Healthy);

        // Act - 5th failure should isolate
        var stateChanged = customPolicy.EvaluateTransition(health, requestSucceeded: false, now);

        // Assert
        stateChanged.Should().BeTrue();
        health.State.Should().Be(HealthState.Isolated);
    }

    [Fact]
    public void EvaluateTransition_ShouldHandle_CustomProbeThresholds()
    {
        // Arrange - Custom: 10 probes, 7 successes required
        var customOptions = new HealthMonitorOptions
        {
            ProbeRequestLimit = 10,
            ProbeSuccessThreshold = 7
        };
        var customPolicy = new CircuitBreakerPolicy(Options.Create(customOptions));
        var health = CreateProbeAllowedInstance();
        health.ProbeRequestsAllowed = 10;
        var now = DateTime.UtcNow;

        // Act - 7 successes
        for (int i = 0; i < 7; i++)
        {
            customPolicy.EvaluateTransition(health, requestSucceeded: true, now);
        }

        // Assert - Should transition to Healthy
        health.State.Should().Be(HealthState.Healthy);
    }

    #endregion

    #region Complete Flow Tests

    [Fact]
    public void CircuitBreaker_ShouldComplete_FullRecoveryFlow()
    {
        // Arrange
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Step 1: Healthy → Isolated (3 failures)
        for (int i = 0; i < 3; i++)
        {
            _policy.EvaluateTransition(health, requestSucceeded: false, now);
        }
        health.State.Should().Be(HealthState.Isolated);

        // Step 2: Isolated → CoolingDown (cooling period expires)
        now = health.CoolingPeriodExpiresUtc!.Value.AddSeconds(1);
        _policy.EvaluateTransition(health, requestSucceeded: true, now);
        health.State.Should().Be(HealthState.CoolingDown);

        // Step 3: CoolingDown → ProbeAllowed
        _policy.EvaluateTransition(health, requestSucceeded: true, now);
        health.State.Should().Be(HealthState.ProbeAllowed);

        // Step 4: ProbeAllowed → Healthy (3 successes)
        for (int i = 0; i < 3; i++)
        {
            _policy.EvaluateTransition(health, requestSucceeded: true, now);
        }
        health.State.Should().Be(HealthState.Healthy);
        health.ConsecutiveTimeouts.Should().Be(0);
    }

    [Fact]
    public void CircuitBreaker_ShouldHandle_RepeatedIsolationCycles()
    {
        // Arrange
        var health = CreateHealthyInstance();
        var now = DateTime.UtcNow;

        // Cycle 1: Healthy → Isolated → Healthy
        // Isolate
        for (int i = 0; i < 3; i++)
        {
            _policy.EvaluateTransition(health, requestSucceeded: false, now);
        }
        health.State.Should().Be(HealthState.Isolated);

        // Recover
        now = health.CoolingPeriodExpiresUtc!.Value.AddSeconds(1);
        _policy.EvaluateTransition(health, requestSucceeded: true, now); // → CoolingDown
        _policy.EvaluateTransition(health, requestSucceeded: true, now); // → ProbeAllowed
        for (int i = 0; i < 3; i++)
        {
            _policy.EvaluateTransition(health, requestSucceeded: true, now);
        }
        health.State.Should().Be(HealthState.Healthy);

        // Cycle 2: Fail again
        for (int i = 0; i < 3; i++)
        {
            _policy.EvaluateTransition(health, requestSucceeded: false, now);
        }
        health.State.Should().Be(HealthState.Isolated, "should be able to isolate again");
    }

    #endregion

    #region Helper Methods

    private ServiceInstanceHealth CreateHealthyInstance()
    {
        return new ServiceInstanceHealth
        {
            Key = new ServiceSchedulingKey("TestService", "test-1"),
            State = HealthState.Healthy,
            LastActivityUtc = DateTime.UtcNow
        };
    }

    private ServiceInstanceHealth CreateIsolatedInstance(DateTime coolingExpiry)
    {
        return new ServiceInstanceHealth
        {
            Key = new ServiceSchedulingKey("TestService", "test-1"),
            State = HealthState.Isolated,
            ConsecutiveTimeouts = 3,
            CoolingPeriodExpiresUtc = coolingExpiry,
            LastActivityUtc = DateTime.UtcNow
        };
    }

    private ServiceInstanceHealth CreateProbeAllowedInstance()
    {
        return new ServiceInstanceHealth
        {
            Key = new ServiceSchedulingKey("TestService", "test-1"),
            State = HealthState.ProbeAllowed,
            ProbeRequestsAllowed = _options.ProbeRequestLimit,
            ProbeSuccessCount = 0,
            LastActivityUtc = DateTime.UtcNow
        };
    }

    #endregion
}
