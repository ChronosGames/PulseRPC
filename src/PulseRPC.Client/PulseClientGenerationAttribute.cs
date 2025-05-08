using System;

namespace PulseRPC.Client;

/// <summary>
/// 标记用于生成PulseRPC客户端代码的类
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PulseClientGenerationAttribute : Attribute
{
    /// <summary>
    /// 要扫描的程序集中的任意类型
    /// </summary>
    public Type MarkerType { get; }

    /// <summary>
    /// 是否禁用自动注册
    /// </summary>
    public bool DisableAutoRegistration { get; set; }

    /// <summary>
    /// 序列化器类型
    /// </summary>
    public GenerateSerializerType Serializer { get; set; } = GenerateSerializerType.MemoryPack;

    /// <summary>
    /// 初始化 <see cref="PulseClientGenerationAttribute"/> 的新实例
    /// </summary>
    /// <param name="markerType">要扫描的程序集中的任意类型</param>
    public PulseClientGenerationAttribute(Type markerType)
    {
        MarkerType = markerType;
    }
}

/// <summary>
/// 序列化器类型
/// </summary>
public enum GenerateSerializerType
{
    /// <summary>
    /// 使用 MessagePack 序列化
    /// </summary>
    MessagePack,

    /// <summary>
    /// 使用 MemoryPack 序列化
    /// </summary>
    MemoryPack
}
