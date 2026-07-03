using System.Reflection;

namespace PulseRPC.Protocol;

/// <summary>
/// 手动指定协议号特性
/// </summary>
/// <remarks>
/// <para>
/// 用于手动指定方法的协议号，解决自动生成的协议号冲突。
/// </para>
/// <para>
/// <strong>使用场景</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>协议号碰撞时，手动分配唯一ID（<strong>必需</strong>：编译期一旦检测到
/// 两个方法的协议号相同即报错——<c>PULSE003</c>（服务端 Hub 方法）/ <c>PULSE004</c>
/// （服务端 Receiver 方法）/ <c>PRPC001</c>（客户端）——不会像早期版本那样自动 +1 寻找空位，
/// 因此这是解决冲突的唯一方式）</description></item>
/// <item><description>版本兼容性要求，固定协议号</description></item>
/// <item><description>跨语言互操作，需要明确的协议号</description></item>
/// </list>
/// <para>
/// <strong>注意</strong>: 协议号自动生成由 SourceGenerator 完成（FNV-1a 哈希方法签名的纯函数，
/// 不做线性探测），无需手动生成；只有在编译报错提示冲突时才需要添加本特性。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public interface IChatHub : IPulseHub
/// {
///     // 自动生成协议号（由 SourceGenerator 生成）
///     ValueTask SendMessageAsync(string message);
///
///     // 手动指定协议号（十进制）
///     [Protocol(0x1234)]
///     ValueTask GetHistoryAsync(int count);
///
///     // 手动指定协议号（十六进制）
///     [Protocol("0x5678")]
///     ValueTask JoinRoomAsync(string roomId);
/// }
///
/// // IPulseReceiver（服务端推送）方法同样支持该特性，协议号空间与 Hub 方法相互独立
/// public interface IChatReceiver : IPulseReceiver
/// {
///     [Protocol(0x2001)]
///     ValueTask OnMessageReceivedAsync(string message);
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ProtocolAttribute : Attribute
{
    /// <summary>
    /// 协议号值 (0-65535)
    /// </summary>
    public ushort Value { get; }

    /// <summary>
    /// 初始化协议号特性（十进制）
    /// </summary>
    /// <param name="value">协议号值（十进制）</param>
    /// <example>
    /// <code>
    /// [Protocol(1001)]  // 十进制
    /// ValueTask MethodAsync();
    /// </code>
    /// </example>
    public ProtocolAttribute(ushort value)
    {
        Value = value;
    }

    /// <summary>
    /// 初始化协议号特性（十六进制字符串）
    /// </summary>
    /// <param name="hexValue">协议号值（十六进制字符串，如 "0x1234" 或 "1234"）</param>
    /// <example>
    /// <code>
    /// [Protocol("0x1234")]  // 十六进制
    /// ValueTask MethodAsync();
    /// </code>
    /// </example>
    public ProtocolAttribute(string hexValue)
    {
        if (string.IsNullOrWhiteSpace(hexValue))
            throw new ArgumentNullException(nameof(hexValue));

        // 移除 "0x" 或 "0X" 前缀
        var valueStr = hexValue.Trim();
        if (valueStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            valueStr = valueStr.Substring(2);

        // 解析十六进制
        if (!ushort.TryParse(valueStr, System.Globalization.NumberStyles.HexNumber, null, out var value))
            throw new ArgumentException($"Invalid hex value: '{hexValue}'", nameof(hexValue));

        Value = value;
    }

    /// <summary>
    /// 获取协议号
    /// </summary>
    public ProtocolId GetProtocolId() => new(Value);

    /// <summary>
    /// 字符串表示
    /// </summary>
    public override string ToString() => $"[Protocol(0x{Value:X4})]";
}

/// <summary>
/// 协议号特性扩展方法
/// </summary>
public static class ProtocolAttributeExtensions
{
    /// <summary>
    /// 尝试从方法获取手动指定的协议号
    /// </summary>
    /// <param name="method">方法信息</param>
    /// <param name="protocolId">协议号（如果指定）</param>
    /// <returns>是否指定了协议号</returns>
    /// <remarks>
    /// 仅用于 SourceGenerator 检测手动指定的协议号。
    /// 自动生成的协议号在编译时确定，不使用此方法。
    /// </remarks>
    public static bool TryGetProtocolId(this System.Reflection.MethodInfo method, out ProtocolId protocolId)
    {
        var attribute = method.GetCustomAttribute<ProtocolAttribute>(false);
        if (attribute != null)
        {
            protocolId = attribute.GetProtocolId();
            return true;
        }

        protocolId = default;
        return false;
    }
}
