using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using PulseRPC.Abstractions.Transport.Security;

namespace PulseRPC.Shared.Security;

internal readonly struct TransportWireOffer
{
    public TransportWireCapabilities Capabilities { get; }
    public int CompressionThreshold { get; }
    public uint KeyId { get; }
    public byte[] ClientNonce { get; }

    public TransportWireOffer(
        TransportWireCapabilities capabilities,
        int compressionThreshold,
        uint keyId,
        byte[] clientNonce)
    {
        Capabilities = capabilities;
        CompressionThreshold = compressionThreshold;
        KeyId = keyId;
        ClientNonce = clientNonce;
    }
}

internal static class TransportWireNegotiator
{
    private const string ExtensionPrefix = "prpc-wire-v3";
    private const int SessionNonceSize = 16;

    public static void ValidateOptions(TransportOptions options)
    {
        if (options.MaxPacketSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxPacketSize));
        if (options.CompressionThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.CompressionThreshold));

        if (!options.UseEncryption)
            return;

        var provider = options.EncryptionKeyProvider
            ?? throw new InvalidOperationException("启用 wire 加密时必须配置 EncryptionKeyProvider。");
        ValidateKey(provider.GetCurrentKey());
    }

    public static TransportWireOffer CreateClientOffer(TransportOptions options)
    {
        ValidateOptions(options);
        var capabilities = GetRequiredCapabilities(options);
        var keyId = 0u;
        var nonce = Array.Empty<byte>();

        if ((capabilities & TransportWireCapabilities.Aes256GcmEncryption) != 0)
        {
            var key = options.EncryptionKeyProvider!.GetCurrentKey();
            ValidateKey(key);
            keyId = key.KeyId;
            nonce = CreateNonce();
        }

        return new TransportWireOffer(capabilities, options.CompressionThreshold, keyId, nonce);
    }

    public static string SerializeOffer(TransportWireOffer offer)
        => string.Join("|",
            ExtensionPrefix,
            ((byte)offer.Capabilities).ToString(CultureInfo.InvariantCulture),
            offer.CompressionThreshold.ToString(CultureInfo.InvariantCulture),
            offer.KeyId.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(offer.ClientNonce));

    public static bool TryAcceptServerOffer(
        TransportOptions options,
        string extensions,
        out string responseExtensions,
        out TransportWireSession? session,
        out string? reason)
    {
        responseExtensions = "{}";
        session = null;
        reason = null;

        try
        {
            ValidateOptions(options);
            var offer = ParseOffer(extensions);
            var required = GetRequiredCapabilities(options);
            if (offer.Capabilities != required)
            {
                reason = $"wire 能力不匹配: client={offer.Capabilities}, server={required}";
                return false;
            }

            var threshold = Math.Max(options.CompressionThreshold, offer.CompressionThreshold);
            var serverNonce = Array.Empty<byte>();
            if ((required & TransportWireCapabilities.Aes256GcmEncryption) != 0)
            {
                if (offer.KeyId == 0 || offer.ClientNonce.Length != SessionNonceSize)
                {
                    reason = "wire 加密协商缺少有效 key id 或 client nonce。";
                    return false;
                }

                if (!options.EncryptionKeyProvider!.TryGetKey(offer.KeyId, out var key))
                {
                    reason = $"wire 加密 key id {offer.KeyId} 不可用。";
                    return false;
                }
                ValidateKey(key, offer.KeyId);
                serverNonce = CreateNonce();
            }

            responseExtensions = SerializeResponse(required, threshold, offer.KeyId, serverNonce);
            session = TransportWireSession.Create(
                options, required, threshold, offer.KeyId, offer.ClientNonce, serverNonce, isClient: false);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            reason = $"无效的 wire v3 能力协商: {ex.Message}";
            return false;
        }
    }

    public static bool TryCompleteClient(
        TransportOptions options,
        TransportWireOffer offer,
        string extensions,
        out TransportWireSession? session,
        out string? reason)
    {
        session = null;
        reason = null;
        try
        {
            var response = ParseResponse(extensions);
            if (response.Capabilities != offer.Capabilities)
            {
                reason = $"服务端选择的 wire 能力发生降级: offered={offer.Capabilities}, selected={response.Capabilities}";
                return false;
            }
            if (response.CompressionThreshold < offer.CompressionThreshold)
            {
                reason = "服务端返回的压缩阈值低于客户端要求。";
                return false;
            }
            if (response.KeyId != offer.KeyId)
            {
                reason = $"服务端返回了未请求的 key id {response.KeyId}。";
                return false;
            }
            if ((offer.Capabilities & TransportWireCapabilities.Aes256GcmEncryption) != 0 &&
                response.ServerNonce.Length != SessionNonceSize)
            {
                reason = "服务端未返回有效的加密 nonce。";
                return false;
            }

            session = TransportWireSession.Create(
                options,
                response.Capabilities,
                response.CompressionThreshold,
                response.KeyId,
                offer.ClientNonce,
                response.ServerNonce,
                isClient: true);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            reason = $"无效的 wire v3 协商响应: {ex.Message}";
            return false;
        }
    }

    private static TransportWireCapabilities GetRequiredCapabilities(TransportOptions options)
    {
        var result = TransportWireCapabilities.None;
        if (options.UseCompression)
            result |= TransportWireCapabilities.BrotliCompression;
        if (options.UseEncryption)
            result |= TransportWireCapabilities.Aes256GcmEncryption;
        return result;
    }

    private static TransportWireOffer ParseOffer(string extensions)
    {
        var fields = ParseFields(extensions);
        var capabilities = ParseCapabilities(fields[1]);
        var threshold = ParsePositiveInt(fields[2], "compression threshold");
        var keyId = uint.Parse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture);
        var nonce = ParseBase64(fields[4]);
        ValidateEncryptionFields(capabilities, keyId, nonce, "client");
        return new TransportWireOffer(capabilities, threshold, keyId, nonce);
    }

    private static string SerializeResponse(
        TransportWireCapabilities capabilities,
        int compressionThreshold,
        uint keyId,
        byte[] serverNonce)
        => string.Join("|",
            ExtensionPrefix,
            ((byte)capabilities).ToString(CultureInfo.InvariantCulture),
            compressionThreshold.ToString(CultureInfo.InvariantCulture),
            keyId.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(serverNonce));

    private static (TransportWireCapabilities Capabilities, int CompressionThreshold, uint KeyId, byte[] ServerNonce)
        ParseResponse(string extensions)
    {
        var fields = ParseFields(extensions);
        var capabilities = ParseCapabilities(fields[1]);
        var threshold = ParsePositiveInt(fields[2], "compression threshold");
        var keyId = uint.Parse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture);
        var nonce = ParseBase64(fields[4]);
        ValidateEncryptionFields(capabilities, keyId, nonce, "server");
        return (capabilities, threshold, keyId, nonce);
    }

    private static string[] ParseFields(string extensions)
    {
        var fields = extensions.Split('|');
        if (fields.Length != 5 || fields[0] != ExtensionPrefix)
            throw new FormatException("扩展格式不是 prpc-wire-v3。 ");
        return fields;
    }

    private static TransportWireCapabilities ParseCapabilities(string value)
    {
        var raw = byte.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
        const TransportWireCapabilities known =
            TransportWireCapabilities.BrotliCompression | TransportWireCapabilities.Aes256GcmEncryption;
        var parsed = (TransportWireCapabilities)raw;
        if ((parsed & ~known) != 0)
            throw new FormatException($"未知 wire capability: {raw}");
        return parsed;
    }

    private static int ParsePositiveInt(string value, string name)
    {
        var parsed = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
        if (parsed <= 0)
            throw new FormatException($"{name} 必须为正数。");
        return parsed;
    }

    private static byte[] ParseBase64(string value)
        => value.Length == 0 ? Array.Empty<byte>() : Convert.FromBase64String(value);

    private static void ValidateEncryptionFields(
        TransportWireCapabilities capabilities,
        uint keyId,
        byte[] nonce,
        string side)
    {
        var encrypted = (capabilities & TransportWireCapabilities.Aes256GcmEncryption) != 0;
        if (encrypted && (keyId == 0 || nonce.Length != SessionNonceSize))
            throw new FormatException($"{side} 加密字段无效。");
        if (!encrypted && (keyId != 0 || nonce.Length != 0))
            throw new FormatException($"{side} 在未协商加密时携带了加密字段。");
    }

    private static byte[] CreateNonce()
    {
        var nonce = new byte[SessionNonceSize];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(nonce);
        return nonce;
    }

    internal static void ValidateKey(TransportEncryptionKey key, uint? expectedKeyId = null)
    {
        if (key.KeyId == 0 || (expectedKeyId.HasValue && key.KeyId != expectedKeyId.Value))
            throw new InvalidOperationException("wire 加密 key id 必须为匹配的非零值。");
        if (key.Key.Length != 32)
            throw new InvalidOperationException("wire 加密仅接受 32 字节 AES-256 密钥。");
    }
}

