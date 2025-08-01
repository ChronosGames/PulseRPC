using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using FluentAssertions;
using GameApp.AuthServer.Services;
using GameApp.AuthServer.Models;
using GameApp.AuthServer.Configuration;

namespace GameApp.AuthServer.Tests.Services;

/// <summary>
/// JWT Token 服务测试
/// </summary>
public class JwtTokenServiceTests
{
    private readonly Mock<ILogger<JwtTokenService>> _mockLogger;
    private readonly JwtOptions _jwtOptions;
    private readonly JwtTokenService _tokenService;

    public JwtTokenServiceTests()
    {
        _mockLogger = new Mock<ILogger<JwtTokenService>>();

        _jwtOptions = new JwtOptions
        {
            SecretKey = "test-secret-key-for-unit-testing-1234567890abcdef",
            Issuer = "TestGameApp",
            Audience = "TestGameApp.Client",
            AccessTokenExpirationMinutes = 60,
            RefreshTokenExpirationDays = 30
        };

        var mockOptions = new Mock<IOptions<JwtOptions>>();
        mockOptions.Setup(x => x.Value).Returns(_jwtOptions);

        _tokenService = new JwtTokenService(mockOptions.Object, _mockLogger.Object);
    }

    [Fact]
    public void GenerateJwtToken_ValidUser_ShouldReturnValidToken()
    {
        // Arrange
        var user = CreateTestUser();

        // Act
        var token = _tokenService.GenerateJwtToken(user);

        // Assert
        token.Should().NotBeNullOrEmpty();

        // 验证Token可以被正确解析
        var validationResult = _tokenService.ValidateJwtToken(token);
        validationResult.IsValid.Should().BeTrue();
        validationResult.UserClaims.Should().NotBeNull();
        validationResult.UserClaims!.UserId.Should().Be(user.UserId);
        validationResult.UserClaims.Username.Should().Be(user.Username);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnUniqueTokens()
    {
        // Act
        var token1 = _tokenService.GenerateRefreshToken();
        var token2 = _tokenService.GenerateRefreshToken();

        // Assert
        token1.Should().NotBeNullOrEmpty();
        token2.Should().NotBeNullOrEmpty();
        token1.Should().NotBe(token2);
    }

    [Fact]
    public void ValidateJwtToken_ValidToken_ShouldReturnValidResult()
    {
        // Arrange
        var user = CreateTestUser();
        var token = _tokenService.GenerateJwtToken(user);

        // Act
        var result = _tokenService.ValidateJwtToken(token);

        // Assert
        result.IsValid.Should().BeTrue();
        result.UserClaims.Should().NotBeNull();
        result.UserClaims!.UserId.Should().Be(user.UserId);
        result.UserClaims.Username.Should().Be(user.Username);
        result.UserClaims.Email.Should().Be(user.Email);
    }

    [Fact]
    public void ValidateJwtToken_InvalidToken_ShouldReturnInvalidResult()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";

        // Act
        var result = _tokenService.ValidateJwtToken(invalidToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Message.Should().Be("Invalid token");
    }

    [Fact]
    public void ValidateJwtToken_ExpiredToken_ShouldReturnExpiredResult()
    {
        // Arrange - 创建过期的Token配置
        var expiredOptions = new JwtOptions
        {
            SecretKey = _jwtOptions.SecretKey,
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            AccessTokenExpirationMinutes = -1 // 已过期
        };

        var mockExpiredOptions = new Mock<IOptions<JwtOptions>>();
        mockExpiredOptions.Setup(x => x.Value).Returns(expiredOptions);

        var expiredTokenService = new JwtTokenService(mockExpiredOptions.Object, _mockLogger.Object);
        var user = CreateTestUser();
        var expiredToken = expiredTokenService.GenerateJwtToken(user);

        // 等待一小段时间确保Token过期
        Thread.Sleep(100);

        // Act
        var result = _tokenService.ValidateJwtToken(expiredToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Message.Should().Be("Token has expired");
    }

    [Fact]
    public void ExtractUserClaims_ValidToken_ShouldReturnCorrectClaims()
    {
        // Arrange
        var user = CreateTestUser();
        var token = _tokenService.GenerateJwtToken(user);

        // Act
        var claims = _tokenService.ExtractUserClaims(token);

        // Assert
        claims.Should().NotBeNull();
        claims!.UserId.Should().Be(user.UserId);
        claims.Username.Should().Be(user.Username);
        claims.Email.Should().Be(user.Email);
    }

    [Fact]
    public void ExtractUserClaims_InvalidToken_ShouldReturnNull()
    {
        // Arrange
        var invalidToken = "invalid.jwt.token";

        // Act
        var claims = _tokenService.ExtractUserClaims(invalidToken);

        // Assert
        claims.Should().BeNull();
    }

    private User CreateTestUser()
    {
        return new User
        {
            Id = "507f1f77bcf86cd799439011",
            UserId = 1001,
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hashed_password",
            Salt = "test_salt",
            Status = "active",
            Profile = new UserProfile
            {
                Nickname = "Test User",
                Level = 10,
                VipLevel = 1,
                Language = "zh-CN"
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
