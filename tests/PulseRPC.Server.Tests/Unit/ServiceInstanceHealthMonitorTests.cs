using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PulseRPC.Scheduling;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Models;
using PulseRPC.Server.Scheduling;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for ServiceInstanceHealthMonitor (T022)
/// Tests health tracking, request recording, and statistics aggregation
/// </summary>
public class ServiceInstanceHealthMonitorTests
{
    private readonly ServiceInstanceHealthMonitor _monitor;
    private readonly HealthMonitorOptions _options;
    private readonly ILogger<ServiceInstanceHealthMonitor> _logger;

    public ServiceInstanceHealthMonitorTests()
    {
        _options = new HealthMonitorOptions
        {
            FailureThreshold = 3,
            CoolingPeriod = TimeSpan.FromMinutes(1),
            ProbeRequestLimit = 5,
            ProbeSuccessThreshold = 3
        };

        _logger = Substitute.For<ILogger<ServiceInstanceHealthMonitor>>();
        _monitor = new ServiceInstanceHealthMonitor(Options.Create(_options), _logger);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitialize_WithValidParameters()
    {
        // Act & Assert - Constructor should not throw
        var monitor = new ServiceInstanceHealthMonitor(
            Options.Create(_options),
            Substitute.For<ILogger<ServiceInstanceHealthMonitor>>());

        monitor.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        // Act
        Action act = () => new ServiceInstanceHealthMonitor(
            Options.Create(_options),
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region RecordRequestResult Tests

    [Fact]
    public void RecordRequestResult_ShouldCreateHealthRecord_OnFirstRequest()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Act
        _monitor.RecordRequestResult(key, success: true, timestamp);

        // Assert
        var health = _monitor.GetHealth(key);
        health.Should().NotBeNull();
        health!.Key.Should().Be(key);
        health.State.Should().Be(HealthState.Healthy);
        health.TotalRequests.Should().Be(1);
        health.SuccessfulRequests.Should().Be(1);
        health.LastActivityUtc.Should().Be(timestamp);
    }

    [Fact]
    public void RecordRequestResult_ShouldIncrementStatistics_OnSuccess()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Act - Record 5 successful requests
        for (int i = 0; i < 5; i++)
        {
            _monitor.RecordRequestResult(key, success: true, timestamp.AddSeconds(i));
        }

        // Assert
        var health = _monitor.GetHealth(key);
        health.Should().NotBeNull();
        health!.TotalRequests.Should().Be(5);
        health.SuccessfulRequests.Should().Be(5);
    }

    [Fact]
    public void RecordRequestResult_ShouldNotIncrementSuccessCount_OnFailure()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Act - 2 successes, 3 failures
        _monitor.RecordRequestResult(key, success: true, timestamp);
        _monitor.RecordRequestResult(key, success: true, timestamp);
        _monitor.RecordRequestResult(key, success: false, timestamp);
        _monitor.RecordRequestResult(key, success: false, timestamp);
        _monitor.RecordRequestResult(key, success: false, timestamp);

        // Assert
        var health = _monitor.GetHealth(key);
        health.Should().NotBeNull();
        health!.TotalRequests.Should().Be(5);
        health.SuccessfulRequests.Should().Be(2);
    }

    [Fact]
    public void RecordRequestResult_ShouldUpdateLastActivity_OnEveryRequest()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp1 = DateTime.UtcNow;
        var timestamp2 = timestamp1.AddSeconds(10);
        var timestamp3 = timestamp1.AddSeconds(20);

        // Act
        _monitor.RecordRequestResult(key, success: true, timestamp1);
        _monitor.RecordRequestResult(key, success: true, timestamp2);
        _monitor.RecordRequestResult(key, success: true, timestamp3);

