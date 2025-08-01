using FluentAssertions;
using GameApp.SystemTests.Infrastructure;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace GameApp.SystemTests.Performance
{
    /// <summary>
    /// 系统性能基准测试 - 测试各个组件的性能指标
    /// </summary>
    [Collection("SystemTests")]
    public class BenchmarkTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _authClient;

        public BenchmarkTests(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _authClient = _fixture.CreateAuthServerClient();
        }

        [Fact]
        public async Task AuthServer_ResponseTime_ShouldMeetBenchmarks()
        {
            // Arrange
            _output.WriteLine("开始响应时间基准测试");

            await _fixture.WaitForServicesReadyAsync();

            // 先创建测试用户
            var registerRequest = new
            {
                Username = "benchmarkuser",
                Password = "benchmark123",
                Email = "benchmark@example.com",
                DeviceId = "benchmark-device"
            };

            await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            var loginRequest = new
            {
                Username = "benchmarkuser",
                Password = "benchmark123",
                DeviceId = "benchmark-device"
            };

            var responseTimes = new List<long>();
            const int testRuns = 50;

            // Act - 测量多次请求的响应时间
            for (int i = 0; i < testRuns; i++)
            {
                var stopwatch = Stopwatch.StartNew();

                var response = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);

                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    responseTimes.Add(stopwatch.ElapsedMilliseconds);
                }

                // 小延迟避免过度压力
                await Task.Delay(10);
            }

            // Assert
            responseTimes.Should().NotBeEmpty("应该有成功的响应时间记录");

            var averageTime = responseTimes.Average();
            var minTime = responseTimes.Min();
            var maxTime = responseTimes.Max();
            var p95Time = responseTimes.OrderBy(t => t).Skip((int)(testRuns * 0.95)).First();

            _output.WriteLine($"响应时间统计 (基于 {responseTimes.Count} 次测试):");
            _output.WriteLine($"平均响应时间: {averageTime:F2}ms");
            _output.WriteLine($"最小响应时间: {minTime}ms");
            _output.WriteLine($"最大响应时间: {maxTime}ms");
            _output.WriteLine($"95%响应时间: {p95Time}ms");

            // 性能基准验证
            averageTime.Should().BeLessThan(200, "平均响应时间应该少于200ms");
            p95Time.Should().BeLessThan(500, "95%的请求应该在500ms内完成");
            maxTime.Should().BeLessThan(1000, "最大响应时间应该少于1秒");

            _output.WriteLine("响应时间基准测试通过");
        }

        [Fact]
        public async Task AuthServer_Throughput_ShouldMeetBenchmarks()
        {
            // Arrange
            _output.WriteLine("开始吞吐量基准测试");

            await _fixture.WaitForServicesReadyAsync();

            // 准备多个测试用户
            var users = new List<(string username, string password)>();
            for (int i = 0; i < 20; i++)
            {
                var username = $"throughputuser{i}";
                var password = "throughput123";

                var registerRequest = new
                {
                    Username = username,
                    Password = password,
                    Email = $"throughput{i}@example.com",
                    DeviceId = $"throughput-device-{i}"
                };

                await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);
                users.Add((username, password));
            }

            const int concurrentRequests = 10;
            const int requestsPerUser = 5;
            var totalRequests = concurrentRequests * requestsPerUser;
            var successfulRequests = 0;

            var stopwatch = Stopwatch.StartNew();

            // Act - 并发执行多个请求
            var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
            {
                using var client = _fixture.CreateAuthServerClient();
                var user = users[i % users.Count];
                var localSuccesses = 0;

                for (int j = 0; j < requestsPerUser; j++)
                {
                    var loginRequest = new
                    {
                        Username = user.username,
                        Password = user.password,
                        DeviceId = $"throughput-device-{i}-{j}"
                    };

                    try
                    {
                        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
                        if (response.IsSuccessStatusCode)
                        {
                            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                            if (result.GetProperty("success").GetBoolean())
                            {
                                localSuccesses++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"请求失败: {ex.Message}");
                    }
                }

                return localSuccesses;
            });

            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            successfulRequests = results.Sum();
            var throughput = (double)successfulRequests / stopwatch.Elapsed.TotalSeconds;

            // Assert
            _output.WriteLine($"吞吐量测试结果:");
            _output.WriteLine($"总请求数: {totalRequests}");
            _output.WriteLine($"成功请求数: {successfulRequests}");
            _output.WriteLine($"测试时间: {stopwatch.Elapsed.TotalSeconds:F2}秒");
            _output.WriteLine($"吞吐量: {throughput:F2} 请求/秒");

            // 吞吐量基准验证
            throughput.Should().BeGreaterThan(10, "吞吐量应该大于10请求/秒");
            var successRate = (double)successfulRequests / totalRequests;
            successRate.Should().BeGreaterThan(0.9, "成功率应该大于90%");

            _output.WriteLine("吞吐量基准测试通过");
        }

        [Fact]
        public async Task AuthServer_ConcurrencyHandling_ShouldBeStable()
        {
            // Arrange
            _output.WriteLine("开始并发处理稳定性测试");

            await _fixture.WaitForServicesReadyAsync();

            // 创建测试用户
            var registerRequest = new
            {
                Username = "concurrencyuser",
                Password = "concurrency123",
                Email = "concurrency@example.com",
                DeviceId = "concurrency-device"
            };

            await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            const int concurrentUsers = 20;
            var successCount = 0;
            var errorCount = 0;
            var timeoutCount = 0;

            var tasks = Enumerable.Range(0, concurrentUsers).Select(async i =>
            {
                using var client = _fixture.CreateAuthServerClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var loginRequest = new
                {
                    Username = "concurrencyuser",
                    Password = "concurrency123",
                    DeviceId = $"concurrency-device-{i}"
                };

                try
                {
                    var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                        if (result.GetProperty("success").GetBoolean())
                        {
                            Interlocked.Increment(ref successCount);
                            return "success";
                        }
                    }

                    Interlocked.Increment(ref errorCount);
                    return $"error:{response.StatusCode}";
                }
                catch (TaskCanceledException)
                {
                    Interlocked.Increment(ref timeoutCount);
                    return "timeout";
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    return $"exception:{ex.GetType().Name}";
                }
            });

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // Assert
            _output.WriteLine($"并发处理测试结果:");
            _output.WriteLine($"并发用户数: {concurrentUsers}");
            _output.WriteLine($"成功请求: {successCount}");
            _output.WriteLine($"错误请求: {errorCount}");
            _output.WriteLine($"超时请求: {timeoutCount}");
            _output.WriteLine($"总耗时: {stopwatch.ElapsedMilliseconds}ms");

            // 验证并发处理的稳定性
            successCount.Should().BeGreaterThan(concurrentUsers / 2, "至少一半的并发请求应该成功");
            timeoutCount.Should().BeLessThan(concurrentUsers / 4, "超时请求应该少于25%");

            // 检查是否有服务器崩溃
            var healthResponse = await _authClient.GetAsync("/ready");
            healthResponse.IsSuccessStatusCode.Should().BeTrue("服务器应该在并发测试后仍然健康");

            _output.WriteLine("并发处理稳定性测试通过");
        }

        [Fact]
        public async Task AuthServer_MemoryEfficiency_ShouldBeOptimal()
        {
            // Arrange
            _output.WriteLine("开始内存效率测试");

            await _fixture.WaitForServicesReadyAsync();

            // 记录初始内存
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var initialMemory = GC.GetTotalMemory(false);
            _output.WriteLine($"初始内存: {initialMemory / 1024 / 1024:F2} MB");

            // 执行大量操作来测试内存使用
            const int operationCount = 200;
            var successfulOperations = 0;

            for (int i = 0; i < operationCount; i++)
            {
                var registerRequest = new
                {
                    Username = $"memuser{i}",
                    Password = "memtest123",
                    Email = $"mem{i}@example.com",
                    DeviceId = $"mem-device-{i}"
                };

                try
                {
                    var response = await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);
                    if (response.IsSuccessStatusCode)
                    {
                        successfulOperations++;
                    }
                }
                catch
                {
                    // 忽略错误，专注于内存测试
                }

                // 每隔50次操作检查内存
                if (i % 50 == 0 && i > 0)
                {
                    var currentMemory = GC.GetTotalMemory(false);
                    var memoryIncrease = (currentMemory - initialMemory) / 1024.0 / 1024.0;
                    _output.WriteLine($"操作 {i} 后内存增长: {memoryIncrease:F2} MB");
                }
            }

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var totalMemoryIncrease = (finalMemory - initialMemory) / 1024.0 / 1024.0;

            // Assert
            _output.WriteLine($"内存效率测试结果:");
            _output.WriteLine($"操作次数: {operationCount}");
            _output.WriteLine($"成功操作: {successfulOperations}");
            _output.WriteLine($"最终内存增长: {totalMemoryIncrease:F2} MB");
            _output.WriteLine($"每操作内存增长: {totalMemoryIncrease / operationCount * 1024:F2} KB");

            // 内存效率验证
            totalMemoryIncrease.Should().BeLessThan(100, "总内存增长应该少于100MB");
            var memoryPerOperation = totalMemoryIncrease / operationCount * 1024; // KB
            memoryPerOperation.Should().BeLessThan(500, "每个操作的内存增长应该少于500KB");

            _output.WriteLine("内存效率测试通过");
        }

        [Fact]
        public async Task AuthServer_DatabaseOperationPerformance_ShouldBeAcceptable()
        {
            // Arrange
            _output.WriteLine("开始数据库操作性能测试");

            await _fixture.WaitForServicesReadyAsync();

            var operationTimes = new Dictionary<string, List<long>>
            {
                ["Register"] = new List<long>(),
                ["Login"] = new List<long>(),
                ["GetZones"] = new List<long>()
            };

            const int testIterations = 20;

            // Act - 测试各种数据库操作的性能
            for (int i = 0; i < testIterations; i++)
            {
                var username = $"dbperfuser{i}";
                var password = "dbperf123";

                // 测试注册性能
                var registerStopwatch = Stopwatch.StartNew();
                var registerRequest = new
                {
                    Username = username,
                    Password = password,
                    Email = $"dbperf{i}@example.com",
                    DeviceId = $"dbperf-device-{i}"
                };

                var registerResponse = await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);
                registerStopwatch.Stop();

                if (registerResponse.IsSuccessStatusCode)
                {
                    operationTimes["Register"].Add(registerStopwatch.ElapsedMilliseconds);
                }

                // 测试登录性能
                var loginStopwatch = Stopwatch.StartNew();
                var loginRequest = new
                {
                    Username = username,
                    Password = password,
                    DeviceId = $"dbperf-device-{i}"
                };

                var loginResponse = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);
                loginStopwatch.Stop();

                if (loginResponse.IsSuccessStatusCode)
                {
                    var loginResult = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
                    if (loginResult.GetProperty("success").GetBoolean())
                    {
                        operationTimes["Login"].Add(loginStopwatch.ElapsedMilliseconds);

                        // 测试获取区服列表性能
                        var token = loginResult.GetProperty("accessToken").GetString();
                        _authClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                        var zonesStopwatch = Stopwatch.StartNew();
                        var zonesResponse = await _authClient.GetAsync("/api/zones");
                        zonesStopwatch.Stop();

                        if (zonesResponse.IsSuccessStatusCode)
                        {
                            operationTimes["GetZones"].Add(zonesStopwatch.ElapsedMilliseconds);
                        }

                        // 清除授权头
                        _authClient.DefaultRequestHeaders.Authorization = null;
                    }
                }

                await Task.Delay(50); // 短暂延迟
            }

            // Assert
            _output.WriteLine("数据库操作性能统计:");

            foreach (var operation in operationTimes)
            {
                if (operation.Value.Any())
                {
                    var avg = operation.Value.Average();
                    var min = operation.Value.Min();
                    var max = operation.Value.Max();

                    _output.WriteLine($"{operation.Key}:");
                    _output.WriteLine($"  平均时间: {avg:F2}ms");
                    _output.WriteLine($"  最小时间: {min}ms");
                    _output.WriteLine($"  最大时间: {max}ms");
                    _output.WriteLine($"  测试次数: {operation.Value.Count}");

                    // 性能验证
                    avg.Should().BeLessThan(1000, $"{operation.Key}操作平均时间应该少于1秒");
                    max.Should().BeLessThan(3000, $"{operation.Key}操作最大时间应该少于3秒");
                }
            }

            _output.WriteLine("数据库操作性能测试通过");
        }
    }
}
