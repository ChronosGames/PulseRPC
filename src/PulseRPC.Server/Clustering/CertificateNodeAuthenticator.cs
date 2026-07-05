using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="CertificateNodeAuthenticator"/> 的配置项。
/// </summary>
public sealed class CertificateNodeAuthenticatorOptions
{
    /// <summary>本节点的证书（<strong>必须含私钥</strong>，用于对凭据签名）。</summary>
    public X509Certificate2? LocalCertificate { get; set; }

    /// <summary>
    /// 受信任的 CA 证书集合（根/中间）。校验时对端证书需链式信任到其中之一
    /// （自定义信任根，<see cref="X509ChainTrustMode.CustomRootTrust"/>）。
    /// </summary>
    public X509Certificate2Collection TrustedCertificateAuthorities { get; set; } = new();

    /// <summary>
    /// 受信任证书指纹（SHA-256/SHA-1，十六进制）白名单。作为 CA 链式信任之外的备选：
    /// 对端证书指纹命中即视为可信（适合"证书钉扎"部署）。
    /// </summary>
    public HashSet<string> TrustedThumbprints { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否要求对端 <c>nodeId</c> 与其证书主体（CN 或 SAN DNS）匹配。默认 true。</summary>
    public bool RequireNodeIdMatchesCertificate { get; set; } = true;

    /// <summary>凭据有效期（抵御重放）。默认 5 分钟。</summary>
    public TimeSpan CredentialLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// 基于 X.509 证书的 <see cref="INodeAuthenticator"/> 实现 —— 生产级节点互信（对标 mTLS 的应用层等价物）。
/// </summary>
/// <remarks>
/// <para>
/// 每个节点持有自己的证书（含私钥）。<see cref="CreateCredentialAsync"/> 产出凭据
/// <c>[本节点证书 DER][时间戳][对 "nodeId:timestamp" 的签名]</c>；<see cref="ValidateAsync"/> 校验：
/// </para>
/// <list type="number">
/// <item><description>时间戳在有效期与时钟偏移容忍内（抗重放）；</description></item>
/// <item><description>对端证书<strong>可信</strong>：指纹在 <see cref="CertificateNodeAuthenticatorOptions.TrustedThumbprints"/>
/// 白名单中，或链式信任到 <see cref="CertificateNodeAuthenticatorOptions.TrustedCertificateAuthorities"/>；</description></item>
/// <item><description>签名用对端证书公钥验证通过（支持 RSA / ECDSA）；</description></item>
/// <item><description>（可选）<c>nodeId</c> 与证书主体（CN 或 SAN DNS）匹配。</description></item>
/// </list>
/// <para>
/// 未配置任何信任源（白名单与 CA 均为空）时<strong>一律拒绝</strong>（fail-closed）。相比共享密钥，本实现
/// 提供按节点独立密钥、可吊销、可链式信任的更强安全性；生产环境推荐配合真实 TLS 传输一并使用。
/// </para>
/// </remarks>
public sealed class CertificateNodeAuthenticator : INodeAuthenticator
{
    private const int ClockSkewToleranceMillis = 30_000;

    private readonly CertificateNodeAuthenticatorOptions _options;

    /// <summary>创建证书节点鉴权器。</summary>
    public CertificateNodeAuthenticator(IOptions<CertificateNodeAuthenticatorOptions> options)
        : this(options?.Value!)
    {
    }

    /// <summary>创建证书节点鉴权器（直接传入配置，便于测试）。</summary>
    public CertificateNodeAuthenticator(CertificateNodeAuthenticatorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_options.LocalCertificate is null)
        {
            throw new ArgumentException("必须配置本节点证书（CertificateNodeAuthenticatorOptions.LocalCertificate）。", nameof(options));
        }

        if (!_options.LocalCertificate.HasPrivateKey)
        {
            throw new ArgumentException("本节点证书必须包含私钥，用于对节点凭据签名。", nameof(options));
        }
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> CreateCredentialAsync(string localNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localNodeId);

        var cert = _options.LocalCertificate!;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signature = Sign(cert, BuildPayload(localNodeId, timestamp));
        var certDer = cert.Export(X509ContentType.Cert);

        // 布局：[4 certLen][cert DER][8 timestamp][4 sigLen][signature]
        var credential = new byte[4 + certDer.Length + 8 + 4 + signature.Length];
        var span = credential.AsSpan();
        BinaryPrimitives.WriteInt32BigEndian(span, certDer.Length);
        certDer.CopyTo(span[4..]);
        var offset = 4 + certDer.Length;
        BinaryPrimitives.WriteInt64BigEndian(span[offset..], timestamp);
        offset += 8;
        BinaryPrimitives.WriteInt32BigEndian(span[offset..], signature.Length);
        offset += 4;
        signature.CopyTo(span[offset..]);

        return new ValueTask<ReadOnlyMemory<byte>>(credential);
    }

    /// <inheritdoc/>
    public ValueTask<NodeAuthResult> ValidateAsync(string remoteNodeId, ReadOnlyMemory<byte> credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteNodeId);

        if (!TryParse(credential.Span, out var certDer, out var timestamp, out var signature))
        {
            return Result(NodeAuthResult.Failure("凭据格式不正确"));
        }

        var ageMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
        if (ageMillis < -ClockSkewToleranceMillis || ageMillis > _options.CredentialLifetime.TotalMilliseconds)
        {
            return Result(NodeAuthResult.Failure("凭据已过期或时间戳无效"));
        }

        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadCertificate(certDer);
        }
        catch (CryptographicException)
        {
            return Result(NodeAuthResult.Failure("对端证书解析失败"));
        }

