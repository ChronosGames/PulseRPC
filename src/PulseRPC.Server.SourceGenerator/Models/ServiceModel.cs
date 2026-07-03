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
    public string? ServiceName { get; set; }
    public List<MethodModel> Methods { get; set; } = null!;
    public bool HasAsyncMethods => Methods.Any(m => m.IsAsync);

    /// <summary>
    /// 接口级别的授权信息
    /// </summary>
    public AuthorizationModel? Authorization { get; set; }
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

    /// <summary>
    /// 协议号 - 用于高性能方法路由
    /// </summary>
    public ushort ProtocolId { get; set; }

    /// <summary>
    /// 当方法返回 Task&lt;T&gt;/ValueTask&lt;T&gt; 时，该属性为泛型参数的完全限定名；否则为 null。
    /// </summary>
    public string? ResponseTypeFullName { get; set; }

    /// <summary>
    /// 响应类型是否标记为 MemoryPackable，用于生成序列化优化路径。
    /// </summary>
    public bool IsResponseMemoryPackable { get; set; }

    /// <summary>
    /// 方法级别的授权信息
    /// </summary>
    public AuthorizationModel? Authorization { get; set; }

    /// <summary>
    /// 方法是否标记为可重入（只读），可绕过串行邮箱与其它读者并发执行。
    /// </summary>
    public bool IsReentrant { get; set; }

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

/// <summary>
/// 授权元数据模型
/// </summary>
public sealed class AuthorizationModel
{
    /// <summary>
    /// 是否允许匿名访问
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// 角色类型（External, Internal, GM, System, 或自定义）
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// 授权策略
    /// </summary>
    public string? Policy { get; set; }

    /// <summary>
    /// 权限范围
    /// </summary>
    public string[]? Scopes { get; set; }
}