internal sealed class TransportWireSession : IDisposable
{
    internal const byte EnvelopeVersion = 1;
    internal const byte CompressedFlag = 1;
    internal const byte EncryptedFlag = 2;
    internal const int HeaderSize = 18;
    internal const int AuthenticationTagSize = 16;
    internal const int MaxEnvelopeOverhead = HeaderSize + AuthenticationTagSize;

    private readonly TransportWireCapabilities _capabilities;
    private readonly int _compressionThreshold;
    private readonly int _maxPacketSize;
    private readonly uint _keyId;
    private readonly byte[]? _sendKey;
    private readonly byte[]? _receiveKey;
    private readonly byte[]? _sendNoncePrefix;
    private readonly byte[]? _receiveNoncePrefix;
    private long _sendSequence;
    private long _receiveSequence;
    private int _disposed;

    public bool HasTransforms => _capabilities != TransportWireCapabilities.None;
    public bool RequiresEncryption =>
        (_capabilities & TransportWireCapabilities.Aes256GcmEncryption) != 0;

    private TransportWireSession(
        TransportWireCapabilities capabilities,
        int compressionThreshold,
        int maxPacketSize,
        uint keyId,
        byte[]? sendKey,
        byte[]? receiveKey,
        byte[]? sendNoncePrefix,
        byte[]? receiveNoncePrefix)
    {
        _capabilities = capabilities;
        _compressionThreshold = compressionThreshold;
        _maxPacketSize = maxPacketSize;
        _keyId = keyId;
        _sendKey = sendKey;
        _receiveKey = receiveKey;
        _sendNoncePrefix = sendNoncePrefix;
        _receiveNoncePrefix = receiveNoncePrefix;
    }

