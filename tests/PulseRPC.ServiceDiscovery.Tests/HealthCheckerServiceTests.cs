using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using PulseRPC.HealthCheck;
using PulseServiceDiscovery.Client.HealthCheck;

namespace PulseRPC.ServiceDiscovery.Tests;

/// <summary>
/// HealthCheckService单元测试
/// </summary>
public class HealthCheckerServiceTests : IDisposable
{
    private readonly Mock<ILogger<HealthCheckerService>> _mockLogger;
    private readonly HealthCheckOptions _options;
    private readonly HealthCheckerService _healthCheckerService;

    public HealthCheckerServiceTests()
    {
        _mockLogger = new Mock<ILogger<HealthCheckerService>>();
        _options = new HealthCheckOptions
        {
            DefaultTimeout = TimeSpan.FromSeconds(5),
            TcpCheckTimeout = TimeSpan.FromSeconds(3),
            RetryCount = 2,
            RetryDelay = TimeSpan.FromMilliseconds(100),
            EnableConcurrentChecks = true,
            MaxConcurrentChecks = 50
        };

        var optionsMock = new Mock<IOptions<HealthCheckOptions>>();
        optionsMock.Setup(x => x.Value).Returns(_options);

        _healthCheckerService = new HealthCheckerService(optionsMock.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        _healthCheckerService?.Dispose();
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldThrowArgumentNullException_WhenEndpointIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _healthCheckerService.MonitorHealthAsync(null!, TimeSpan.FromSeconds(1)).GetAsyncEnumerator().MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldThrowArgumentException_WhenIntervalIsZero()
    {
        // Arrange
        var endpoint = CreateTestEndpoint();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _healthCheckerService.MonitorHealthAsync(endpoint, TimeSpan.Zero).GetAsyncEnumerator().MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldThrowArgumentException_WhenIntervalIsNegative()
    {
        // Arrange
        var endpoint = CreateTestEndpoint();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _healthCheckerService.MonitorHealthAsync(endpoint, TimeSpan.FromMilliseconds(-1)).GetAsyncEnumerator().MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldYieldHealthCheckResults_WhenEndpointIsValid()
    {
        // Arrange
        var endpoint = CreateTestEndpoint();
        var interval = TimeSpan.FromMilliseconds(50);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var results = new List<HealthCheckResult>();

        // Act
        await foreach (var result in _healthCheckerService.MonitorHealthAsync(endpoint, interval, cts.Token))
        {
            results.Add(result);
            if (results.Count >= 3) // 收集3个结果后停止
                break;
        }

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result =>
        {
            Assert.Equal(endpoint.ServiceId, result.ServiceId);
            Assert.True(result.CheckTime > DateTime.MinValue);
            Assert.True(result.Attempts > 0);
        });
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldStopGracefully_WhenCancellationRequested()
    {
        // Arrange
        var endpoint = CreateTestEndpoint();
        var interval = TimeSpan.FromMilliseconds(10);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var results = new List<HealthCheckResult>();

        // Act
        await foreach (var result in _healthCheckerService.MonitorHealthAsync(endpoint, interval, cts.Token))
        {
            results.Add(result);
        }

        // Assert
        Assert.NotEmpty(results);
        // 验证在取消令牌触发时停止了监控
        Assert.True(cts.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldReturnUnhealthyResult_WhenEndpointIsUnavailable()
    {
        // Arrange
        var endpoint = new ServiceEndpoint
        {
            ServiceId = "test-service-unavailable",
            ServiceName = "Test Service",
            EndPoint = new IPEndPoint(IPAddress.Parse("192.0.2.1"), 12345) // 使用测试专用IP地址
        };
        var interval = TimeSpan.FromMilliseconds(100);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        var results = new List<HealthCheckResult>();
        await foreach (var result in _healthCheckerService.MonitorHealthAsync(endpoint, interval, cts.Token))
        {
            results.Add(result);
            if (results.Count >= 2) // 收集2个结果后停止
                break;
        }

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result =>
        {
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.NotNull(result.ErrorMessage);
            Assert.True(result.ResponseTime >= TimeSpan.Zero);
        });
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldMaintainInterval_BetweenChecks()
    {
        // Arrange
        var endpoint = CreateTestEndpoint();
        var interval = TimeSpan.FromMilliseconds(200);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var timestamps = new List<DateTime>();

        // Act
        await foreach (var result in _healthCheckerService.MonitorHealthAsync(endpoint, interval, cts.Token))
        {
            timestamps.Add(result.CheckTime);
            if (timestamps.Count >= 3) // 收集3个时间戳
                break;
        }

        // Assert
        Assert.True(timestamps.Count >= 2);
        for (int i = 1; i < timestamps.Count; i++)
        {
            var actualInterval = timestamps[i] - timestamps[i - 1];
            // 允许一定的时间误差（±50ms）
            Assert.True(actualInterval >= TimeSpan.FromMilliseconds(150),
                $"间隔太短: {actualInterval.TotalMilliseconds}ms");
            Assert.True(actualInterval <= TimeSpan.FromMilliseconds(300),
                $"间隔太长: {actualInterval.TotalMilliseconds}ms");
        }
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldLogStartAndStop_Messages()
    {
        // Arrange
        var endpoint = CreateTestEndpoint();
        var interval = TimeSpan.FromMilliseconds(50);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        await foreach (var result in _healthCheckerService.MonitorHealthAsync(endpoint, interval, cts.Token))
        {
            // 只需要启动监控即可
            break;
        }

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("开始监控服务健康状态")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("停止监控服务健康状态")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task MonitorHealthAsync_ShouldHandleMultipleSimultaneousMonitoring()
    {
        // Arrange
        var endpoint1 = CreateTestEndpoint("service-1");
        var endpoint2 = CreateTestEndpoint("service-2");
        var interval = TimeSpan.FromMilliseconds(100);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        var task1 = CollectResultsAsync(endpoint1, interval, cts.Token, 2);
        var task2 = CollectResultsAsync(endpoint2, interval, cts.Token, 2);

        var results = await Task.WhenAll(task1, task2);

        // Assert
        var results1 = results[0];
        var results2 = results[1];

        Assert.NotEmpty(results1);
        Assert.NotEmpty(results2);
        Assert.All(results1, r => Assert.Equal("service-1", r.ServiceId));
        Assert.All(results2, r => Assert.Equal("service-2", r.ServiceId));
    }

    private async Task<List<HealthCheckResult>> CollectResultsAsync(
        ServiceEndpoint endpoint,
        TimeSpan interval,
        CancellationToken cancellationToken,
        int maxResults)
    {
        var results = new List<HealthCheckResult>();

        await foreach (var result in _healthCheckerService.MonitorHealthAsync(endpoint, interval, cancellationToken))
        {
            results.Add(result);
            if (results.Count >= maxResults)
                break;
        }

        return results;
    }

    private static ServiceEndpoint CreateTestEndpoint(string serviceId = "test-service")
    {
        return new ServiceEndpoint
        {
            ServiceId = serviceId,
            ServiceName = "Test Service",
            EndPoint = new IPEndPoint(IPAddress.Loopback, 80) // 使用回环地址，通常会快速失败
        };
    }
}
