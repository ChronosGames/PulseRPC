using System.Collections.Generic;
using System.Linq;

namespace PulseRPC.Server.SourceGenerator.Models;

/// <summary>
/// 服务元数据模型
/// </summary>
public sealed class ServiceModel
{
    public string InterfaceName { get; set; } = null!;
    public string InterfaceFullName { get; set; } = null!;
    public string Namespace { get; set; } = null!;
    public string ChannelName { get; set; } = null!;
    public List<MethodModel> Methods { get; set; } = null!;
    public bool HasAsyncMethods => Methods.Any(m => m.IsAsync);
}

/// <summary>
/// 方法元数据模型
/// </summary>
public sealed class MethodModel
{
    public string MethodName { get; set; } = null!;
    public string ReturnTypeName { get; set; } = null!;
    public string ReturnTypeFullName { get; set; } = null!;
    public List<ParameterModel> Parameters { get; set; } = null!;
    public bool IsAsync { get; set; }
    public bool IsGenericTask { get; set; }
    public string? ChannelName { get; set; }

    public bool HasParameters => Parameters.Count > 0;
    public bool IsSingleParameter => Parameters.Count == 1;
    public ParameterModel? FirstParameter => Parameters.FirstOrDefault();
}

/// <summary>
/// 参数元数据模型
/// </summary>
public sealed class ParameterModel
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string TypeFullName { get; set; } = "";
    public bool IsMemoryPackable { get; set; }
}

/// <summary>
/// 消息类型元数据模型
/// </summary>
public sealed class MessageModel
{
    public string TypeName { get; set; } = null!;
    public string TypeFullName { get; set; } = null!;
    public string Namespace { get; set; } = null!;
    public bool IsMemoryPackable { get; set; }
    public List<PropertyModel> Properties { get; set; } = null!;
}

/// <summary>
/// 属性元数据模型
/// </summary>
public sealed class PropertyModel
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string TypeFullName { get; set; } = "";
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
}
