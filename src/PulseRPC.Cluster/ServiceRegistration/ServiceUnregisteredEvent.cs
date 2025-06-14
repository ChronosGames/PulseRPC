// using PulseRPC.ServiceDiscovery;
//
// namespace PulseRPC.ServiceRegistration;
//
// /// <summary>
// /// 服务注销事件
// /// </summary>
// public class ServiceUnregisteredEvent : ServiceEvent
// {
//     /// <summary>
//     /// 服务ID
//     /// </summary>
//     public string ServiceId { get; init; } = string.Empty;
//
//     /// <summary>
//     /// 注销原因
//     /// </summary>
//     public string Reason { get; init; } = string.Empty;
//
//     /// <summary>
//     /// 注销成功标志
//     /// </summary>
//     public bool Success { get; init; } = true;
//
//     /// <summary>
//     /// 错误信息
//     /// </summary>
//     public string? ErrorMessage { get; init; }
//
//     /// <summary>
//     /// 是否为主动注销
//     /// </summary>
//     public bool IsGraceful { get; init; } = true;
//
//         /// <summary>
//     /// 创建成功的注销事件
//     /// </summary>
//     /// <param name="serviceId">服务ID</param>
//     /// <param name="serviceName">服务名称</param>
//     /// <param name="endpoint">服务端点</param>
//     /// <param name="reason">注销原因</param>
//     /// <param name="isGraceful">是否为主动注销</param>
//     /// <param name="source">事件源</param>
//     /// <returns>注销事件</returns>
//     public static ServiceUnregisteredEvent CreateSuccess(
//         string serviceId,
//         string serviceName,
//         ServiceEndpoint? endpoint = null,
//         string reason = "",
//         bool isGraceful = true,
//         string source = "") => new()
//     {
//         ServiceId = serviceId,
//         ServiceName = serviceName,
//         Endpoint = endpoint,
//         Reason = reason,
//         Success = true,
//         IsGraceful = isGraceful,
//         Source = source
//     };
//
//     /// <summary>
//     /// 创建失败的注销事件
//     /// </summary>
//     /// <param name="serviceId">服务ID</param>
//     /// <param name="serviceName">服务名称</param>
//     /// <param name="errorMessage">错误信息</param>
//     /// <param name="source">事件源</param>
//     /// <returns>注销事件</returns>
//     public static ServiceUnregisteredEvent CreateFailed(string serviceId, string serviceName, string errorMessage, string source = "") => new()
//     {
//         ServiceId = serviceId,
//         ServiceName = serviceName,
//         Success = false,
//         ErrorMessage = errorMessage,
//         Source = source
//     };
// }
