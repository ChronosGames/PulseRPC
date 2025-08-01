using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using PulseRPC.Client;
using GameApp.AuthServer;
using GameApp.Shared.Services;

namespace GameApp.Integration.Tests;

/// <summary>
/// 端到端登录流程集成测试
/// </summary>
public class EndToEndLoginTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly HttpClient _authClient;

    public EndToEndLoginTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _authClient = _fixture.AuthServerFactory.CreateClient();
    }

    [Fact]
    public async Task CompleteLoginFlow_ShouldWork()
    {
        // 1. 用户注册
        var registerRequest = new
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "testpassword",
            ConfirmPassword = "testpassword",
            AgreementAccepted = true
        };

        var registerJson = JsonSerializer.Serialize(registerRequest);
        var registerContent = new StringContent(registerJson, Encoding.UTF8, "application/json");

        var registerResponse = await _authClient.PostAsync("/api/auth/register", registerContent);
        registerResponse.Should().BeSuccessful();

        // 2. 用户登录 AuthServer
        var loginRequest = new
        {
            Username = "testuser",
            Password = "testpassword",
            DeviceId = "test-device-001",
            DeviceInfo = new
            {
                Platform = "Unity",
                Version = "1.0.0",
                UnityVersion = "2022.3.0f1"
            }
        };

        var loginJson = JsonSerializer.Serialize(loginRequest);
        var loginContent = new StringContent(loginJson, Encoding.UTF8, "application/json");

        var loginResponse = await _authClient.PostAsync("/api/auth/login", loginContent);
        loginResponse.Should().BeSuccessful();

        var loginResult = await loginResponse.Content.ReadAsStringAsync();
        var loginData = JsonSerializer.Deserialize<AuthApiResponse>(loginResult);

        loginData.Should().NotBeNull();
        loginData!.Success.Should().BeTrue();
        loginData.Data.Should().NotBeNull();
        loginData.Data.AccessToken.Should().NotBeEmpty();
        loginData.Data.GameTicket.Should().NotBeEmpty();

        // 3. 连接 GameServer (模拟)
        // 注意：这里需要实际的 GameServer 实例，或者使用 Mock
        // 在实际测试中，我们会启动 GameServer 实例

        var gameTicket = loginData.Data.GameTicket;
        gameTicket.Should().NotBeEmpty();

        // 4. 验证游戏票据有效性（通过再次调用验证接口）
        _authClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {loginData.Data.AccessToken}");
        var validateResponse = await _authClient.GetAsync("/api/auth/validate-token");
        validateResponse.Should().BeSuccessful();
    }

    [Fact]
    public async Task GetZoneList_ShouldReturnZones()
    {
        // 获取区服列表
        var zonesResponse = await _authClient.GetAsync("/api/zone/list");
        zonesResponse.Should().BeSuccessful();

        var zonesResult = await zonesResponse.Content.ReadAsStringAsync();
        var zonesData = JsonSerializer.Deserialize<ZoneListApiResponse>(zonesResult);

        zonesData.Should().NotBeNull();
        zonesData!.Success.Should().BeTrue();
        zonesData.Data.Should().NotBeNull();
        zonesData.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvalidLogin_ShouldFail()
    {
        // 测试无效登录
        var invalidLoginRequest = new
        {
            Username = "nonexistentuser",
            Password = "wrongpassword",
            DeviceId = "test-device-002",
            DeviceInfo = new
            {
                Platform = "Unity",
                Version = "1.0.0"
            }
        };

        var json = JsonSerializer.Serialize(invalidLoginRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _authClient.PostAsync("/api/auth/login", content);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }
}

/// <summary>
/// 集成测试基础设置
/// </summary>
public class IntegrationTestFixture : IAsyncDisposable
{
    public WebApplicationFactory<Program> AuthServerFactory { get; private set; }

    // 注意：在实际项目中，这里还需要启动 GameServer 实例
    // public IHost GameServerHost { get; private set; }

    public IntegrationTestFixture()
    {
        // 创建 AuthServer 测试工厂
        AuthServerFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // 这里可以替换测试用的数据库连接等
                    // 例如：使用内存数据库或测试容器
                });
            });
    }

    public async ValueTask DisposeAsync()
    {
        AuthServerFactory?.Dispose();
        // GameServerHost?.Dispose();
    }
}

// 响应数据模型
public class AuthApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public AuthData Data { get; set; } = new();
}

public class AuthData
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string GameTicket { get; set; } = string.Empty;
    public UserData User { get; set; } = new();
}

public class UserData
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ZoneListApiResponse
{
    public bool Success { get; set; }
    public List<ZoneData> Data { get; set; } = new();
}

public class ZoneData
{
    public string ZoneId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
