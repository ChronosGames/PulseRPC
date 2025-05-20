// using PulseRPC.Network;
//
// namespace PulseRPC.Server;
//
// /// <summary>
// /// 请求处理器接口
// /// </summary>
// public interface IRequestHandler<in TRequest, TResponse> : IPacketHandler
// {
//     /// <summary>
//     /// 处理请求
//     /// </summary>
//     /// <param name="session">网络会话</param>
//     /// <param name="request">请求对象</param>
//     /// <param name="cancellationToken">取消令牌</param>
//     /// <returns>响应对象</returns>
//     Task<TResponse> HandleAsync(NetworkSession session, TRequest request, CancellationToken cancellationToken);
// }
//
// /// <summary>
// /// 扩展请求处理器接口，支持请求上下文
// /// </summary>
// /// <typeparam name="TRequest">请求类型</typeparam>
// /// <typeparam name="TResponse">响应类型</typeparam>
// /// <typeparam name="TContext">请求上下文类型</typeparam>
// public interface IContextualRequestHandler<in TRequest, TResponse, in TContext> : IPacketHandler
// {
//     /// <summary>
//     /// 处理带上下文的请求
//     /// </summary>
//     /// <param name="session">网络会话</param>
//     /// <param name="request">请求对象</param>
//     /// <param name="context">请求上下文</param>
//     /// <param name="cancellationToken">取消令牌</param>
//     /// <returns>响应对象</returns>
//     Task<TResponse> HandleAsync(NetworkSession session, TRequest request, TContext context, CancellationToken cancellationToken);
// }
//
// /// <summary>
// /// 通用请求处理器接口，提供更大的灵活性
// /// </summary>
// /// <typeparam name="TRequest">请求类型</typeparam>
// /// <typeparam name="TResponse">响应类型</typeparam>
// /// <typeparam name="TOptions">请求选项类型</typeparam>
// /// <typeparam name="TResult">结果类型，可以与TResponse不同</typeparam>
// public interface IExtendedRequestHandler<in TRequest, TResponse, in TOptions, TResult> : IPacketHandler
// {
//     /// <summary>
//     /// 处理扩展请求
//     /// </summary>
//     /// <param name="session">网络会话</param>
//     /// <param name="request">请求对象</param>
//     /// <param name="options">请求选项</param>
//     /// <param name="cancellationToken">取消令牌</param>
//     /// <returns>处理结果</returns>
//     Task<(TResponse Response, TResult Result)> HandleAsync(NetworkSession session, TRequest request, TOptions options, CancellationToken cancellationToken);
// }
