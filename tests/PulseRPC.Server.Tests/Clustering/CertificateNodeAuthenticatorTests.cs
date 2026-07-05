using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PulseRPC.Server.Clustering;
using Xunit;

namespace PulseRPC.Server.Tests.Clustering;

/// <summary>
/// P8：<see cref="CertificateNodeAuthenticator"/> —— 基于 X.509 证书的生产级节点互信
/// （签名/验签、指纹白名单、CA 链式信任、nodeId 匹配、过期/重放、fail-closed）。
/// </summary>
public class CertificateNodeAuthenticatorTests
{
    private static X509Certificate2 CreateSelfSignedCert(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // 确保私钥可导出/可用（在部分平台上 CreateSelfSigned 已附带私钥）。
        return cert;
    }

    private static (X509Certificate2 ca, X509Certificate2 leaf) CreateCaAndLeaf(string caName, string leafName)
    {
        using var caRsa = RSA.Create(2048);
        var caRequest = new CertificateRequest($"CN={caName}", caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));

        using var leafRsa = RSA.Create(2048);
        var leafRequest = new CertificateRequest($"CN={leafName}", leafRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        var leafPublic = leafRequest.Create(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), serial);
        var leaf = leafPublic.CopyWithPrivateKey(leafRsa);
        leafPublic.Dispose();
        return (ca, leaf);
    }

    private static CertificateNodeAuthenticator Create(CertificateNodeAuthenticatorOptions options)
        => new(Options.Create(options));

    [Fact]
    public async Task ThumbprintAllowlist_ValidCredential_Succeeds()
    {
        using var cert = CreateSelfSignedCert("node-a");
        var auth = Create(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = cert,
            TrustedThumbprints = { cert.Thumbprint },
            RequireNodeIdMatchesCertificate = true,
        });

        var credential = await auth.CreateCredentialAsync("node-a");
        var result = await auth.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeTrue(result.FailureReason);
    }

    [Fact]
    public async Task CaChainTrust_LeafSignedByTrustedCa_Succeeds()
    {
        var (ca, leaf) = CreateCaAndLeaf("PulseRPC-Test-CA", "node-b");
        using (ca)
        using (leaf)
        {
            var auth = Create(new CertificateNodeAuthenticatorOptions
            {
                LocalCertificate = leaf,
                TrustedCertificateAuthorities = { ca },
                RequireNodeIdMatchesCertificate = true,
            });

            var credential = await auth.CreateCredentialAsync("node-b");
            var result = await auth.ValidateAsync("node-b", credential);

            result.IsAuthenticated.Should().BeTrue(result.FailureReason);
        }
    }

    [Fact]
    public async Task UntrustedCertificate_FailsClosed()
    {
        using var certA = CreateSelfSignedCert("node-a");
        using var certAttacker = CreateSelfSignedCert("node-a"); // 不同的自签证书（不在白名单）

        // 校验方只信任 certA 的指纹；攻击者用自己的证书签名冒充 node-a。
        var attackerAuth = new CertificateNodeAuthenticator(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = certAttacker,
        });
        var forged = await attackerAuth.CreateCredentialAsync("node-a");

        var validator = Create(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = certA,
            TrustedThumbprints = { certA.Thumbprint },
        });

        var result = await validator.ValidateAsync("node-a", forged);
        result.IsAuthenticated.Should().BeFalse("攻击者证书不在信任集内，必须被拒绝");
    }

    [Fact]
    public async Task NoTrustSourceConfigured_FailsClosed()
    {
        using var cert = CreateSelfSignedCert("node-a");
        var auth = Create(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = cert,
            // 既无指纹白名单也无受信 CA
        });

        var credential = await auth.CreateCredentialAsync("node-a");
        var result = await auth.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeFalse("未配置任何信任源时必须一律拒绝（fail-closed）");
    }

    [Fact]
    public async Task NodeIdMismatch_Fails()
    {
        using var cert = CreateSelfSignedCert("node-a");
        var auth = Create(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = cert,
            TrustedThumbprints = { cert.Thumbprint },
            RequireNodeIdMatchesCertificate = true,
        });

        // 证书主体是 node-a，但对端自称 node-z：应因主体不匹配而拒绝。
        var credential = await auth.CreateCredentialAsync("node-z");
        var result = await auth.ValidateAsync("node-z", credential);

        result.IsAuthenticated.Should().BeFalse("nodeId 与证书主体不匹配时应拒绝");
    }

    [Fact]
    public async Task ExpiredCredential_Fails()
    {
        using var cert = CreateSelfSignedCert("node-a");
        var auth = Create(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = cert,
            TrustedThumbprints = { cert.Thumbprint },
            CredentialLifetime = TimeSpan.FromMilliseconds(50),
            RequireNodeIdMatchesCertificate = false,
        });

        var credential = await auth.CreateCredentialAsync("node-a");
        await Task.Delay(200);
        var result = await auth.ValidateAsync("node-a", credential);

        result.IsAuthenticated.Should().BeFalse("超过有效期的凭据应被拒绝（抗重放）");
    }

    [Fact]
    public void Constructor_WithoutPrivateKey_Throws()
    {
        using var certWithKey = CreateSelfSignedCert("node-a");
        // 导出为仅公钥证书（去掉私钥）。
        using var publicOnly = X509CertificateLoader.LoadCertificate(certWithKey.Export(X509ContentType.Cert));

        var act = () => new CertificateNodeAuthenticator(new CertificateNodeAuthenticatorOptions
        {
            LocalCertificate = publicOnly,
        });

        act.Should().Throw<ArgumentException>();
    }
}