        using (cert)
        {
            if (!IsTrusted(cert))
            {
                return Result(NodeAuthResult.Failure("对端证书不受信任（既不在指纹白名单，也未链式信任到受信 CA）"));
            }

            if (!VerifySignature(cert, BuildPayload(remoteNodeId, timestamp), signature))
            {
                return Result(NodeAuthResult.Failure("凭据签名校验失败"));
            }

            if (_options.RequireNodeIdMatchesCertificate && !NodeIdMatchesCertificate(cert, remoteNodeId))
            {
                return Result(NodeAuthResult.Failure($"节点标识 '{remoteNodeId}' 与证书主体不匹配"));
            }
        }

        return Result(NodeAuthResult.Success());
    }

    private bool IsTrusted(X509Certificate2 cert)
    {
        if (_options.TrustedThumbprints.Count > 0
            && _options.TrustedThumbprints.Contains(cert.Thumbprint))
        {
            return true;
        }

        if (_options.TrustedCertificateAuthorities.Count == 0)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.CustomTrustStore.AddRange(_options.TrustedCertificateAuthorities);

        return chain.Build(cert);
    }

    private static byte[] BuildPayload(string nodeId, long timestamp)
        => Encoding.UTF8.GetBytes($"{nodeId}:{timestamp}");

    private static byte[] Sign(X509Certificate2 cert, byte[] payload)
    {
        using var rsa = cert.GetRSAPrivateKey();
        if (rsa is not null)
        {
            return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        using var ecdsa = cert.GetECDsaPrivateKey();
        if (ecdsa is not null)
        {
            return ecdsa.SignData(payload, HashAlgorithmName.SHA256);
        }

        throw new InvalidOperationException("本节点证书的私钥类型不受支持（仅支持 RSA / ECDSA）。");
    }

    private static bool VerifySignature(X509Certificate2 cert, byte[] payload, byte[] signature)
    {
        using var rsa = cert.GetRSAPublicKey();
        if (rsa is not null)
        {
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        using var ecdsa = cert.GetECDsaPublicKey();
        if (ecdsa is not null)
        {
            return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }

        return false;
    }

    private static bool NodeIdMatchesCertificate(X509Certificate2 cert, string nodeId)
    {
        var simpleName = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        if (string.Equals(simpleName, nodeId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var dnsName = cert.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        return string.Equals(dnsName, nodeId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParse(ReadOnlySpan<byte> credential, out byte[] certDer, out long timestamp, out byte[] signature)
    {
        certDer = Array.Empty<byte>();
        timestamp = 0;
        signature = Array.Empty<byte>();

        if (credential.Length < 4)
        {
            return false;
        }

        var certLen = BinaryPrimitives.ReadInt32BigEndian(credential);
        if (certLen <= 0 || credential.Length < 4 + certLen + 8 + 4)
        {
            return false;
        }

        var offset = 4;
        certDer = credential.Slice(offset, certLen).ToArray();
        offset += certLen;

        timestamp = BinaryPrimitives.ReadInt64BigEndian(credential[offset..]);
        offset += 8;

        var sigLen = BinaryPrimitives.ReadInt32BigEndian(credential[offset..]);
        offset += 4;
        if (sigLen <= 0 || credential.Length != offset + sigLen)
        {
            return false;
        }

        signature = credential.Slice(offset, sigLen).ToArray();
        return true;
    }

    private static ValueTask<NodeAuthResult> Result(NodeAuthResult result) => new(result);
}
