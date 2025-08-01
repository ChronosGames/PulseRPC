using NBomber.Contracts;
using NBomber.CSharp;
using GameApp.SystemTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;
using System.Text.Json;
using System.Net.Http.Json;
using FluentAssertions;

namespace GameApp.SystemTests.Performance
{
    /// <summary>
    /// 系统性能和负载测试
    /// </summary>
    [Collection("SystemTests")]
    public class LoadTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;

        public LoadTests(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task AuthServer_LoginLoad_ShouldHandleConcurrentRequests()
        {
            // Arrange
            _output.WriteLine("开始认证服务器负载测试");

            await _fixture.WaitForServicesReadyAsync();

            // 先创建一个测试用户
            using var setupClient = _fixture.CreateAuthServerClient();

            var registerRequest = new
            {
                Username = "loadtestuser",
                Password = "loadtest123",
                Email = "loadtest@example.com",
                DeviceId = "loadtest-device"
            };

            var registerResponse = await setupClient.PostAsJsonAsync("/api/auth/register", registerRequest);
            registerResponse.Should().BeSuccessful("测试用户注册应该成功");

            // 创建NBomber场景
            var scenario = Scenario.Create("auth_login_load", async context =>
            {
                var loginData = new
                {
                    Username = "loadtestuser",
                    Password = "loadtest123",
                    DeviceId = $"device-{context.ScenarioInfo.ThreadId}"
                };

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(_fixture.AuthServerUrl);

                var response = await httpClient.PostAsJsonAsync("/api/auth/login", loginData);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(content);

                    if (result.GetProperty("success").GetBoolean())
                    {
                        return Response.Ok();
                    }
                }

                return Response.Fail($"登录失败: {response.StatusCode}");
            })
            .WithLoadSimulations(
                Simulation.InjectPerSec(rate: 10, during: TimeSpan.FromSeconds(30)) // 每秒10个请求，持续30秒
            );

            // Act
            var stats = NBomberRunner
                .RegisterScenarios(scenario)
                .WithTestSuite("AuthServer负载测试")
                .WithTestName("登录API负载测试")
                .Run();

            // Assert
            var sceneStats = stats.AllScenarioStats.First();

            _output.WriteLine($"总请求数: {sceneStats.AllRequestCount}");
            _output.WriteLine($"成功请求数: {sceneStats.AllOkCount}");
            _output.WriteLine($"失败请求数: {sceneStats.AllFailCount}");
            _output.WriteLine($"平均响应时间: {sceneStats.Ok.Mean}ms");
            _output.WriteLine($"95%响应时间: {sceneStats.Ok.Percentile95}ms");
            _output.WriteLine($"99%响应时间: {sceneStats.Ok.Percentile99}ms");

            // 验证性能指标
            sceneStats.AllFailCount.Should().Be(0, "不应该有失败的请求");
            sceneStats.Ok.Mean.Should().BeLessThan(200, "平均响应时间应该少于200ms");
            sceneStats.Ok.Percentile95.Should().BeLessThan(500, "95%的请求响应时间应该少于500ms");

            _output.WriteLine("认证服务器负载测试通过");
        }

