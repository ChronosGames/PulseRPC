using System;
using System.Threading.Tasks;
using FluentAssertions;
using PulseRPC.Memory;
using Xunit;

namespace PulseRPC.Tests.Memory;

/// <summary>
/// ZeroCopyCircularBuffer 单元测试
/// 验证零拷贝循环缓冲区的功能和性能特性
/// </summary>
public class ZeroCopyCircularBufferTests : IDisposable
{
    private readonly ZeroCopyCircularBuffer<int> _buffer;
    
    public ZeroCopyCircularBufferTests()
    {
        _buffer = new ZeroCopyCircularBuffer<int>(64); // 使用64个元素的缓冲区进行测试
    }
    
    public void Dispose()
    {
        _buffer.Dispose();
    }
    
    #region 基础功能测试
    
    [Fact]
    public void Constructor_ValidCapacity_ShouldCreateBuffer()
    {
        // Arrange & Act
        using var buffer = new ZeroCopyCircularBuffer<int>(128);
        
        // Assert
        buffer.Capacity.Should().Be(128);
        buffer.Count.Should().Be(0);
        buffer.IsEmpty.Should().BeTrue();
        buffer.IsFull.Should().BeFalse();
    }
    
    [Theory]
    [InlineData(32)]   // 小于最小值
    [InlineData(63)]   // 不是2的幂
    [InlineData(1048577)] // 大于最大值
    public void Constructor_InvalidCapacity_ShouldThrowException(int capacity)
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZeroCopyCircularBuffer<int>(capacity));
    }
    
    [Fact]
    public void TryEnqueue_EmptyBuffer_ShouldSucceed()
    {
        // Act
        var result = _buffer.TryEnqueue(42);
        
        // Assert
        result.Should().BeTrue();
        _buffer.Count.Should().Be(1);
        _buffer.IsEmpty.Should().BeFalse();
        _buffer.IsFull.Should().BeFalse();
    }
    
    [Fact]
    public void TryDequeue_BufferWithOneItem_ShouldSucceedAndReturnItem()
    {
        // Arrange
        _buffer.TryEnqueue(42);
        
        // Act
        var result = _buffer.TryDequeue(out var item);
        
        // Assert
        result.Should().BeTrue();
        item.Should().Be(42);
        _buffer.Count.Should().Be(0);
        _buffer.IsEmpty.Should().BeTrue();
    }
    
    [Fact]
    public void TryDequeue_EmptyBuffer_ShouldFail()
    {
        // Act
        var result = _buffer.TryDequeue(out var item);
        
        // Assert
        result.Should().BeFalse();
        item.Should().Be(0);
        _buffer.Count.Should().Be(0);
    }
    
    #endregion
    
    #region 容量和边界测试
    
    [Fact]
    public void FillToCapacity_ShouldMarkBufferAsFull()
    {
        // Arrange & Act
        for (int i = 0; i < _buffer.Capacity; i++)
        {
            var result = _buffer.TryEnqueue(i);
            result.Should().BeTrue($"Should enqueue item {i}");
        }
        
        // Assert
        _buffer.Count.Should().Be(_buffer.Capacity);
        _buffer.IsFull.Should().BeTrue();
    }
    
    [Fact]
    public void TryEnqueue_FullBuffer_ShouldFail()
    {
        // Arrange - 填满缓冲区
        for (int i = 0; i < _buffer.Capacity; i++)
        {
            _buffer.TryEnqueue(i);
        }
        
        // Act - 尝试再添加一个元素
        var result = _buffer.TryEnqueue(999);
        
        // Assert
        result.Should().BeFalse();
        _buffer.Count.Should().Be(_buffer.Capacity);
        _buffer.IsFull.Should().BeTrue();
    }
    
    [Fact]
    public void CircularBehavior_ShouldWorkCorrectly()
    {
        // Arrange - 填满缓冲区
        for (int i = 0; i < _buffer.Capacity; i++)
        {
            _buffer.TryEnqueue(i);
        }
        
        // Act - 出队一半元素
        var halfCapacity = _buffer.Capacity / 2;
        for (int i = 0; i < halfCapacity; i++)
        {
            var result = _buffer.TryDequeue(out var item);
            result.Should().BeTrue();
            item.Should().Be(i);
        }
        
        // 再入队一半元素
        for (int i = 0; i < halfCapacity; i++)
        {
            var result = _buffer.TryEnqueue(i + 1000);
            result.Should().BeTrue();
        }
        
        // Assert
        _buffer.Count.Should().Be(_buffer.Capacity);
        _buffer.IsFull.Should().BeTrue();
    }
    
    #endregion
    
    #region 批量操作测试
    
    [Fact]
    public void TryEnqueueBatch_ValidData_ShouldSucceed()
    {
        // Arrange
        var data = new int[] { 1, 2, 3, 4, 5 };
        
        // Act
        var result = _buffer.TryEnqueueBatch(data);
        
        // Assert
        result.Should().Be(5);
        _buffer.Count.Should().Be(5);
    }
    
    [Fact]
    public void TryEnqueueBatch_ExceedsCapacity_ShouldEnqueueWhatFits()
    {
        // Arrange
        var data = new int[_buffer.Capacity + 10];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = i;
        }
        
        // Act
        var result = _buffer.TryEnqueueBatch(data);
        
        // Assert
        result.Should().Be(_buffer.Capacity);
        _buffer.IsFull.Should().BeTrue();
    }
    
    [Fact]
    public void TryDequeueBatch_WithData_ShouldReturnCorrectSlice()
    {
        // Arrange
        var originalData = new int[] { 10, 20, 30, 40, 50 };
        _buffer.TryEnqueueBatch(originalData);
        
        // Act
        var result = _buffer.TryDequeueBatch(3);
        
        // Assert
        result.Length.Should().Be(3);
        var span = result.Span;
        span[0].Should().Be(10);
        span[1].Should().Be(20);
        span[2].Should().Be(30);
        
        _buffer.Count.Should().Be(2);
    }
    
    [Fact]
    public void TryDequeueBatch_ToArray_ShouldFillDestinationCorrectly()
    {
        // Arrange
        var originalData = new int[] { 100, 200, 300, 400 };
        _buffer.TryEnqueueBatch(originalData);
        var destination = new int[6];
        
        // Act
        var result = _buffer.TryDequeueBatch(destination, 3);
        
        // Assert
        result.Should().Be(3);
        destination[0].Should().Be(100);
        destination[1].Should().Be(200);
        destination[2].Should().Be(300);
        destination[3].Should().Be(0); // 未填充的部分
        
        _buffer.Count.Should().Be(1);
    }
    
    #endregion
    
    #region 超时操作测试
    
    [Fact]
    public void TryEnqueue_WithTimeout_EmptyBuffer_ShouldSucceedImmediately()
    {
        // Act
        var result = _buffer.TryEnqueue(42, TimeSpan.FromMilliseconds(100));
        
        // Assert
        result.Should().BeTrue();
        _buffer.Count.Should().Be(1);
    }
    
    [Fact]
    public void TryEnqueue_WithZeroTimeout_FullBuffer_ShouldFailImmediately()
    {
        // Arrange - 填满缓冲区
        for (int i = 0; i < _buffer.Capacity; i++)
        {
            _buffer.TryEnqueue(i);
        }
        
        // Act
        var result = _buffer.TryEnqueue(999, TimeSpan.Zero);
        
        // Assert
        result.Should().BeFalse();
    }
    
    [Fact]
    public async Task TryEnqueue_WithTimeout_ShouldWaitForSpace()
    {
        // Arrange - 填满缓冲区
        for (int i = 0; i < _buffer.Capacity; i++)
        {
            _buffer.TryEnqueue(i);
        }
        
        // Act - 在后台任务中释放空间
        var enqueueTask = Task.Run(() => _buffer.TryEnqueue(999, TimeSpan.FromMilliseconds(500)));
        
        await Task.Delay(100); // 等待入队任务开始等待
        
        // 释放一个空间
        _buffer.TryDequeue(out _);
        
        var result = await enqueueTask;
        
        // Assert
        result.Should().BeTrue();
    }
    
    #endregion
    
    #region 线程安全测试
    
    [Fact]
    public async Task ConcurrentEnqueueDequeue_ShouldMaintainConsistency()
    {
        // Arrange
        const int itemCount = 10000;
        const int taskCount = 4;
        var enqueueTask = Task.Run(async () =>
        {
            for (int i = 0; i < itemCount; i++)
            {
                while (!_buffer.TryEnqueue(i))
                {
                    await Task.Yield();
                }
            }
        });
        
        var dequeueTasks = new Task[taskCount];
        var totalDequeued = 0;
        
        for (int t = 0; t < taskCount; t++)
        {
            dequeueTasks[t] = Task.Run(async () =>
            {
                var localCount = 0;
                while (localCount < itemCount / taskCount)
                {
                    if (_buffer.TryDequeue(out _))
                    {
                        localCount++;
                        Interlocked.Increment(ref totalDequeued);
                    }
                    else
                    {
                        await Task.Yield();
                    }
                }
            });
        }
        
        // Act
        await Task.WhenAll(new[] { enqueueTask }.Concat(dequeueTasks));
        
        // Assert
        totalDequeued.Should().Be(itemCount);
        _buffer.Count.Should().Be(0);
    }
    
    #endregion
    
    #region 性能统计测试
    
    [Fact]
    public void GetStatistics_AfterOperations_ShouldReturnCorrectStats()
    {
        // Arrange
        var testData = new int[] { 1, 2, 3, 4, 5 };
        _buffer.TryEnqueueBatch(testData);
        _buffer.TryDequeue(out _);
        _buffer.TryDequeue(out _);
        
        // Act
        var stats = _buffer.GetStatistics();
        
        // Assert
        stats.Capacity.Should().Be(_buffer.Capacity);
        stats.Count.Should().Be(3);
        stats.TotalEnqueues.Should().BeGreaterThan(0);
        stats.TotalDequeues.Should().BeGreaterThan(0);
        stats.Utilization.Should().BeApproximately(3.0 / _buffer.Capacity, 0.01);
    }
    
    #endregion
    
    #region 清理操作测试
    
    [Fact]
    public void Clear_BufferWithData_ShouldEmptyBuffer()
    {
        // Arrange
        _buffer.TryEnqueue(1);
        _buffer.TryEnqueue(2);
        _buffer.TryEnqueue(3);
        
        // Act
        _buffer.Clear();
        
        // Assert
        _buffer.Count.Should().Be(0);
        _buffer.IsEmpty.Should().BeTrue();
        _buffer.IsFull.Should().BeFalse();
    }
    
    #endregion
    
    #region 边界条件测试
    
    [Fact]
    public void Operations_AfterDispose_ShouldThrow()
    {
        // Arrange
        _buffer.Dispose();
        
        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _buffer.TryEnqueue(1));
        Assert.Throws<ObjectDisposedException>(() => _buffer.TryDequeue(out _));
    }
    
    #endregion
}