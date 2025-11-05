namespace PulseRPC.Protocol;

/// <summary>
/// 协议号类型 - 16位无符号整数包装
/// </summary>
/// <remarks>
/// <para>
/// 协议号用于标识RPC方法，范围 0-65535。
/// 通过 xxHash32 自动生成，保证客户端和服务端一致。
/// </para>
/// <para>
/// <strong>使用示例</strong>:
/// </para>
/// <code>
/// // 自动生成
/// var protocolId = ProtocolId.Generate("MyApp.IChatHub.SendMessageAsync(string)");
/// // protocolId: 0x3A7F (14975)
///
/// // 手动指定
/// var protocolId = new ProtocolId(0x1234);  // 4660
/// </code>
/// </remarks>
public readonly struct ProtocolId : IEquatable<ProtocolId>, IComparable<ProtocolId>
{
    /// <summary>
    /// 协议号值 (0-65535)
    /// </summary>
    public ushort Value { get; }

    /// <summary>
    /// 初始化协议号
    /// </summary>
    /// <param name="value">协议号值</param>
    public ProtocolId(ushort value)
    {
        Value = value;
    }

    /// <summary>
    /// 协议号是否为默认值（未初始化）
    /// </summary>
    public bool IsDefault => Value == 0;

    /// <summary>
    /// 隐式转换为 ushort
    /// </summary>
    public static implicit operator ushort(ProtocolId id) => id.Value;

    /// <summary>
    /// 隐式转换为 ProtocolId
    /// </summary>
    public static implicit operator ProtocolId(ushort value) => new(value);

    /// <summary>
    /// 相等比较
    /// </summary>
    public bool Equals(ProtocolId other) => Value == other.Value;

    /// <summary>
    /// 相等比较（覆盖）
    /// </summary>
    public override bool Equals(object? obj) => obj is ProtocolId other && Equals(other);

    /// <summary>
    /// 哈希码
    /// </summary>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>
    /// 大小比较
    /// </summary>
    public int CompareTo(ProtocolId other) => Value.CompareTo(other.Value);

    /// <summary>
    /// 相等运算符
    /// </summary>
    public static bool operator ==(ProtocolId left, ProtocolId right) => left.Equals(right);

    /// <summary>
    /// 不等运算符
    /// </summary>
    public static bool operator !=(ProtocolId left, ProtocolId right) => !left.Equals(right);

    /// <summary>
    /// 字符串表示（十六进制格式）
    /// </summary>
    /// <returns>格式: "0x1234"</returns>
    public override string ToString() => $"0x{Value:X4}";

    /// <summary>
    /// 格式化字符串
    /// </summary>
    /// <param name="format">
    /// 支持的格式:
    /// - "X" 或 "x": 十六进制（例如: "0x1234"）
    /// - "D" 或 "d": 十进制（例如: "4660"）
    /// - null: 默认使用十六进制
    /// </param>
    /// <returns>格式化后的字符串</returns>
    public string ToString(string? format) => format?.ToUpperInvariant() switch
    {
        "X" => $"0x{Value:X4}",
        "D" => Value.ToString(),
        null => ToString(),
        _ => throw new FormatException($"Invalid format string: '{format}'")
    };
}
