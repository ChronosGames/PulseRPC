using System.Collections.Generic;
using System.Linq;

namespace PulseRPC.Server.SourceGenerator.Models;

/// <summary>
/// 接收器元数据模型 - 用于 IPulseReceiver 接口
/// </summary>
public sealed class ReceiverModel
{
    /// <summary>
    /// 接口名称（如 IGameReceiver）
    /// </summary>
    public string InterfaceName { get; set; } = null!;

    /// <summary>
    /// 接口完全限定名（如 GameApp.Shared.IGameReceiver）
    /// </summary>
    public string InterfaceFullName { get; set; } = null!;

    /// <summary>
    /// 命名空间
    /// </summary>
    public string Namespace { get; set; } = null!;

    /// <summary>
    /// 方法列表
    /// </summary>
    public List<ReceiverMethodModel> Methods { get; set; } = new();

    /// <summary>
    /// 是否有异步方法
    /// </summary>
    public bool HasAsyncMethods => Methods.Any(m => m.IsAsync);

    /// <summary>
    /// 生成的代理类名称（如 GameReceiverProxy）
    /// </summary>
    public string ProxyClassName => InterfaceName.StartsWith("I")
        ? InterfaceName.Substring(1) + "Proxy"
        : InterfaceName + "Proxy";

    /// <summary>
    /// 生成的 HubContext 类名称（如 GameReceiverHubContext）
    /// </summary>
    public string HubContextClassName => InterfaceName.StartsWith("I")
        ? InterfaceName.Substring(1) + "HubContext"
        : InterfaceName + "HubContext";

    /// <summary>
    /// 生成的 HubClients 类名称（如 GameReceiverHubClients）
    /// </summary>
    public string HubClientsClassName => InterfaceName.StartsWith("I")
        ? InterfaceName.Substring(1) + "HubClients"
        : InterfaceName + "HubClients";
}

/// <summary>
/// 接收器方法元数据模型
/// </summary>
public sealed class ReceiverMethodModel
{
    /// <summary>
    /// 方法名称
    /// </summary>
    public string MethodName { get; set; } = null!;

    /// <summary>
    /// 返回类型名称
    /// </summary>
    public string ReturnTypeName { get; set; } = null!;

    /// <summary>
    /// 参数列表
    /// </summary>
    public List<ReceiverParameterModel> Parameters { get; set; } = new();

    /// <summary>
    /// 是否为异步方法
    /// </summary>
    public bool IsAsync { get; set; }

    /// <summary>
    /// 协议号
    /// </summary>
    public ushort ProtocolId { get; set; }

    /// <summary>
    /// 是否返回 Task
    /// </summary>
    public bool ReturnsTask =>
        ReturnTypeName == "Task" ||
        ReturnTypeName == "System.Threading.Tasks.Task";

    /// <summary>
    /// 是否返回 ValueTask
    /// </summary>
    public bool ReturnsValueTask =>
        ReturnTypeName == "ValueTask" ||
        ReturnTypeName == "System.Threading.Tasks.ValueTask";

    /// <summary>
    /// 是否有参数
    /// </summary>
    public bool HasParameters => Parameters.Count > 0;

    /// <summary>
    /// 是否为单参数
    /// </summary>
    public bool IsSingleParameter => Parameters.Count == 1;

    /// <summary>
    /// 第一个参数
    /// </summary>
    public ReceiverParameterModel? FirstParameter => Parameters.FirstOrDefault();
}

/// <summary>
/// 接收器参数元数据模型
/// </summary>
public sealed class ReceiverParameterModel
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 参数类型名称（简短）
    /// </summary>
    public string TypeName { get; set; } = "";

    /// <summary>
    /// 参数类型完全限定名
    /// </summary>
    public string TypeFullName { get; set; } = "";
}

