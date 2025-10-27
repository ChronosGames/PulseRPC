using FluentAssertions;
using PulseRPC.Server.Scheduling;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PulseRPC.Server.Tests.Unit;

/// <summary>
/// Unit tests for ConsistentHashRing (T012)
/// Tests distribution uniformity, virtual nodes, and thread mapping stability
/// </summary>
public class ConsistentHashRingTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldInitialize_WithValidParameters()
    {
        // Arrange & Act
        var ring = new ConsistentHashRing(totalThreads: 16, virtualNodesPerThread: 150);

        // Assert
        ring.TotalThreads.Should().Be(16);
        ring.TotalVirtualNodes.Should().Be(16 * 150);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenThreadCountIsZero()
    {
        // Act
        Action act = () => new ConsistentHashRing(totalThreads: 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*工作线程总数必须大于 0*");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenVirtualNodesIsZero()
    {
        // Act
        Action act = () => new ConsistentHashRing(totalThreads: 16, virtualNodesPerThread: 0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*虚拟节点数量必须大于 0*");
    }

    #endregion

    #region GetThread Tests

    [Fact]
    public void GetThread_ShouldReturnValidThreadId_ForAnyServiceId()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);

        // Act
        var threadId = ring.GetThread("room-123");

        // Assert
        threadId.Should().BeInRange(0, 15);
    }

    [Fact]
    public void GetThread_ShouldBeConsistent_ForSameServiceId()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);
        var serviceId = "room-123";

        // Act
        var threadId1 = ring.GetThread(serviceId);
        var threadId2 = ring.GetThread(serviceId);
        var threadId3 = ring.GetThread(serviceId);

        // Assert
        threadId1.Should().Be(threadId2);
        threadId2.Should().Be(threadId3);
    }

    [Fact]
    public void GetThread_ShouldThrow_WhenServiceIdIsNull()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);

        // Act
        Action act = () => ring.GetThread(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Distribution Quality Tests

    [Theory]
    [InlineData(1000, 16, 150)]  // 1K instances, 16 threads
    [InlineData(5000, 16, 150)]  // 5K instances, 16 threads
    [InlineData(10000, 16, 150)] // 10K instances, 16 threads
    public void GetThread_ShouldDistribute_UniformlyAcrossThreads(int instanceCount, int threadCount, int virtualNodes)
    {
        // Arrange
        var ring = new ConsistentHashRing(threadCount, virtualNodes);
        var threadCounts = new Dictionary<int, int>();
        for (int i = 0; i < threadCount; i++)
        {
            threadCounts[i] = 0;
        }

        // Act - Map many service instances to threads
        for (int i = 0; i < instanceCount; i++)
        {
            var serviceId = $"room-{i}";
            var threadId = ring.GetThread(serviceId);
            threadCounts[threadId]++;
        }

        // Assert
        var expectedPerThread = instanceCount / threadCount;
        var standardDeviation = CalculateStandardDeviation(threadCounts.Values, expectedPerThread);
        var standardDeviationPercent = (standardDeviation / expectedPerThread) * 100;

        // Standard deviation should be less than 5% for good distribution
        standardDeviationPercent.Should().BeLessThan(5.0,
            $"Distribution should be uniform. Got: {string.Join(", ", threadCounts.Values)}");
    }

    [Fact]
    public void GetThread_Distribution_ShouldImproveWith_MoreVirtualNodes()
    {
        // Arrange
        const int threadCount = 16;
        const int instanceCount = 10000;

        var ring50 = new ConsistentHashRing(threadCount, virtualNodesPerThread: 50);
        var ring150 = new ConsistentHashRing(threadCount, virtualNodesPerThread: 150);

        // Act - Test distribution with 50 virtual nodes
        var counts50 = GetDistribution(ring50, instanceCount, threadCount);
        var stdDev50 = CalculateStandardDeviation(counts50, instanceCount / threadCount);

        // Act - Test distribution with 150 virtual nodes
        var counts150 = GetDistribution(ring150, instanceCount, threadCount);
        var stdDev150 = CalculateStandardDeviation(counts150, instanceCount / threadCount);

        // Assert - More virtual nodes should have better distribution
        stdDev150.Should().BeLessThan(stdDev50,
            "150 virtual nodes should distribute better than 50");
    }

    [Fact]
    public void GetThread_ShouldHandle_SequentialServiceIds()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);
        var threadCounts = new Dictionary<int, int>();

        // Act - Test with sequential IDs (potential worst case)
        for (int i = 0; i < 1000; i++)
        {
            var threadId = ring.GetThread($"{i}");
            if (!threadCounts.ContainsKey(threadId))
                threadCounts[threadId] = 0;
            threadCounts[threadId]++;
        }

        // Assert - Should still distribute reasonably well
        var maxDeviation = threadCounts.Values.Max() - threadCounts.Values.Min();
        var expectedPerThread = 1000.0 / 16;
        var maxDeviationPercent = (maxDeviation / expectedPerThread) * 100;

        maxDeviationPercent.Should().BeLessThan(30,
            "Sequential IDs should not cause extreme clustering");
    }

    #endregion

    #region Stability Tests

    [Fact]
    public void GetThread_ShouldRemainStable_AcrossMultipleCalls()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);
        var serviceIds = Enumerable.Range(0, 1000).Select(i => $"room-{i}").ToList();
        var initialMappings = new Dictionary<string, int>();

        // Act - Record initial mappings
        foreach (var serviceId in serviceIds)
        {
            initialMappings[serviceId] = ring.GetThread(serviceId);
        }

        // Act - Check mappings again
        foreach (var serviceId in serviceIds)
        {
            var threadId = ring.GetThread(serviceId);

            // Assert
            threadId.Should().Be(initialMappings[serviceId],
                $"ServiceId '{serviceId}' should always map to the same thread");
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetThread_ShouldHandle_EmptyString()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);

        // Act
        var threadId = ring.GetThread("");

        // Assert
        threadId.Should().BeInRange(0, 15);
    }

    [Fact]
    public void GetThread_ShouldHandle_VeryLongServiceId()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);
        var longServiceId = new string('A', 10000);

        // Act
        var threadId = ring.GetThread(longServiceId);

        // Assert
        threadId.Should().BeInRange(0, 15);
    }

    [Fact]
    public void GetThread_ShouldHandle_UnicodeServiceIds()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 16);

        // Act
        var thread1 = ring.GetThread("房间-123");
        var thread2 = ring.GetThread("комната-456");
        var thread3 = ring.GetThread("部屋-789");

        // Assert
        thread1.Should().BeInRange(0, 15);
        thread2.Should().BeInRange(0, 15);
        thread3.Should().BeInRange(0, 15);
    }

    [Fact]
    public void GetThread_ShouldHandle_SingleThread()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 1);

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            var threadId = ring.GetThread($"room-{i}");
            threadId.Should().Be(0, "single thread should always return 0");
        }
    }

    [Fact]
    public void GetThread_ShouldDistribute_WithMaxThreads()
    {
        // Arrange
        var ring = new ConsistentHashRing(totalThreads: 64); // Max supported

        // Act
        var threadIds = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
        {
            threadIds.Add(ring.GetThread($"room-{i}"));
        }

        // Assert
        threadIds.Count.Should().BeGreaterThan(50,
            "should use most of the 64 threads");
    }

    #endregion

    #region Helper Methods

    private static double CalculateStandardDeviation(IEnumerable<int> values, double mean)
    {
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
        return Math.Sqrt(variance);
    }

    private static List<int> GetDistribution(ConsistentHashRing ring, int instanceCount, int threadCount)
    {
        var threadCounts = Enumerable.Repeat(0, threadCount).ToList();

        for (int i = 0; i < instanceCount; i++)
        {
            var serviceId = $"room-{i}";
            var threadId = ring.GetThread(serviceId);
            threadCounts[threadId]++;
        }

        return threadCounts;
    }

    #endregion

    #region Performance Characteristics

    [Fact]
    public void GetThread_ShouldBeConsistentWith_DifferentHashRingInstances()
    {
        // Arrange - Two hash rings with same configuration
        var ring1 = new ConsistentHashRing(totalThreads: 16, virtualNodesPerThread: 150);
        var ring2 = new ConsistentHashRing(totalThreads: 16, virtualNodesPerThread: 150);

        // Act & Assert - Same service ID should map to same thread
        for (int i = 0; i < 100; i++)
        {
            var serviceId = $"room-{i}";
            var thread1 = ring1.GetThread(serviceId);
            var thread2 = ring2.GetThread(serviceId);

            thread1.Should().Be(thread2,
                $"ServiceId '{serviceId}' should map to same thread in both rings");
        }
    }

    #endregion
}
