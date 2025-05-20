namespace PulseRPC.Client;

/// <summary>
/// 标记用于生成PulseRPC客户端代码的类
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PulseClientGenerationAttribute : Attribute
{
    /// <summary>
    /// 要扫描的程序集中的任意类型
    /// </summary>
    public Type MarkerType { get; }

    /// <summary>
    /// 初始化 <see cref="PulseClientGenerationAttribute"/> 的新实例
    /// </summary>
    /// <param name="markerType">要扫描的程序集中的任意类型</param>
    public PulseClientGenerationAttribute(Type markerType)
    {
        MarkerType = markerType;
    }

    /// <summary>
    /// 获取或设置方法返回类型，用于指定WithDeadline等方法的返回类型
    /// </summary>
    public Type? WithResultType { get; set; }
}