    public static TransportWireSession Create(
        TransportOptions options,
        TransportWireCapabilities capabilities,
        int compressionThreshold,
        uint keyId,
        byte[] clientNonce,
        byte[] serverNonce,
        bool isClient)
    {
        if ((capabilities & TransportWireCapabilities.Aes256GcmEncryption) == 0)
        {
            return new TransportWireSession(
                capabilities, compressionThreshold, options.MaxPacketSize, 0, null, null, null, null);
        }

        if (!options.EncryptionKeyProvider!.TryGetKey(keyId, out var material))
            throw new InvalidOperationException($"wire 加密 key id {keyId} 不可用。");
        TransportWireNegotiator.ValidateKey(material, keyId);

        var masterKey = material.Key.ToArray();
        var clientToServer = Derive(masterKey, clientNonce, serverNonce, "client-to-server-key", 32);
        var serverToClient = Derive(masterKey, clientNonce, serverNonce, "server-to-client-key", 32);
        var clientNoncePrefix = Derive(masterKey, clientNonce, serverNonce, "client-to-server-nonce", 4);
        var serverNoncePrefix = Derive(masterKey, clientNonce, serverNonce, "server-to-client-nonce", 4);
        CryptographicOperations.ZeroMemory(masterKey);

        return new TransportWireSession(
            capabilities,
            compressionThreshold,
            options.MaxPacketSize,
            keyId,
            isClient ? clientToServer : serverToClient,
            isClient ? serverToClient : clientToServer,
            isClient ? clientNoncePrefix : serverNoncePrefix,
            isClient ? serverNoncePrefix : clientNoncePrefix);
    }

