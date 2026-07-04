using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// 回归测试：<see cref="SharedSecretNodeAuthenticator"/>（§P4 共享密钥 <c>INodeAuthenticator</c>）。
/// 覆盖凭据往返校验、密钥不一致、时间戳过期与签名篡改场景。
/// </summary>
public class SharedSecretNodeAuthenticatorTests
{
    private static SharedSecretNodeAuthenticator CreateAuthenticator(string secret = "cluster-shared-secret", TimeSpan? lifetime = null)
    {
        var options = new SharedSecretNodeAuthenticatorOptions
        {
            SharedSecret = secret,
            CredentialLifetime = lifetime ?? TimeSpan.FromMinutes(5),
        };
        return new SharedSecretNodeAuthenticator(Options.Create(options));
    }

    [Fact]
    public async Task ValidateAsync_WithMatchingSecretAndNodeId_MustSucceed()
    {
        var authenticator = CreateAuthenticator();

        var credential = await authenticator.CreateCredentialAsync("node-a");
        var result = await authenticator.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentNodeIdThanCredentialWasIssuedFor_MustFail()
    {
        var authenticator = CreateAuthenticator();

        var credential = await authenticator.CreateCredentialAsync("node-a");
        // 凭据的 MAC 绑定了签发时使用的 nodeId；用另一个 nodeId 校验必须失败（防止伪造身份）。
        var result = await authenticator.ValidateAsync("node-b", credential);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentSharedSecret_MustFail()
    {
        var issuer = CreateAuthenticator(secret: "secret-A");
        var validator = CreateAuthenticator(secret: "secret-B");

        var credential = await issuer.CreateCredentialAsync("node-a");
        var result = await validator.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_TamperedCredentialBytes_MustFail()
    {
        var authenticator = CreateAuthenticator();

        var credential = (await authenticator.CreateCredentialAsync("node-a")).ToArray();
        credential[credential.Length - 1] ^= 0xFF; // 篡改签名最后一个字节

        var result = await authenticator.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredCredential_MustFail()
    {
        var authenticator = CreateAuthenticator(lifetime: TimeSpan.FromMilliseconds(1));

        var credential = await authenticator.CreateCredentialAsync("node-a");
        await Task.Delay(TimeSpan.FromSeconds(1));

        var result = await authenticator.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MalformedCredentialLength_MustFail()
    {
        var authenticator = CreateAuthenticator();

        var result = await authenticator.ValidateAsync("node-a", new byte[] { 1, 2, 3 });

        result.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithoutSharedSecret_MustThrow()
    {
        var act = () => new SharedSecretNodeAuthenticator(Options.Create(new SharedSecretNodeAuthenticatorOptions()));

        act.Should().Throw<ArgumentException>();
    }
}
