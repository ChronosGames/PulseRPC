// using PulseRPC.ServiceDiscovery;
//
// namespace PulseRPC.ServiceRegistration;
//
// /// <summary>
// /// 服务注册事件
// /// </summary>
// public class ServiceRegisteredEvent : ServiceEvent
// {
//     /// <summary>
//     /// 服务注册信息
//     /// </summary>
//     public ServiceRegistration Registration { get; init; } = new();
//
//     /// <summary>
//     /// 注册成功标志
//     /// </summary>
//     public bool Success { get; init; } = true;
//
//     /// <summary>
//     /// 错误信息
//     /// </summary>
//     public string? ErrorMessage { get; init; }
//
//     /// <summary>
//     /// 创建成功的注册事件
//     /// </summary>
//     /// <param name="registration">注册信息</param>
//     /// <param name="source">事件源</param>
//     /// <returns>注册事件</returns>
//     public static ServiceRegisteredEvent CreateSuccess(ServiceRegistration registration, string source = "") => new()
//     {
//         ServiceName = registration.ServiceName,
//         Endpoint = registration.ToEndpoint(),
//         Registration = registration,
//         Success = true,
//         Source = source
//     };
//
//     /// <summary>
//     /// 创建失败的注册事件
//     /// </summary>
//     /// <param name="registration">注册信息</param>
//     /// <param name="errorMessage">错误信息</param>
//     /// <param name="source">事件源</param>
//     /// <returns>注册事件</returns>
//     public static ServiceRegisteredEvent CreateFailed(ServiceRegistration registration, string errorMessage, string source = "") => new()
//     {
//         ServiceName = registration.ServiceName,
//         Endpoint = registration.ToEndpoint(),
//         Registration = registration,
//         Success = false,
//         ErrorMessage = errorMessage,
//         Source = source
//     };
// }