        // Assert
        var health = _monitor.GetHealth(key);
        health!.LastActivityUtc.Should().Be(timestamp3);
    }

    [Fact]
    public void RecordRequestResult_ShouldTriggerStateTransition_After3Failures()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Act - 3 consecutive failures should trigger Healthy → Isolated
        _monitor.RecordRequestResult(key, success: false, timestamp);
        _monitor.RecordRequestResult(key, success: false, timestamp);
        _monitor.RecordRequestResult(key, success: false, timestamp);

        // Assert
        var health = _monitor.GetHealth(key);
        health!.State.Should().Be(HealthState.Isolated);
    }

    #endregion

    #region CanAcceptRequest Tests

    [Fact]
    public void CanAcceptRequest_ShouldReturnTrue_ForFirstRequest()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");

        // Act
        var canAccept = _monitor.CanAcceptRequest(key);

        // Assert
        canAccept.Should().BeTrue("first request should always be allowed");
    }

    [Fact]
    public void CanAcceptRequest_ShouldReturnTrue_WhenHealthy()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        _monitor.RecordRequestResult(key, success: true, DateTime.UtcNow);

        // Act
        var canAccept = _monitor.CanAcceptRequest(key);

        // Assert
        canAccept.Should().BeTrue();
    }

    [Fact]
    public void CanAcceptRequest_ShouldReturnFalse_WhenIsolated()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Isolate the instance (3 failures)
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(key, success: false, timestamp);
        }

        // Act
        var canAccept = _monitor.CanAcceptRequest(key);

        // Assert
        canAccept.Should().BeFalse("isolated instances should not accept requests");
    }

    [Fact]
    public void CanAcceptRequest_ShouldReturnFalse_DuringCoolingDown()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Isolate
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(key, success: false, timestamp);
        }

        // Transition to CoolingDown (cooling period expired)
        var health = _monitor.GetHealth(key);
        var expiredTime = health!.CoolingPeriodExpiresUtc!.Value.AddSeconds(1);
        _monitor.RecordRequestResult(key, success: true, expiredTime);

        // Act
        var canAccept = _monitor.CanAcceptRequest(key);

        // Assert
        health.State.Should().Be(HealthState.CoolingDown);
        canAccept.Should().BeFalse("cooling down instances should not accept requests");
    }

    [Fact]
    public void CanAcceptRequest_ShouldReturnTrue_WhenProbeAllowed()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Isolate → CoolingDown → ProbeAllowed
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(key, success: false, timestamp);
        }

        var health = _monitor.GetHealth(key);
        var expiredTime = health!.CoolingPeriodExpiresUtc!.Value.AddSeconds(1);
        _monitor.RecordRequestResult(key, success: true, expiredTime); // → CoolingDown
        _monitor.RecordRequestResult(key, success: true, expiredTime); // → ProbeAllowed

        // Act
        var canAccept = _monitor.CanAcceptRequest(key);

        // Assert
        health.State.Should().Be(HealthState.ProbeAllowed);
        canAccept.Should().BeTrue("probe allowed instances should accept limited requests");
    }

    #endregion

    #region GetHealth Tests

    [Fact]
    public void GetHealth_ShouldReturnNull_ForNonExistentKey()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-999");

        // Act
        var health = _monitor.GetHealth(key);

        // Assert
        health.Should().BeNull();
    }

    [Fact]
    public void GetHealth_ShouldReturnHealthRecord_ForExistingKey()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        _monitor.RecordRequestResult(key, success: true, DateTime.UtcNow);

        // Act
        var health = _monitor.GetHealth(key);

        // Assert
        health.Should().NotBeNull();
        health!.Key.Should().Be(key);
    }

    #endregion

    #region ResetHealth Tests

    [Fact]
    public void ResetHealth_ShouldResetToHealthy_ForIsolatedInstance()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Isolate the instance
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(key, success: false, timestamp);
        }

        var healthBefore = _monitor.GetHealth(key);
        healthBefore!.State.Should().Be(HealthState.Isolated);

        // Act
        var result = _monitor.ResetHealth(key);

        // Assert
        result.Should().BeTrue();

        var healthAfter = _monitor.GetHealth(key);
        healthAfter!.State.Should().Be(HealthState.Healthy);
        healthAfter.ConsecutiveTimeouts.Should().Be(0);
        healthAfter.CoolingPeriodExpiresUtc.Should().BeNull();
    }

    [Fact]
    public void ResetHealth_ShouldReturnFalse_ForNonExistentKey()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-999");

        // Act
        var result = _monitor.ResetHealth(key);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ResetHealth_ShouldPreserveStatistics()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;

        // Record some activity
        for (int i = 0; i < 10; i++)
        {
            _monitor.RecordRequestResult(key, success: i % 2 == 0, timestamp);
        }

        var healthBefore = _monitor.GetHealth(key);
        var totalBefore = healthBefore!.TotalRequests;
        var successBefore = healthBefore.SuccessfulRequests;

        // Act
        _monitor.ResetHealth(key);

        // Assert
        var healthAfter = _monitor.GetHealth(key);
        healthAfter!.TotalRequests.Should().Be(totalBefore, "statistics should not be reset");
        healthAfter.SuccessfulRequests.Should().Be(successBefore, "statistics should not be reset");
    }

    #endregion

    #region GetAllHealthStates Tests

    [Fact]
    public void GetAllHealthStates_ShouldReturnEmptyDictionary_Initially()
    {
        // Act
        var allStates = _monitor.GetAllHealthStates();

        // Assert
        allStates.Should().BeEmpty();
    }

    [Fact]
    public void GetAllHealthStates_ShouldReturnAllTrackedInstances()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-1");
        var key2 = new ServiceSchedulingKey("ChatRoom", "room-2");
        var key3 = new ServiceSchedulingKey("GameRoom", "game-1");
        var timestamp = DateTime.UtcNow;

        _monitor.RecordRequestResult(key1, success: true, timestamp);
        _monitor.RecordRequestResult(key2, success: true, timestamp);
        _monitor.RecordRequestResult(key3, success: true, timestamp);

        // Act
        var allStates = _monitor.GetAllHealthStates();

        // Assert
        allStates.Should().HaveCount(3);
        allStates.Should().ContainKey(key1);
        allStates.Should().ContainKey(key2);
        allStates.Should().ContainKey(key3);
    }

    [Fact]
    public void GetAllHealthStates_ShouldReturnReadOnlyDictionary()
    {
        // Act
        var allStates = _monitor.GetAllHealthStates();

        // Assert
        allStates.Should().BeAssignableTo<IReadOnlyDictionary<ServiceSchedulingKey, ServiceInstanceHealth>>();
    }

    #endregion

    #region GetSummary Tests

    [Fact]
    public void GetSummary_ShouldReturnZeroCounts_WhenNoInstances()
    {
        // Act
        var summary = _monitor.GetSummary();

        // Assert
        summary.TotalInstances.Should().Be(0);
        summary.HealthyInstances.Should().Be(0);
        summary.IsolatedInstances.Should().Be(0);
        summary.CoolingDownInstances.Should().Be(0);
        summary.ProbeAllowedInstances.Should().Be(0);
        summary.OverallStatus.Should().Be("Unknown");
    }

    [Fact]
    public void GetSummary_ShouldCountHealthyInstances()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            var key = new ServiceSchedulingKey("ChatRoom", $"room-{i}");
            _monitor.RecordRequestResult(key, success: true, timestamp);
        }

        // Act
        var summary = _monitor.GetSummary();

        // Assert
        summary.TotalInstances.Should().Be(5);
        summary.HealthyInstances.Should().Be(5);
        summary.IsolatedInstances.Should().Be(0);
        summary.OverallStatus.Should().Be("Healthy");
    }

    [Fact]
    public void GetSummary_ShouldCountIsolatedInstances()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Create 3 healthy instances
        for (int i = 0; i < 3; i++)
        {
            var key = new ServiceSchedulingKey("ChatRoom", $"healthy-{i}");
            _monitor.RecordRequestResult(key, success: true, timestamp);
        }

        // Create 2 isolated instances
        for (int i = 0; i < 2; i++)
        {
            var key = new ServiceSchedulingKey("ChatRoom", $"isolated-{i}");
            for (int j = 0; j < 3; j++)
            {
                _monitor.RecordRequestResult(key, success: false, timestamp);
            }
        }

        // Act
        var summary = _monitor.GetSummary();

        // Assert
        summary.TotalInstances.Should().Be(5);
        summary.HealthyInstances.Should().Be(3);
        summary.IsolatedInstances.Should().Be(2);
        summary.OverallStatus.Should().Be("Degraded");
    }

    [Fact]
    public void GetSummary_ShouldReturnUnhealthyStatus_WhenMajorityIsolated()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // Create 1 healthy instance
        var healthyKey = new ServiceSchedulingKey("ChatRoom", "healthy-1");
        _monitor.RecordRequestResult(healthyKey, success: true, timestamp);

        // Create 4 isolated instances (more than half)
        for (int i = 0; i < 4; i++)
        {
            var key = new ServiceSchedulingKey("ChatRoom", $"isolated-{i}");
            for (int j = 0; j < 3; j++)
            {
                _monitor.RecordRequestResult(key, success: false, timestamp);
            }
        }

        // Act
        var summary = _monitor.GetSummary();

        // Assert
        summary.TotalInstances.Should().Be(5);
        summary.IsolatedInstances.Should().Be(4);
        summary.OverallStatus.Should().Be("Unhealthy");
    }

    [Fact]
    public void GetSummary_ShouldCountAllStates_Accurately()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;

        // 1 Healthy
        var healthyKey = new ServiceSchedulingKey("ChatRoom", "healthy");
        _monitor.RecordRequestResult(healthyKey, success: true, timestamp);

        // 1 Isolated
        var isolatedKey = new ServiceSchedulingKey("ChatRoom", "isolated");
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(isolatedKey, success: false, timestamp);
        }

        // 1 CoolingDown
        var coolingKey = new ServiceSchedulingKey("ChatRoom", "cooling");
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(coolingKey, success: false, timestamp);
        }
        var coolingHealth = _monitor.GetHealth(coolingKey);
        var expiredTime = coolingHealth!.CoolingPeriodExpiresUtc!.Value.AddSeconds(1);
        _monitor.RecordRequestResult(coolingKey, success: true, expiredTime); // → CoolingDown

        // 1 ProbeAllowed
        var probeKey = new ServiceSchedulingKey("ChatRoom", "probe");
        for (int i = 0; i < 3; i++)
        {
            _monitor.RecordRequestResult(probeKey, success: false, timestamp);
        }
        var probeHealth = _monitor.GetHealth(probeKey);
        var expiredTime2 = probeHealth!.CoolingPeriodExpiresUtc!.Value.AddSeconds(1);
        _monitor.RecordRequestResult(probeKey, success: true, expiredTime2); // → CoolingDown
        _monitor.RecordRequestResult(probeKey, success: true, expiredTime2); // → ProbeAllowed

        // Act
        var summary = _monitor.GetSummary();

        // Assert
        summary.TotalInstances.Should().Be(4);
        summary.HealthyInstances.Should().Be(1);
        summary.IsolatedInstances.Should().Be(1);
        summary.CoolingDownInstances.Should().Be(1);
        summary.ProbeAllowedInstances.Should().Be(1);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task RecordRequestResult_ShouldBeThreadSafe_WithConcurrentAccess()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var timestamp = DateTime.UtcNow;
        const int taskCount = 100;
        const int requestsPerTask = 10;

        // Act - Concurrent requests from multiple threads
        var tasks = Enumerable.Range(0, taskCount)
            .Select(i => Task.Run(() =>
            {
                for (int j = 0; j < requestsPerTask; j++)
                {
                    _monitor.RecordRequestResult(key, success: true, timestamp.AddMilliseconds(i * requestsPerTask + j));
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert
        var health = _monitor.GetHealth(key);
        health.Should().NotBeNull();
        health!.TotalRequests.Should().Be(taskCount * requestsPerTask);
        health.SuccessfulRequests.Should().Be(taskCount * requestsPerTask);
    }

    [Fact]
    public async Task GetAllHealthStates_ShouldBeThreadSafe_WithConcurrentWrites()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        const int taskCount = 50;

        // Act - Concurrent writes and reads
        var writeTasks = Enumerable.Range(0, taskCount)
            .Select(i => Task.Run(() =>
            {
                var key = new ServiceSchedulingKey("ChatRoom", $"room-{i}");
                _monitor.RecordRequestResult(key, success: true, timestamp);
            }))
            .ToArray();

        var readTask = Task.Run(() =>
        {
            // Read while writes are happening
            for (int i = 0; i < 10; i++)
            {
                var allStates = _monitor.GetAllHealthStates();
                allStates.Should().NotBeNull();
            }
        });

        await Task.WhenAll(writeTasks.Concat(new[] { readTask }));

        // Assert
        var finalStates = _monitor.GetAllHealthStates();
        finalStates.Should().HaveCount(taskCount);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RecordRequestResult_ShouldHandle_MultipleServiceNames()
    {
        // Arrange
        var key1 = new ServiceSchedulingKey("ChatRoom", "room-1");
        var key2 = new ServiceSchedulingKey("GameRoom", "room-1"); // Same ID, different service
        var timestamp = DateTime.UtcNow;

        // Act
        _monitor.RecordRequestResult(key1, success: true, timestamp);
        _monitor.RecordRequestResult(key2, success: true, timestamp);

        // Assert
        var health1 = _monitor.GetHealth(key1);
        var health2 = _monitor.GetHealth(key2);

        health1.Should().NotBeNull();
        health2.Should().NotBeNull();
        health1.Should().NotBeSameAs(health2, "different services should have separate health records");
    }

    [Fact]
    public void RecordRequestResult_ShouldHandle_VeryOldTimestamps()
    {
        // Arrange
        var key = new ServiceSchedulingKey("ChatRoom", "room-123");
        var oldTimestamp = DateTime.UtcNow.AddDays(-365);

        // Act
        _monitor.RecordRequestResult(key, success: true, oldTimestamp);

        // Assert
        var health = _monitor.GetHealth(key);
        health!.LastActivityUtc.Should().Be(oldTimestamp);
    }

    #endregion
}
