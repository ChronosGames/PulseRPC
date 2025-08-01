using FluentAssertions;
using GameApp.SystemTests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging;
// using GameApp.Shared.DTOs.Auth; // 暂时注释掉，使用 JsonElement 代替

namespace GameApp.SystemTests.EndToEnd
{
    /// <summary>
    /// 完整登录流程端到端测试
    /// </summary>
    [Collection("SystemTests")]
    public class CompleteLoginFlowTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _authClient;

        public CompleteLoginFlowTests(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _authClient = _fixture.CreateAuthServerClient();
        }

        [Fact]
        public async Task CompleteLoginFlow_ShouldSucceed()
        {
            // Arrange
            _output.WriteLine("开始完整登录流程测试");

            await _fixture.WaitForServicesReadyAsync();

            var testUser = new
            {
                Username = "testuser",
                Password = "testpass123",
                Email = "test@example.com"
            };

            // Act & Assert

            // 1. 用户注册
            _output.WriteLine("步骤1: 用户注册");

            var registerRequest = new
            {
                Username = testUser.Username,
                Password = testUser.Password,
                Email = testUser.Email,
                DeviceId = "test-device-001"
            };

            var registerResponse = await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);
            registerResponse.Should().BeSuccessful("用户注册应该成功");

            var registerResult = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
            registerResult.GetProperty("success").GetBoolean().Should().BeTrue("注册应该成功");

            _output.WriteLine($"注册成功: {registerResult}");

            // 2. 用户登录
            _output.WriteLine("步骤2: 用户登录");

            var loginRequest = new
            {
                Username = testUser.Username,
                Password = testUser.Password,
                DeviceId = "test-device-001"
            };

            var loginResponse = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);
            loginResponse.Should().BeSuccessful("用户登录应该成功");

            var loginResult = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
            loginResult.GetProperty("success").GetBoolean().Should().BeTrue("登录应该成功");

            var accessToken = loginResult.GetProperty("accessToken").GetString();
            var gameTicket = loginResult.GetProperty("gameTicket").GetString();

            accessToken.Should().NotBeNullOrEmpty("应该返回访问令牌");
            gameTicket.Should().NotBeNullOrEmpty("应该返回游戏票据");

            _output.WriteLine($"登录成功，获得访问令牌和游戏票据");

            // 3. 获取区服列表
            _output.WriteLine("步骤3: 获取区服列表");

            _authClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var zonesResponse = await _authClient.GetAsync("/api/zones");
            zonesResponse.Should().BeSuccessful("获取区服列表应该成功");

            var zonesResult = await zonesResponse.Content.ReadFromJsonAsync<JsonElement>();
            var zones = zonesResult.GetProperty("zones").EnumerateArray().ToList();

            zones.Should().NotBeEmpty("应该返回至少一个区服");

            _output.WriteLine($"获取到 {zones.Count} 个区服");

            // 4. 选择区服并生成游戏票据
            _output.WriteLine("步骤4: 选择区服");

            var selectedZone = zones.First();
            var zoneId = selectedZone.GetProperty("zoneId").GetString();

            var selectZoneRequest = new
            {
                ZoneId = zoneId
            };

            var selectZoneResponse = await _authClient.PostAsJsonAsync("/api/zones/select", selectZoneRequest);
            selectZoneResponse.Should().BeSuccessful("选择区服应该成功");

            var selectZoneResult = await selectZoneResponse.Content.ReadFromJsonAsync<JsonElement>();
            selectZoneResult.GetProperty("success").GetBoolean().Should().BeTrue("选择区服应该成功");

            var finalGameTicket = selectZoneResult.GetProperty("gameTicket").GetString();
            finalGameTicket.Should().NotBeNullOrEmpty("应该返回最终游戏票据");

            _output.WriteLine($"选择区服成功，获得最终游戏票据");

            // 5. 验证游戏票据
            _output.WriteLine("步骤5: 验证游戏票据");

            var validateRequest = new
            {
                GameTicket = finalGameTicket
            };

            var validateResponse = await _authClient.PostAsJsonAsync("/api/auth/validate-ticket", validateRequest);
            validateResponse.Should().BeSuccessful("验证游戏票据应该成功");

            var validateResult = await validateResponse.Content.ReadFromJsonAsync<JsonElement>();
            validateResult.GetProperty("success").GetBoolean().Should().BeTrue("游戏票据验证应该成功");

            var playerInfo = validateResult.GetProperty("playerInfo");
            playerInfo.GetProperty("username").GetString().Should().Be(testUser.Username);

            _output.WriteLine("游戏票据验证成功，完整登录流程测试通过");
        }

        [Theory]
        [InlineData("", "password123", "用户名不能为空")]
        [InlineData("user", "", "密码不能为空")]
        [InlineData("ab", "password123", "用户名长度不符合要求")]
        [InlineData("user", "123", "密码长度不符合要求")]
        public async Task LoginFlow_WithInvalidInput_ShouldFail(string username, string password, string expectedError)
        {
            // Arrange
            _output.WriteLine($"测试无效输入: {expectedError}");

            await _fixture.WaitForServicesReadyAsync();

            var loginRequest = new
            {
                Username = username,
                Password = password,
                DeviceId = "test-device-001"
            };

            // Act
            var response = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            response.IsSuccessStatusCode.Should().BeFalse($"无效输入应该导致登录失败: {expectedError}");

            _output.WriteLine($"无效输入测试通过: {expectedError}");
        }

        [Fact]
        public async Task LoginFlow_WithInvalidCredentials_ShouldFail()
        {
            // Arrange
            _output.WriteLine("测试无效的登录凭据");

            await _fixture.WaitForServicesReadyAsync();

            var loginRequest = new
            {
                Username = "nonexistentuser",
                Password = "wrongpassword",
                DeviceId = "test-device-001"
            };

            // Act
            var response = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                result.GetProperty("success").GetBoolean().Should().BeFalse("无效凭据应该导致登录失败");
            }
            else
            {
                response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized, "应该返回401未授权状态");
            }

            _output.WriteLine("无效凭据测试通过");
        }

        [Fact]
        public async Task LoginFlow_ConcurrentUsers_ShouldSucceed()
        {
            // Arrange
            _output.WriteLine("测试并发用户登录");

            await _fixture.WaitForServicesReadyAsync();

            const int concurrentUsers = 5;
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < concurrentUsers; i++)
            {
                int userId = i;
                tasks.Add(Task.Run(async () =>
                {
                    using var client = _fixture.CreateAuthServerClient();

                    // 注册用户
                    var registerRequest = new
                    {
                        Username = $"user{userId}",
                        Password = "password123",
                        Email = $"user{userId}@example.com",
                        DeviceId = $"device-{userId}"
                    };

                    var registerResponse = await client.PostAsJsonAsync("/api/auth/register", registerRequest);
                    var registerResult = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();

                    if (registerResponse.IsSuccessStatusCode && registerResult.GetProperty("success").GetBoolean())
                    {
                        // 登录用户
                        var loginRequest = new
                        {
                            Username = $"user{userId}",
                            Password = "password123",
                            DeviceId = $"device-{userId}"
                        };

                        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
                        loginResponse.Should().BeSuccessful($"用户{userId}登录应该成功");
                    }
                }));
            }

            // Assert
            await Task.WhenAll(tasks);

            _output.WriteLine($"并发用户登录测试通过: {concurrentUsers}个用户");
        }
    }
}
