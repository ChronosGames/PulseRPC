using System.Text.RegularExpressions;

namespace PulseRPC.Server.Validation;

/// <summary>
/// ServiceId 验证器
/// </summary>
/// <remarks>
/// 验证服务实例 ID 的格式和长度，确保符合调度系统要求：
/// <list type="bullet">
/// <item><description>长度限制：1 到 1000 字符</description></item>
/// <item><description>允许字符：字母、数字、连字符、下划线、冒号</description></item>
/// <item><description>防止哈希碰撞和性能问题</description></item>
/// </list>
/// </remarks>
public static partial class ServiceIdValidator
{
    /// <summary>
    /// ServiceId 最大长度限制
    /// </summary>
    public const int MaxLength = 1000;

    /// <summary>
    /// ServiceId 最小长度限制
    /// </summary>
    public const int MinLength = 1;

    /// <summary>
    /// 允许的字符：字母、数字、连字符、下划线、冒号
    /// </summary>
    private static readonly Regex ValidPattern = GenerateValidPattern();

    [GeneratedRegex(@"^[a-zA-Z0-9\-_:]+$", RegexOptions.Compiled)]
    private static partial Regex GenerateValidPattern();

    /// <summary>
    /// 验证 ServiceId 的有效性
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <exception cref="ArgumentNullException">当 serviceId 为 null 时抛出</exception>
    /// <exception cref="ArgumentException">当 serviceId 格式无效时抛出</exception>
    public static void Validate(string serviceId)
    {
        ArgumentNullException.ThrowIfNull(serviceId);

        if (serviceId.Length < MinLength || serviceId.Length > MaxLength)
        {
            throw new ArgumentException(
                $"ServiceId 长度必须在 {MinLength} 到 {MaxLength} 字符之间，当前长度：{serviceId.Length}",
                nameof(serviceId));
        }

        if (!ValidPattern.IsMatch(serviceId))
        {
            throw new ArgumentException(
                $"ServiceId 包含无效字符，仅允许字母、数字、连字符、下划线和冒号。当前值：{serviceId}",
                nameof(serviceId));
        }
    }

    /// <summary>
    /// 判断 ServiceId 是否有效（不抛出异常）
    /// </summary>
    /// <param name="serviceId">服务实例 ID</param>
    /// <returns>如果有效返回 true，否则返回 false</returns>
    public static bool IsValid(string? serviceId)
    {
        if (string.IsNullOrEmpty(serviceId))
        {
            return false;
        }

        if (serviceId.Length < MinLength || serviceId.Length > MaxLength)
        {
            return false;
        }

        return ValidPattern.IsMatch(serviceId);
    }
}
