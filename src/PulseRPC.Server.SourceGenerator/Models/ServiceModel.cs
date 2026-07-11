using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

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
    /// <summary>
    /// 组合 facet 中由独立基 Hub 提供实际分发、但仍属于本 Hub 的合法协议别名。
    /// </summary>
    public List<MethodModel> ProtocolAliases { get; } = new();
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
    /// 方法实际声明所在接口的完全限定名（对继承自基接口的方法，是基接口而非 <see cref="ServiceModel.InterfaceFullName"/>）。
    /// 用于协议号哈希输入，必须与客户端生成器（<c>ProtocolIdGenerator.BuildMethodSignature</c> 使用
    /// <c>method.ContainingType.ToDisplayString()</c>）保持一致，见 §11.2 风险 #1。为 <c>null</c> 时
    /// 回退为 <see cref="ServiceModel.InterfaceFullName"/>（等价于方法直接声明在当前接口上）。
    /// </summary>
    public string? DeclaringInterfaceFullName { get; set; }

    /// <summary>
    /// 协议号 - 用于高性能方法路由
    /// </summary>
    public ushort ProtocolId { get; set; }

    /// <summary>
    /// 方法声明的源码位置（用于协议号冲突诊断定位，以支持 CodeFixProvider 自动插入
    /// <c>[Protocol(0xXXXX)]</c>）。当方法来自引用程序集的元数据（而非当前编译单元的源码）时为 <c>null</c>。
    /// </summary>
    public Location? Location { get; set; }

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

    /// <summary>
    /// P-6：方法是否对客户端可见（已解析出的最终值 = 方法级标注覆盖 facet 级默认值）。
    /// 未标注 <c>[ClientFacing]</c> 的方法默认为 <c>false</c>（白名单语义，拒绝优先）。
    /// </summary>
    public bool IsClientFacing { get; set; }

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

    /// <summary>是否要求调用方已认证。</summary>
    public bool RequireAuthentication { get; set; }

    /// <summary>是否仅允许内部服务调用。</summary>
    public bool InternalOnly { get; set; }

    /// <summary>是否仅允许外部用户调用。</summary>
    public bool ExternalOnly { get; set; }

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

    /// <summary>合并后的全部策略名（全部必须满足）。</summary>
    public List<string> Policies { get; } = new();

    /// <summary>合并后的角色、权限与 scope 要求（全部必须满足）。</summary>
    public List<AuthorizationRequirementModel> Requirements { get; } = new();

    public bool IsEmpty =>
        !AllowAnonymous && !RequireAuthentication && !InternalOnly && !ExternalOnly &&
        Policies.Count == 0 && Requirements.Count == 0;
}

public enum AuthorizationRequirementKindModel
{
    Role,
    Permission,
    Scope,
}

public sealed class AuthorizationRequirementModel
{
    public AuthorizationRequirementKindModel Kind { get; set; }
    public string Value { get; set; } = null!;
    public bool AllowInternal { get; set; }
    public bool AllowSystem { get; set; }
}
