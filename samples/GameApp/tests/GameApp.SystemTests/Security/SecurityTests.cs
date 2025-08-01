using FluentAssertions;
using GameApp.SystemTests.Infrastructure;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using System.Net;

namespace GameApp.SystemTests.Security
{
    /// <summary>
    /// 系统安全测试 - 测试各种安全漏洞和攻击防护
    /// </summary>
    [Collection("SystemTests")]
    public class SecurityTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _authClient;

        public SecurityTests(TestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _authClient = _fixture.CreateAuthServerClient();
        }

        [Theory]
        [InlineData("'; DROP TABLE Users; --", "password123")]
        [InlineData("admin' OR '1'='1", "anything")]
        [InlineData("' UNION SELECT * FROM Users --", "password")]
        [InlineData("admin'; DELETE FROM Users; --", "test")]
        public async Task Authentication_SqlInjectionAttempts_ShouldBeRejected(string maliciousUsername, string password)
        {
            // Arrange
            _output.WriteLine($"测试SQL注入攻击: {maliciousUsername}");

            await _fixture.WaitForServicesReadyAsync();

            var loginRequest = new
            {
                Username = maliciousUsername,
                Password = password,
                DeviceId = "test-device"
            };

            // Act
            var response = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);

            // Assert
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                result.GetProperty("success").GetBoolean().Should().BeFalse("SQL注入攻击应该被拒绝");
            }
            else
            {
                response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
            }

            _output.WriteLine($"SQL注入攻击被成功阻止: {maliciousUsername}");
        }

        [Theory]
        [InlineData("<script>alert('XSS')</script>")]
        [InlineData("<img src=x onerror=alert('XSS')>")]
        [InlineData("javascript:alert('XSS')")]
        [InlineData("<iframe src='javascript:alert(\"XSS\")'></iframe>")]
        public async Task Registration_XssAttempts_ShouldBeSanitized(string maliciousInput)
        {
            // Arrange
            _output.WriteLine($"测试XSS攻击: {maliciousInput}");

            await _fixture.WaitForServicesReadyAsync();

            var registerRequest = new
            {
                Username = $"user{Guid.NewGuid():N}",
                Password = "validpassword123",
                Email = maliciousInput, // 在email字段注入XSS
                DeviceId = "test-device"
            };

            // Act
            var response = await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();

                // 如果注册成功，检查返回的数据是否已经被净化
                if (result.TryGetProperty("user", out var user) && user.TryGetProperty("email", out var email))
                {
                    var emailValue = email.GetString();
                    emailValue.Should().NotContain("<script>", "XSS脚本应该被过滤");
                    emailValue.Should().NotContain("javascript:", "JavaScript协议应该被过滤");
                    emailValue.Should().NotContain("onerror=", "事件处理器应该被过滤");
                }
            }
            else
            {
                // 输入验证应该拒绝恶意输入
                response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
            }

            _output.WriteLine($"XSS攻击被成功防护: {maliciousInput}");
        }

        [Fact]
        public async Task Authentication_BruteForceAttack_ShouldBeThrottled()
        {
            // Arrange
            _output.WriteLine("测试暴力破解攻击防护");

            await _fixture.WaitForServicesReadyAsync();

            // 先注册一个测试用户
            var registerRequest = new
            {
                Username = "bruteforcetest",
                Password = "correctpassword123",
                Email = "bruteforce@example.com",
                DeviceId = "bruteforce-device"
            };

            await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            var successfulAttempts = 0;
            var throttledAttempts = 0;

            // Act - 尝试多次错误登录
            for (int i = 0; i < 10; i++)
            {
                var loginRequest = new
                {
                    Username = "bruteforcetest",
                    Password = $"wrongpassword{i}",
                    DeviceId = "bruteforce-device"
                };

                var response = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throttledAttempts++;
                    _output.WriteLine($"第{i + 1}次尝试被限流");
                }
                else if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (result.GetProperty("success").GetBoolean())
                    {
                        successfulAttempts++;
                    }
                }

                // 短暂延迟
                await Task.Delay(200);
            }

            // Assert
            successfulAttempts.Should().Be(0, "不应该有成功的暴力破解尝试");
            throttledAttempts.Should().BeGreaterThan(0, "应该有请求被限流");

            _output.WriteLine($"暴力破解防护测试通过: {throttledAttempts}个请求被限流");
        }

        [Fact]
        public async Task JWT_TokenManipulation_ShouldBeRejected()
        {
            // Arrange
            _output.WriteLine("测试JWT令牌篡改检测");

            await _fixture.WaitForServicesReadyAsync();

            // 先正常登录获取令牌
            var registerRequest = new
            {
                Username = "jwttest",
                Password = "jwttest123",
                Email = "jwt@example.com",
                DeviceId = "jwt-device"
            };

            await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            var loginRequest = new
            {
                Username = "jwttest",
                Password = "jwttest123",
                DeviceId = "jwt-device"
            };

            var loginResponse = await _authClient.PostAsJsonAsync("/api/auth/login", loginRequest);
            var loginResult = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
            var originalToken = loginResult.GetProperty("accessToken").GetString()!;

            // Act & Assert - 测试各种令牌篡改
            var manipulatedTokens = new[]
            {
                originalToken + "extra", // 添加额外字符
                originalToken.Substring(0, originalToken.Length - 5) + "aaaaa", // 修改签名
                "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhZG1pbiIsImlhdCI6MTYxNjIzOTAyMn0.invalid", // 完全伪造的令牌
                originalToken.Replace('A', 'B'), // 修改payload
            };

            foreach (var manipulatedToken in manipulatedTokens)
            {
                _authClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", manipulatedToken);

                var response = await _authClient.GetAsync("/api/zones");

                response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

                _output.WriteLine("JWT令牌篡改被成功检测");
            }
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("administrator")]
        [InlineData("root")]
        [InlineData("system")]
        [InlineData("sa")]
        public async Task Registration_ReservedUsernames_ShouldBeRejected(string reservedUsername)
        {
            // Arrange
            _output.WriteLine($"测试保留用户名: {reservedUsername}");

            await _fixture.WaitForServicesReadyAsync();

            var registerRequest = new
            {
                Username = reservedUsername,
                Password = "password123",
                Email = $"{reservedUsername}@example.com",
                DeviceId = "test-device"
            };

            // Act
            var response = await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

            // Assert
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                result.GetProperty("success").GetBoolean().Should().BeFalse("保留用户名应该被拒绝");
            }
            else
            {
                response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
            }

            _output.WriteLine($"保留用户名被成功拒绝: {reservedUsername}");
        }

        [Fact]
        public async Task PasswordSecurity_WeakPasswords_ShouldBeRejected()
        {
            // Arrange
            _output.WriteLine("测试弱密码检测");

            await _fixture.WaitForServicesReadyAsync();

            var weakPasswords = new[]
            {
                "123",          // 太短
                "password",     // 常用密码
                "123456",       // 数字序列
                "abc",          // 太短且简单
                "aaa",          // 重复字符
            };

            foreach (var weakPassword in weakPasswords)
            {
                var registerRequest = new
                {
                    Username = $"user{Guid.NewGuid():N}",
                    Password = weakPassword,
                    Email = "test@example.com",
                    DeviceId = "test-device"
                };

                // Act
                var response = await _authClient.PostAsJsonAsync("/api/auth/register", registerRequest);

                // Assert
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    result.GetProperty("success").GetBoolean().Should().BeFalse($"弱密码应该被拒绝: {weakPassword}");
                }
                else
                {
                    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"弱密码应该返回400错误: {weakPassword}");
                }

                _output.WriteLine($"弱密码被成功拒绝: {weakPassword}");
            }
        }

        [Fact]
        public async Task DataTransmission_ShouldUseSecureHeaders()
        {
            // Arrange
            _output.WriteLine("测试安全响应头");

            await _fixture.WaitForServicesReadyAsync();

            // Act
            var response = await _authClient.GetAsync("/ready");

            // Assert
            response.IsSuccessStatusCode.Should().BeTrue();

            // 检查安全相关的响应头
            var headers = response.Headers.Concat(response.Content.Headers);

            // 虽然在测试环境中可能不会设置所有安全头，但我们可以检查一些基本的
            _output.WriteLine("响应头检查:");
            foreach (var header in headers)
            {
                _output.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }

            // 检查是否没有暴露敏感信息
            var serverHeader = headers.FirstOrDefault(h => h.Key.Equals("Server", StringComparison.OrdinalIgnoreCase));
            if (serverHeader.Key != null)
            {
                var serverValue = string.Join("", serverHeader.Value);
                serverValue.Should().NotContain("Microsoft", "不应该暴露服务器详细信息");
            }

            _output.WriteLine("安全响应头检查通过");
        }

        [Fact]
        public async Task InputValidation_MalformedJson_ShouldBeHandledGracefully()
        {
            // Arrange
            _output.WriteLine("测试畸形JSON处理");

            await _fixture.WaitForServicesReadyAsync();

            var malformedJsons = new[]
            {
                "{invalid json}",
                "{\"username\":}",
                "{\"username\":\"test\",\"password\":}",
                "{\"username\":\"test\",\"password\":\"pass\",}",
                "not json at all",
                "",
                "null"
            };

            foreach (var malformedJson in malformedJsons)
            {
                // Act
                var content = new StringContent(malformedJson, Encoding.UTF8, "application/json");
                var response = await _authClient.PostAsync("/api/auth/login", content);

                // Assert
                response.StatusCode.Should().BeOneOf(
                    HttpStatusCode.BadRequest,
                    HttpStatusCode.UnsupportedMediaType);

                // 确保服务器不会崩溃
                var errorResponse = await _authClient.GetAsync("/ready");
                errorResponse.IsSuccessStatusCode.Should().BeTrue("服务器应该仍然正常运行");
            }

            _output.WriteLine("畸形JSON处理测试通过");
        }

        [Fact]
        public async Task RateLimiting_MultipleIPs_ShouldBeIndependent()
        {
            // Arrange
            _output.WriteLine("测试多IP限流独立性");

            await _fixture.WaitForServicesReadyAsync();

            // 这是一个简化的测试，实际环境中IP限流需要反向代理支持
            // 这里我们主要测试基于用户的限流是否工作

            var tasks = new List<Task<bool>>();

            // 创建多个并发请求
            for (int i = 0; i < 5; i++)
            {
                int userId = i;
                tasks.Add(Task.Run(async () =>
                {
                    using var client = _fixture.CreateAuthServerClient();

                    var loginRequest = new
                    {
                        Username = $"ratelimituser{userId}",
                        Password = "password123",
                        DeviceId = $"device{userId}"
                    };

                    var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
                    return response.StatusCode != HttpStatusCode.TooManyRequests;
                }));
            }

            // Act
            var results = await Task.WhenAll(tasks);

            // Assert
            // 至少应该有一些请求成功（表明不是所有请求都被限流）
            results.Should().Contain(true, "不同用户的请求不应该互相影响");

            _output.WriteLine($"多用户限流测试完成: {results.Count(r => r)}/{results.Length} 请求成功");
        }
    }
}
