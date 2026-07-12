using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulseRPC.Serialization;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Processing;
using PulseRPC.Server.Processing.Engine;
using PulseRPC.Server.Processing.Pipeline;
using PulseRPC.Server.Transport;

namespace PulseRPC.Server;

/// <summary>
/// The single internal construction boundary for default, named, and compatibility
/// server runtimes. DI registration decides only whether dependencies are keyed.
/// </summary>
internal static class ServerRuntimeComponentFactory
{
    public static IServerChannelManager CreateChannelRegistry(ILoggerFactory loggerFactory)
        => new ServerChannelManager(
            loggerFactory.CreateLogger<ServerChannelManager>(),
            loggerFactory);

    public static IMessageDispatcher CreateDispatcher(
        IServiceRoutingTable routingTable,
        ILoggerFactory loggerFactory)
        => new MessageDispatcher(
            routingTable,
            loggerFactory.CreateLogger<MessageDispatcher>());

    public static IResponseProcessor CreateResponseProcessor(
        IServerChannelManager channelRegistry,
        ISerializerProvider? serializerProvider,
        IResponseSerializerRegistry? responseSerializerRegistry,
        IServiceRoutingTable? routingTable,
        ILoggerFactory loggerFactory)
        => new ResponseProcessor(
            channelRegistry,
            serializerProvider,
            options: null,
            loggerFactory.CreateLogger<ResponseProcessor>(),
            responseSerializerRegistry,
            routingTable);

    public static ITieredMessageEngine CreateMessageEngine(
        IMessageDispatcher dispatcher,
        IServiceProvider hostServiceProvider,
        PulseServerOptions options,
        IServerChannelManager channelRegistry,
        IResponseProcessor responseProcessor,
        ILoggerFactory loggerFactory)
        => new MessageEngine(
            dispatcher,
            hostServiceProvider,
            Options.Create(options),
            loggerFactory.CreateLogger<MessageEngine>(),
            channelRegistry,
            responseProcessor);

    public static ServerRuntime CreateRuntime(
        ITieredMessageEngine? messageEngine,
        IServerChannelManager? channelRegistry,
        ITransportIntegrationManager? transportIntegrationManager,
        ILoggerFactory? loggerFactory,
        IOptions<PulseServerOptions>? options)
        => new(
            messageEngine,
            channelRegistry,
            transportIntegrationManager,
            loggerFactory,
            options);
}
