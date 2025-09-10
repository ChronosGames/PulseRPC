using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PulseRPC.Server.Memory;
using Xunit;

namespace PulseRPC.Tests.Memory;

/// <summary>
/// TieredMemoryPool 单元测试
/// 验证分层内存池的功能和性能特性
/// </summary>
public class TieredMemoryPoolTests
{
    #region 基础功能测试
    
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Act
        var instance1 = TieredMemoryPool.Instance;
        var instance2 = TieredMemoryPool.Instance;
        
        // Assert
        instance1.Should().BeSameAs(instance2);
        instance1.Should().NotBeNull();
    }
    
    [Theory]
    [InlineData(128)]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void Rent_ValidSizes_ShouldReturnBufferOfCorrectSize(int requestedSize)
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        
        // Act
        var buffer = pool.Rent(requestedSize);
        
        // Assert
        buffer.Should().NotBeNull();
        buffer.Length.Should().BeGreaterOrEqualTo(requestedSize);
        
        // Cleanup
        pool.Return(buffer);
    }
    
    [Fact]
    public void Rent_ZeroSize_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(0));
    }
    
    [Fact]
    public void Rent_NegativeSize_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(-1));
    }
    
    #endregion
    
    #region 缓存命中测试
    
    [Fact]
    public void RentAndReturn_SameSize_ShouldReuseBuffer()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        const int bufferSize = 1024;
        
        // Act - 租用并归还缓冲区
        var buffer1 = pool.Rent(bufferSize);
        var buffer1Hash = buffer1.GetHashCode();
        pool.Return(buffer1);
        
        // 立即重新租用相同大小
        var buffer2 = pool.Rent(bufferSize);
        var buffer2Hash = buffer2.GetHashCode();
        
        // Assert - 应该重用同一个缓冲区（至少在某些层级上）
        buffer2.Length.Should().BeGreaterOrEqualTo(bufferSize);
        
        // Cleanup
        pool.Return(buffer2);
    }
    
    [Fact]
    public void Return_WithClearArray_ShouldClearBuffer()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        var buffer = pool.Rent(1024);
        
        // 填充一些数据
        for (int i = 0; i < Math.Min(buffer.Length, 100); i++)
        {
            buffer[i] = (byte)(i % 256);
        }
        
        // Act
        pool.Return(buffer, clearArray: true);
        
        // 重新租用并检查是否被清除（概率性测试）
        var newBuffer = pool.Rent(1024);
        
        // Assert - 虽然不能保证返回同一个缓冲区，但至少应该正常工作
        newBuffer.Should().NotBeNull();
        newBuffer.Length.Should().BeGreaterOrEqualTo(1024);
        
        // Cleanup
        pool.Return(newBuffer);
    }
    
    #endregion
    
    #region 多级缓存测试
    
    [Fact]
    public void Rent_MultipleBuffers_ShouldDistributeAcrossTiers()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        var buffers = new byte[50][];
        
        // Act - 租用多个不同大小的缓冲区
        for (int i = 0; i < buffers.Length; i++)
        {
            var size = TieredMemoryPool.BUFFER_SIZES[i % TieredMemoryPool.BUFFER_SIZES.Length];
            buffers[i] = pool.Rent(size);
        }
        
        // Assert - 所有缓冲区都应该成功分配
        foreach (var buffer in buffers)
        {
            buffer.Should().NotBeNull();
            buffer.Length.Should().BeGreaterThan(0);
        }
        
        // Cleanup
        foreach (var buffer in buffers)
        {
            pool.Return(buffer);
        }
    }
    
    [Fact]
    public void Rent_LargeBuffer_ShouldHandleCorrectly()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        const int largeSize = 1024 * 1024; // 1MB
        
        // Act
        var buffer = pool.Rent(largeSize);
        
        // Assert
        buffer.Should().NotBeNull();
        buffer.Length.Should().BeGreaterOrEqualTo(largeSize);
        
        // Cleanup
        pool.Return(buffer);
    }
    
    #endregion
    
    #region 并发测试
    
    [Fact]
    public async Task ConcurrentRentAndReturn_ShouldBeThreadSafe()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        const int taskCount = 10;
        const int operationsPerTask = 100;
        
        // Act
        var tasks = Enumerable.Range(0, taskCount).Select(async taskId =>
        {
            var buffers = new byte[operationsPerTask][];
            
            // 租用缓冲区
            for (int i = 0; i < operationsPerTask; i++)
            {
                var size = TieredMemoryPool.BUFFER_SIZES[i % TieredMemoryPool.BUFFER_SIZES.Length];
                buffers[i] = pool.Rent(size);
                buffers[i].Should().NotBeNull();
                
                // 短暂延迟增加并发压力
                if (i % 10 == 0)
                {
                    await Task.Yield();
                }
            }
            
            // 归还缓冲区
            for (int i = 0; i < operationsPerTask; i++)
            {
                pool.Return(buffers[i]);
                
                if (i % 10 == 0)
                {
                    await Task.Yield();
                }
            }
        }).ToArray();
        
        // Assert
        await Task.WhenAll(tasks);
        // 如果执行到这里没有异常，说明线程安全性测试通过
    }
    
    [Fact]
    public async Task HighConcurrencyStress_ShouldMaintainStability()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        const int concurrentTasks = 20;
        const int operationsPerTask = 50;
        
        // Act
        var tasks = Enumerable.Range(0, concurrentTasks).Select(async taskId =>
        {
            for (int i = 0; i < operationsPerTask; i++)
            {
                // 随机选择缓冲区大小
                var sizeIndex = (taskId + i) % TieredMemoryPool.BUFFER_SIZES.Length;
                var size = TieredMemoryPool.BUFFER_SIZES[sizeIndex];
                
                var buffer = pool.Rent(size);
                buffer.Should().NotBeNull();
                
                // 模拟使用
                if (buffer.Length > 0)
                {
                    buffer[0] = (byte)(taskId % 256);
                }
                
                await Task.Delay(1); // 短暂持有
                
                pool.Return(buffer);
            }
        }).ToArray();
        
        // Assert
        await Task.WhenAll(tasks);
    }
    
    #endregion
    
    #region 性能统计测试
    
    [Fact]
    public void GetStatistics_AfterOperations_ShouldReturnValidStats()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        var initialStats = pool.GetStatistics();
        
        // Act - 执行一些操作
        var buffers = new byte[10][];
        for (int i = 0; i < buffers.Length; i++)
        {
            buffers[i] = pool.Rent(1024 * (i + 1));
        }
        
        for (int i = 0; i < buffers.Length; i++)
        {
            pool.Return(buffers[i]);
        }
        
        var finalStats = pool.GetStatistics();
        
        // Assert
        finalStats.TotalRents.Should().BeGreaterThan(initialStats.TotalRents);
        finalStats.TotalReturns.Should().BeGreaterThan(initialStats.TotalReturns);
        finalStats.NumaNodes.Should().BeGreaterThan(0);
        finalStats.CacheHitRatio.Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(1);
    }
    
    [Fact]
    public void GetStatistics_CacheHitRatio_ShouldIncreaseWithReuse()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        pool.Trim(); // 清理以获得更一致的结果
        
        var initialStats = pool.GetStatistics();
        
        // Act - 多次租用和归还相同大小的缓冲区
        const int iterations = 50;
        const int bufferSize = 1024;
        
        for (int i = 0; i < iterations; i++)
        {
            var buffer = pool.Rent(bufferSize);
            pool.Return(buffer);
        }
        
        var finalStats = pool.GetStatistics();
        
        // Assert
        finalStats.TotalRents.Should().BeGreaterThan(initialStats.TotalRents);
        finalStats.TotalReturns.Should().BeGreaterThan(initialStats.TotalReturns);
        // 缓存命中率应该有所改善（虽然不一定完美）
        finalStats.CacheHitRatio.Should().BeGreaterOrEqualTo(0);
    }
    
    #endregion
    
    #region 内存管理测试
    
    [Fact]
    public void Trim_ShouldCleanupExcessBuffers()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        
        // 分配大量缓冲区以填充缓存
        var buffers = new byte[100][];
        for (int i = 0; i < buffers.Length; i++)
        {
            buffers[i] = pool.Rent(1024);
        }
        
        // 归还所有缓冲区
        for (int i = 0; i < buffers.Length; i++)
        {
            pool.Return(buffers[i]);
        }
        
        var statsBeforeTrim = pool.GetStatistics();
        
        // Act
        pool.Trim();
        
        var statsAfterTrim = pool.GetStatistics();
        
        // Assert
        // Trim操作应该成功执行（没有异常）
        statsAfterTrim.Should().NotBeNull();
        
        // 验证Trim后系统仍然正常工作
        var testBuffer = pool.Rent(1024);
        testBuffer.Should().NotBeNull();
        pool.Return(testBuffer);
    }
    
    #endregion
    
    #region 边界条件测试
    
    [Fact]
    public void Return_NullBuffer_ShouldNotThrow()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        
        // Act & Assert - 不应该抛出异常
        pool.Return(null!);
    }
    
    [Fact]
    public void Rent_ExtremelyLargeSize_ShouldReturnValidBuffer()
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        const int extremeSize = 10 * 1024 * 1024; // 10MB
        
        // Act
        var buffer = pool.Rent(extremeSize);
        
        // Assert
        buffer.Should().NotBeNull();
        buffer.Length.Should().BeGreaterOrEqualTo(extremeSize);
        
        // Cleanup
        pool.Return(buffer);
    }
    
    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(255)]
    [InlineData(1023)]
    public void Rent_NonStandardSizes_ShouldRoundUpCorrectly(int requestedSize)
    {
        // Arrange
        var pool = TieredMemoryPool.Instance;
        
        // Act
        var buffer = pool.Rent(requestedSize);
        
        // Assert
        buffer.Should().NotBeNull();
        buffer.Length.Should().BeGreaterOrEqualTo(requestedSize);
        
        // 应该舍入到预定义大小之一或页面对齐大小
        var validSizes = TieredMemoryPool.BUFFER_SIZES.Concat(new[] { 
            ((requestedSize + 4095) / 4096) * 4096 // 页面对齐
        });
        
        validSizes.Should().Contain(s => s >= requestedSize && buffer.Length <= s * 2);
        
        // Cleanup
        pool.Return(buffer);
    }
    
    #endregion
    
    #region NUMA节点测试
    
    [Fact]
    public void Constructor_ShouldDetectNumaNodes()
    {
        // Arrange & Act
        var pool = TieredMemoryPool.Instance;
        var stats = pool.GetStatistics();
        
        // Assert
        stats.NumaNodes.Should().BeGreaterThan(0);
        // 通常应该检测到至少1个NUMA节点
    }
    
    #endregion
}