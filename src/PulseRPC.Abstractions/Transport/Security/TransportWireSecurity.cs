using System;

namespace PulseRPC.Abstractions.Transport.Security;

/// <summary>传输层 wire v3 可协商能力。</summary>
[Flags]
public enum TransportWireCapabilities : byte
{
    /// <summary>不启用 wire 变换。</summary>
    None = 0,
    /// <summary>Brotli 帧压缩。</summary>
    BrotliCompression = 1,
    /// <summary>AES-256-GCM 帧加密。</summary>
    Aes256GcmEncryption = 2
}

/// <summary>传输层加密密钥材料。</summary>
public readonly struct TransportEncryptionKey
{
    /// <summary>稳定且非零的密钥标识。</summary>
    public uint KeyId { get; }
    /// <summary>32 字节 AES-256 主密钥。</summary>
    public ReadOnlyMemory<byte> Key { get; }

    public TransportEncryptionKey(uint keyId, ReadOnlyMemory<byte> key)
    {
        KeyId = keyId;
        Key = key;
    }
}

/// <summary>为新会话选择当前密钥，并在轮换窗口内解析旧密钥。</summary>
/// <remarks>
/// 轮换时先发布新 current key，同时在 <see cref="TryGetKey"/> 中保留旧 key；
/// 待旧会话自然排空后再移除旧 key。每个连接在握手时固定一个 key id，不在会话中途切换。
/// </remarks>
public interface ITransportEncryptionKeyProvider
{
    /// <summary>返回新会话应使用的当前密钥。</summary>
    TransportEncryptionKey GetCurrentKey();
    /// <summary>按 key id 解析当前或轮换窗口内保留的密钥。</summary>
    bool TryGetKey(uint keyId, out TransportEncryptionKey key);
}