        [Fact]
        public async Task AuthServer_StressTest_ShouldMaintainPerformance()
        {
            // Arrange
            _output.WriteLine("开始认证服务器压力测试");

            await _fixture.WaitForServicesReadyAsync();

            // 准备多个测试用户
            using var setupClient = _fixture.CreateAuthServerClient();

            for (int i = 0; i < 10; i++)
            {
                var registerRequest = new
                {
                    Username = $"stressuser{i}",
                    Password = "stress123",
                    Email = $"stress{i}@example.com",
                    DeviceId = $"stress-device-{i}"
                };

                await setupClient.PostAsJsonAsync("/api/auth/register", registerRequest);
            }

            // 创建压力测试场景
            var scenario = Scenario.Create("auth_stress_test", async context =>
            {
                var userId = Random.Shared.Next(0, 10);
                var loginData = new
                {
                    Username = $"stressuser{userId}",
                    Password = "stress123",
                    DeviceId = $"stress-device-{userId}-{context.InvocationNumber}"
                };

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(_fixture.AuthServerUrl);

                try
                {
                    var response = await httpClient.PostAsJsonAsync("/api/auth/login", loginData);

                    if (response.IsSuccessStatusCode)
                    {
                        return Response.Ok();
                    }

                    return Response.Fail($"HTTP {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    return Response.Fail(ex.Message);
                }
            })
            .WithLoadSimulations(
                Simulation.InjectPerSec(rate: 5, during: TimeSpan.FromSeconds(10)),  // 预热
                Simulation.InjectPerSec(rate: 20, during: TimeSpan.FromSeconds(30)), // 逐步增加
                Simulation.InjectPerSec(rate: 50, during: TimeSpan.FromSeconds(30)), // 高负载
                Simulation.InjectPerSec(rate: 20, during: TimeSpan.FromSeconds(10))  // 降级
            );

            // Act
            var stats = NBomberRunner
                .RegisterScenarios(scenario)
                .WithTestSuite("AuthServer压力测试")
                .WithTestName("高并发登录压力测试")
                .Run();

            // Assert
            var sceneStats = stats.AllScenarioStats.First();

            _output.WriteLine($"压力测试结果:");
            _output.WriteLine($"总请求数: {sceneStats.AllRequestCount}");
            _output.WriteLine($"成功率: {(double)sceneStats.AllOkCount / sceneStats.AllRequestCount * 100:F2}%");
            _output.WriteLine($"平均响应时间: {sceneStats.Ok.Mean}ms");
            _output.WriteLine($"最大响应时间: {sceneStats.Ok.Max}ms");

            // 验证系统在压力下的表现
            var successRate = (double)sceneStats.AllOkCount / sceneStats.AllRequestCount;
            successRate.Should().BeGreaterThan(0.95, "成功率应该大于95%");
            sceneStats.Ok.Mean.Should().BeLessThan(1000, "即使在高负载下，平均响应时间也应该少于1秒");

            _output.WriteLine("认证服务器压力测试通过");
        }

        [Fact]
        public async Task AuthServer_EnduranceTest_ShouldMaintainStability()
        {
            // Arrange
            _output.WriteLine("开始认证服务器耐久性测试（时间较长）");

            await _fixture.WaitForServicesReadyAsync();

            // 注册耐久测试用户
            using var setupClient = _fixture.CreateAuthServerClient();

            var registerRequest = new
            {
                Username = "enduranceuser",
                Password = "endurance123",
                Email = "endurance@example.com",
                DeviceId = "endurance-device"
            };

            var registerResponse = await setupClient.PostAsJsonAsync("/api/auth/register", registerRequest);
            registerResponse.Should().BeSuccessful();

            // 创建耐久测试场景
            var scenario = Scenario.Create("auth_endurance_test", async context =>
            {
                var loginData = new
                {
                    Username = "enduranceuser",
                    Password = "endurance123",
                    DeviceId = $"endurance-{context.InvocationNumber}"
                };

                using var httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(_fixture.AuthServerUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                try
                {
                    var response = await httpClient.PostAsJsonAsync("/api/auth/login", loginData);
                    return response.IsSuccessStatusCode ? Response.Ok() : Response.Fail();
                }
                catch (Exception ex)
                {
                    return Response.Fail(ex.Message);
                }
            })
            .WithLoadSimulations(
                Simulation.InjectPerSec(rate: 2, during: TimeSpan.FromMinutes(2)) // 持续2分钟的低负载测试
            );

            // Act
            var stats = NBomberRunner
                .RegisterScenarios(scenario)
                .WithTestSuite("AuthServer耐久测试")
                .WithTestName("长时间稳定性测试")
                .Run();

            // Assert
            var sceneStats = stats.AllScenarioStats.First();

            _output.WriteLine($"耐久测试结果:");
            _output.WriteLine($"测试时长: 2分钟");
            _output.WriteLine($"总请求数: {sceneStats.AllRequestCount}");
            _output.WriteLine($"成功率: {(double)sceneStats.AllOkCount / sceneStats.AllRequestCount * 100:F2}%");

            // 验证系统长时间运行的稳定性
            var successRate = (double)sceneStats.AllOkCount / sceneStats.AllRequestCount;
            successRate.Should().BeGreaterThan(0.99, "长时间运行的成功率应该大于99%");

            _output.WriteLine("认证服务器耐久性测试通过");
        }

        [Fact]
        public async Task AuthServer_MemoryUsage_ShouldRemainStable()
        {
            // Arrange
            _output.WriteLine("开始内存使用监控测试");

            await _fixture.WaitForServicesReadyAsync();

            var initialMemory = GC.GetTotalMemory(false);
            _output.WriteLine($"初始内存使用: {initialMemory / 1024 / 1024:F2} MB");

            // 执行大量请求以测试内存泄露
            using var client = _fixture.CreateAuthServerClient();

            for (int i = 0; i < 100; i++)
            {
                var loginRequest = new
                {
                    Username = $"memtestuser{i}",
                    Password = "memtest123",
                    DeviceId = $"memtest-device-{i}"
                };

                try
                {
                    await client.PostAsJsonAsync("/api/auth/login", loginRequest);
                }
                catch
                {
                    // 忽略失败，我们主要关注内存使用
                }

                if (i % 20 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    var currentMemory = GC.GetTotalMemory(false);
                    _output.WriteLine($"第{i}次请求后内存使用: {currentMemory / 1024 / 1024:F2} MB");
                }
            }

            // 强制垃圾回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = (finalMemory - initialMemory) / 1024.0 / 1024.0;

            _output.WriteLine($"最终内存使用: {finalMemory / 1024 / 1024:F2} MB");
            _output.WriteLine($"内存增长: {memoryIncrease:F2} MB");

            // Assert
            memoryIncrease.Should().BeLessThan(50, "内存增长应该少于50MB，表明没有严重的内存泄露");

            _output.WriteLine("内存使用监控测试通过");
        }
    }
}
