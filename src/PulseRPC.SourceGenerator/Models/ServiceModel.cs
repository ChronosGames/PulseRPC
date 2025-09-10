using System.Collections.Generic;
using System.Linq;

namespace PulseRPC.SourceGenerator.Models;

/// <summary>
/// 服务元数据模型
/// </summary>
public sealed class ServiceModel
{
    public required string InterfaceName { get; init; }
    public required string InterfaceFullName { get; init; }
    public required string Namespace { get; init; }
    public required string ChannelName { get; init; }
    public required List<MethodModel> Methods { get; init; }
    public bool HasAsyncMethods => Methods.Any(m => m.IsAsync);
}

/// <summary>
/// 方法元数据模型
/// </summary>
public sealed class MethodModel
{
    public required string MethodName { get; init; }
    public required string ReturnTypeName { get; init; }
    public required string ReturnTypeFullName { get; init; }
    public required List<ParameterModel> Parameters { get; init; }
    public required bool IsAsync { get; init; }
    public required bool IsGenericTask { get; init; }
    public required string? ChannelName { get; init; }
    
    public bool HasParameters => Parameters.Count > 0;
    public bool IsSingleParameter => Parameters.Count == 1;
    public ParameterModel? FirstParameter => Parameters.FirstOrDefault();
}

/// <summary>
/// 参数元数据模型
/// </summary>
public sealed class ParameterModel
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required string TypeFullName { get; init; }
    public required bool IsMemoryPackable { get; init; }
}

/// <summary>
/// 消息类型元数据模型
/// </summary>
public sealed class MessageModel
{
    public required string TypeName { get; init; }
    public required string TypeFullName { get; init; }
    public required string Namespace { get; init; }
    public required bool IsMemoryPackable { get; init; }
    public required List<PropertyModel> Properties { get; init; }
}

/// <summary>
/// 属性元数据模型
/// </summary>
public sealed class PropertyModel
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required string TypeFullName { get; init; }
    public required bool HasGetter { get; init; }
    public required bool HasSetter { get; init; }
}