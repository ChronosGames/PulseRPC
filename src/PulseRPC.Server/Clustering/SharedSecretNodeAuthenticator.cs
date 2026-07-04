using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PulseRPC.Clustering;

namespace PulseRPC.Server.Clustering;

/// <summary>
/// <see cref="SharedSecretNodeAuthenticator"/> 的配置项。
/// </summary>
public sealed class SharedSecretNodeAuthenticatorOptions
{
    /// <summary>集群共享密钥（预共享，所有节点必须一致）。</summary>
    public string SharedSecret { get; set; } = string.Empty;

    /// <summary>凭据有效期（超过该时长视为过期，用于抵御重放）。默认 5 分钟。</summary>
    public TimeSpan CredentialLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// <see cref="INodeAuthenticator"/> 首版实现 —— 基于预共享密钥的 HMAC-SHA256 凭据。
/// </summary>
/// <remarks>
/// <para>
/// 凭据格式：<c>[8 字节 UTC Unix 秒时间戳（大端）][32 字节 HMAC-SHA256(nodeId:timestamp)]</c>。
/// 校验方通过时间窗口 + 固定时间比较（<see cref="CryptographicOperations.FixedTimeEquals"/>）抵御重放与时序攻击。
/// </para>
/// <para>
/// 适用于内网受控通信；生产环境建议切换到 mTLS 实现（见路线图 P8），两者均实现同一 <see cref="INodeAuthenticator"/> 接口。
/// </para>
/// </remarks>
public sealed class SharedSecretNodeAuthenticator : INodeAuthenticator
{
    private const int TimestampLength = sizeof(long);
    private const int MacLength = 32; // HMACSHA256 输出长度
    private const int CredentialLength = TimestampLength + MacLength;
    private const int ClockSkewToleranceSeconds = 30;

    private readonly byte[] _secretBytes;
    private readonly TimeSpan _lifetime;

    /// <summary>创建共享密钥节点鉴权器。</summary>
    public SharedSecretNodeAuthenticator(IOptions<SharedSecretNodeAuthenticatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrEmpty(value.SharedSecret))
        {
            throw new ArgumentException(
                "必须配置集群共享密钥（SharedSecretNodeAuthenticatorOptions.SharedSecret），且所有节点必须一致。",
                nameof(options));
        }

        _secretBytes = Encoding.UTF8.GetBytes(value.SharedSecret);
        _lifetime = value.CredentialLifetime > TimeSpan.Zero ? value.CredentialLifetime : TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    public ValueTask<ReadOnlyMemory<byte>> CreateCredentialAsync(string localNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localNodeId);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var credential = new byte[CredentialLength];
        BinaryPrimitives.WriteInt64BigEndian(credential, timestamp);

        var mac = ComputeMac(localNodeId, timestamp);
        mac.CopyTo(credential.AsSpan(TimestampLength));

        return new ValueTask<ReadOnlyMemory<byte>>(credential);
    }

    /// <inheritdoc/>
    public ValueTask<NodeAuthResult> ValidateAsync(string remoteNodeId, ReadOnlyMemory<byte> credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteNodeId);

        if (credential.Length != CredentialLength)
        {
            return new ValueTask<NodeAuthResult>(NodeAuthResult.Failure("凭据格式不正确"));
        }

        var span = credential.Span;
        var timestamp = BinaryPrimitives.ReadInt64BigEndian(span[..TimestampLength]);

        var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp;
        if (ageSeconds < -ClockSkewToleranceSeconds || ageSeconds > _lifetime.TotalSeconds)
        {
            return new ValueTask<NodeAuthResult>(NodeAuthResult.Failure("凭据已过期或时间戳无效"));
        }

        var expectedMac = ComputeMac(remoteNodeId, timestamp);
        var providedMac = span[TimestampLength..];

        if (!CryptographicOperations.FixedTimeEquals(expectedMac, providedMac))
        {
            return new ValueTask<NodeAuthResult>(NodeAuthResult.Failure("凭据签名校验失败"));
        }

        return new ValueTask<NodeAuthResult>(NodeAuthResult.Success());
    }

    private byte[] ComputeMac(string nodeId, long timestamp)
    {
        using var hmac = new HMACSHA256(_secretBytes);
        var payload = Encoding.UTF8.GetBytes($"{nodeId}:{timestamp}");
        return hmac.ComputeHash(payload);
    }
}