    public TransportWirePayload Encode(ReadOnlySpan<byte> input)
    {
        if (input.Length <= 0 || input.Length > _maxPacketSize)
            throw new InvalidDataException($"wire 原始消息长度 {input.Length} 超出范围。");

        var transformFlags = (byte)0;
        byte[] payload = input.ToArray();
        if ((_capabilities & TransportWireCapabilities.BrotliCompression) != 0 &&
            input.Length >= _compressionThreshold)
        {
            var compressed = Compress(input);
            if (compressed.Length < input.Length)
            {
                payload = compressed;
                transformFlags |= CompressedFlag;
            }
        }

        ulong sequence = 0;
        var encrypted = RequiresEncryption;
        if (encrypted)
        {
            transformFlags |= EncryptedFlag;
            sequence = checked((ulong)Interlocked.Increment(ref _sendSequence));
        }

        if (transformFlags == 0)
            return new TransportWirePayload(payload, 0);

        var output = new byte[HeaderSize + payload.Length + (encrypted ? AuthenticationTagSize : 0)];
        var header = output.AsSpan(0, HeaderSize);
        header[0] = EnvelopeVersion;
        header[1] = transformFlags;
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(2, 4), encrypted ? _keyId : 0);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(6, 8), sequence);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(14, 4), input.Length);

        if (encrypted)
        {
            var nonce = CreateFrameNonce(_sendNoncePrefix!, sequence);
            var cipher = output.AsSpan(HeaderSize, payload.Length);
            var tag = output.AsSpan(HeaderSize + payload.Length, AuthenticationTagSize);
            Encrypt(_sendKey!, nonce, payload, cipher, tag, header);
        }
        else
        {
            payload.CopyTo(output, HeaderSize);
        }

        return new TransportWirePayload(output, transformFlags);
    }

    public byte[] Decode(ReadOnlySpan<byte> input, byte outerFlags)
    {
        var outerEncrypted = (outerFlags & EncryptedFlag) != 0;
        var outerCompressed = (outerFlags & CompressedFlag) != 0;
        if (outerEncrypted && !RequiresEncryption)
            throw new InvalidDataException("收到未协商的 wire 加密帧。");
        if (outerCompressed &&
            (_capabilities & TransportWireCapabilities.BrotliCompression) == 0)
            throw new InvalidDataException("收到未协商的 wire 压缩帧。");
        if (RequiresEncryption && !outerEncrypted)
            throw new InvalidDataException("加密会话收到未加密业务帧，拒绝降级。");
        if (!outerEncrypted && !outerCompressed)
        {
            if (input.Length <= 0 || input.Length > _maxPacketSize)
                throw new InvalidDataException("wire 原始业务帧长度超限。");
            return input.ToArray();
        }
        if (input.Length < HeaderSize + (outerEncrypted ? AuthenticationTagSize : 0))
            throw new InvalidDataException("wire 变换帧过短。");

        var header = input.Slice(0, HeaderSize);
        if (header[0] != EnvelopeVersion)
            throw new InvalidDataException($"不支持的 wire envelope version {header[0]}。");
        var innerFlags = header[1];
        if ((innerFlags & ~(CompressedFlag | EncryptedFlag)) != 0 ||
            outerCompressed != ((innerFlags & CompressedFlag) != 0) ||
            outerEncrypted != ((innerFlags & EncryptedFlag) != 0))
            throw new InvalidDataException("wire 帧内外标志不一致。");

        var keyId = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(2, 4));
        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(6, 8));
        var originalLength = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(14, 4));
        if (originalLength <= 0 || originalLength > _maxPacketSize)
            throw new InvalidDataException("wire 帧原始长度超限。");

        byte[] payload;
        if (outerEncrypted)
        {
            if (!RequiresEncryption || keyId != _keyId || sequence == 0)
                throw new InvalidDataException("wire 加密 key id 或序号无效。");
            var previous = checked((ulong)Interlocked.Read(ref _receiveSequence));
            if (sequence <= previous)
                throw new InvalidDataException("wire 加密帧重放或乱序。");

            var cipherLength = input.Length - HeaderSize - AuthenticationTagSize;
            payload = new byte[cipherLength];
            var nonce = CreateFrameNonce(_receiveNoncePrefix!, sequence);
            Decrypt(
                _receiveKey!,
                nonce,
                input.Slice(HeaderSize, cipherLength),
                input.Slice(HeaderSize + cipherLength, AuthenticationTagSize),
                payload,
                header);
            Interlocked.Exchange(ref _receiveSequence, checked((long)sequence));
        }
        else
        {
            if (keyId != 0 || sequence != 0)
                throw new InvalidDataException("未加密 wire 帧携带了密钥或序号。");
            payload = input.Slice(HeaderSize).ToArray();
        }

        if (outerCompressed)
            return Decompress(payload, originalLength, _maxPacketSize);
        if (payload.Length != originalLength)
            throw new InvalidDataException("wire 帧原始长度与载荷不一致。");
        return payload;
    }

    private static byte[] Compress(ReadOnlySpan<byte> input)
    {
        using var output = new MemoryStream();
        using (var stream = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
            stream.Write(input.ToArray(), 0, input.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] input, int expectedLength, int maxPacketSize)
    {
        using var source = new MemoryStream(input, writable: false);
        using var stream = new BrotliStream(source, CompressionMode.Decompress);
        var output = new byte[expectedLength];
        var offset = 0;
        while (offset < output.Length)
        {
            var read = stream.Read(output, offset, output.Length - offset);
            if (read == 0)
                break;
            offset += read;
        }
        if (offset != expectedLength || stream.ReadByte() != -1 || output.Length > maxPacketSize)
            throw new InvalidDataException("Brotli 解压长度与协商帧不一致。");
        return output;
    }

    private static byte[] CreateFrameNonce(byte[] prefix, ulong sequence)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), sequence);
        return nonce;
    }

    private static byte[] Derive(
        byte[] masterKey,
        byte[] clientNonce,
        byte[] serverNonce,
        string label,
        int length)
    {
        var labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
        var input = new byte[clientNonce.Length + serverNonce.Length + labelBytes.Length];
        clientNonce.CopyTo(input, 0);
        serverNonce.CopyTo(input, clientNonce.Length);
        labelBytes.CopyTo(input, clientNonce.Length + serverNonce.Length);
        using var hmac = new HMACSHA256(masterKey);
        var hash = hmac.ComputeHash(input);
        return hash.AsSpan(0, length).ToArray();
    }

    private static void Encrypt(
        byte[] key,
        byte[] nonce,
        byte[] plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData)
    {
#if NET10_0_OR_GREATER
        using var aes = new AesGcm(key, AuthenticationTagSize);
#else
#pragma warning disable SYSLIB0053
        using var aes = new AesGcm(key);
#pragma warning restore SYSLIB0053
#endif
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    private static void Decrypt(
        byte[] key,
        byte[] nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
#if NET10_0_OR_GREATER
        using var aes = new AesGcm(key, AuthenticationTagSize);
#else
#pragma warning disable SYSLIB0053
        using var aes = new AesGcm(key);
#pragma warning restore SYSLIB0053
#endif
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (_sendKey != null)
            CryptographicOperations.ZeroMemory(_sendKey);
        if (_receiveKey != null)
            CryptographicOperations.ZeroMemory(_receiveKey);
        if (_sendNoncePrefix != null)
            CryptographicOperations.ZeroMemory(_sendNoncePrefix);
        if (_receiveNoncePrefix != null)
            CryptographicOperations.ZeroMemory(_receiveNoncePrefix);
    }
}

internal readonly struct TransportWirePayload
{
    public byte[] Data { get; }
    public byte Flags { get; }

    public TransportWirePayload(byte[] data, byte flags)
    {
        Data = data;
        Flags = flags;
    }
}
