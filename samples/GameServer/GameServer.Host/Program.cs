using System.Net;
using System.Security.Cryptography.X509Certificates;
using GameServer.Contracts;
using GameServer.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PulseRPC.Backplane.Redis;
using PulseRPC.Clustering;
using PulseRPC.Server.Clustering;
using PulseRPC.Server.Configuration;
using PulseRPC.Server.Extensions;
using PulseRPC.Server.Gateway;
using PulseRPC.Server.Services.Management;
using StackExchange.Redis;

if (args.Length < 1) throw new ArgumentException("Use init or node <node-id> <port> <certificate-directory> <outbound-base-port>.");
if (args[0] == "client") { await AcceptanceClient.RunAsync(args); return; }
var database = Environment.GetEnvironmentVariable("GAME_POSTGRES") ?? throw new InvalidOperationException("Set GAME_POSTGRES.");
await using var source = NpgsqlDataSource.Create(database);
var store = new AssetStore(source);
if (args[0] == "load") { await GameLoadClient.RunAsync(args, store); return; }
if (args[0] == "init") { await store.InitializeAsync(); return; }
if (args[0] == "verify-store") { await AssetStoreVerification.RunAsync(store, source); return; }
if (args[0] == "verify-sessions-store") { await AssetStoreVerification.VerifySessionsAsync(store, source); return; }
if (args[0] == "verify-inactive-owner" && args.Length == 2)
{ await AssetStoreVerification.VerifyInactiveOwnerAsync(source, args[1]); return; }
if (args[0] == "hold-player-row" && args.Length == 2)
{ await AssetStoreVerification.HoldPlayerRowAsync(source, args[1]); return; }
if (args[0] == "seed-broker" && args.Length == 3)
{
    var fence = await store.AcquireAsync(args[1], Guid.NewGuid(), CancellationToken.None);
    await store.PurchaseAsync(fence, new PurchaseCommand { OperationId = Guid.Parse(args[2]) }, CancellationToken.None);
    await store.ReleaseAsync(fence, CancellationToken.None);
    Console.WriteLine("RESULT {\"Seeded\":true}");
    return;
}
if (args[0] == "broker" && args.Length >= 3)
{
    using var brokerConnection = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("GAME_REDIS")
        ?? throw new InvalidOperationException("Set GAME_REDIS."));
    var broker = new PurchaseBroker(source, brokerConnection, args[1], args.Length == 4 ? args[3] : null);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    async Task CrashWindow()
    {
        Console.WriteLine("FAULT_READY " + args[2]);
        await Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token);
    }
    switch (args[2])
    {
        case "publish": await broker.PublishAsync(timeout.Token); break;
        case "publish-crash": await broker.PublishAsync(timeout.Token, CrashWindow); break;
        case "consume": await broker.ConsumeAsync(timeout.Token); break;
        case "consume-crash": await broker.ConsumeAsync(timeout.Token, CrashWindow); break;
        case "verify" when args.Length == 4: await broker.VerifyAsync(args[3], timeout.Token); break;
        default: throw new ArgumentException("Invalid broker command.");
    }
    return;
}
if (args[0] != "node" || args.Length != 5) throw new ArgumentException("Invalid node arguments.");
var node = args[1];
if (node is not ("gateway" or "game-a" or "game-b")) throw new ArgumentException("Unknown cluster node.");
var port = int.Parse(args[2]);
var certDirectory = Path.GetFullPath(args[3]);
var outboundBase = int.Parse(args[4]);
using var certificate = X509Certificate2.CreateFromPemFile(Path.Combine(certDirectory, node + ".crt"), Path.Combine(certDirectory, node + ".key"));
using var authority = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(certDirectory, "ca.crt"));
using var redis = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("GAME_REDIS")
    ?? throw new InvalidOperationException("Set GAME_REDIS."));
using var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); logging.SetMinimumLevel(LogLevel.Warning); })
    .ConfigureServices(services =>
    {
        services.AddSingleton(store);
        services.AddSingleton(source);
        services.AddSingleton<PlayerSessions>();
        services.AddSingleton<PlayerSessionMode>();
        services.AddSingleton<GameAdmission>();
        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddSingleton(provider => new DrainDirectory(redis, provider.GetRequiredService<GameAdmission>(), node,
            provider.GetRequiredService<ILogger<DrainDirectory>>()));
        services.AddHostedService(provider => provider.GetRequiredService<DrainDirectory>());
        if (Environment.GetEnvironmentVariable("GAME_PURCHASE_STREAM") is { Length: > 0 } stream)
        {
            services.AddSingleton(new PurchaseBroker(source, redis, stream));
            services.AddHostedService<PurchaseDeliveryWorker>();
        }
        services.AddPulseServer(options =>
        {
            if (node == "gateway") options.UseGameGatewayProfile();
            else options.UseGameNodeProfile();
            options.MessageWorkerShardCount = 2;
            options.AddTcp(node, port, configure: tcp =>
            {
                tcp.MaxPacketSize = 256 * 1024;
                tcp.RecvBufferSize = tcp.SendBufferSize = 64 * 1024;
            });
            options.Transports[0].ListenAddress = IPAddress.Loopback;
        });
        var dispatcherDescriptor = services.Last(descriptor => descriptor.ServiceType == typeof(PulseRPC.Server.Processing.Engine.IMessageDispatcher));
        services.Remove(dispatcherDescriptor);
        services.AddSingleton(provider => new GameDispatcher(
            (PulseRPC.Server.Processing.Engine.IMessageDispatcher)dispatcherDescriptor.ImplementationFactory!(provider),
            provider.GetRequiredService<GameAdmission>()));
        services.AddSingleton<PulseRPC.Server.Processing.Engine.IMessageDispatcher>(provider => provider.GetRequiredService<GameDispatcher>());
        services.AddPulseClustering(topology =>
        {
            topology.LocalNodeId = node;
            var members = new[] { "gateway", "game-a", "game-b" };
            for (var i = 0; i < members.Length; i++) topology.Members.Add(new ClusterNodeEndpoint
            {
                NodeId = members[i], Host = "127.0.0.1", Port = outboundBase + i
            });
        }, _ => { }, lease => lease.LeaseDuration = TimeSpan.FromSeconds(9));
        services.UseCertificateNodeAuthentication(auth =>
        {
            auth.LocalCertificate = certificate;
            auth.TrustedCertificateAuthorities.Add(authority);
            auth.AllowedNodeIds.UnionWith(["gateway", "game-a", "game-b"]);
        });
        services.Configure<TcpNodeTransportOptions>(transport =>
        {
            transport.SecurityMode = NodeTransportSecurityMode.ExternalMutualTls;
            transport.ConnectTimeout = TimeSpan.FromSeconds(2);
            transport.RequestTimeout = TimeSpan.FromSeconds(5);
            transport.MaxFrameSize = 256 * 1024;
            transport.SendQueueCapacity = 128;
            transport.RecvBufferSize = transport.SendBufferSize = 64 * 1024;
        });
        services.Configure<ActorLeaseHeartbeatOptions>(heartbeat =>
        {
            heartbeat.Interval = TimeSpan.FromSeconds(2);
            heartbeat.RenewalTimeout = TimeSpan.FromSeconds(1);
        });
        services.Configure<StaticClusterMembershipOptions>(membership =>
        {
            membership.FailureThreshold = 1;
            // Keep a failed candidate excluded beyond placement expiry and RPC timeout.
            // A shorter quarantine can select the dead node again when its lease expires.
            membership.QuarantineDuration = TimeSpan.FromSeconds(20);
        });
        services.AddRedisActorLeases(options => options.KeyPrefix = "game-acceptance");
        services.AddSingleton<IActorPlacementStrategy, BackendPlacement>();
        services.AddPulseService<PlayerService>((provider, key) => new PlayerService(key, store, node,
            provider.GetRequiredService<PulseServiceManager>(), provider.GetRequiredService<PlayerSessions>(),
            provider.GetRequiredService<PlayerSessionMode>(), provider.GetRequiredService<ILogger<PlayerService>>()));
        if (node == "gateway")
        {
            services.AddPulseGateway();
            services.AddSingleton<ISessionHub>(provider => new SessionHub(provider.GetRequiredService<PulseRPC.Server.Transport.IServerChannelManager>(),
                provider.GetRequiredService<PlayerSessions>()));
            services.AddSingleton<IGatewayActorInvocationPolicy>(new UserOwnedActorInvocationPolicy(
                new Dictionary<string, IReadOnlyCollection<ushort>> { ["PlayerHub"] = new ushort[] { 0x7101, 0x7102, 0x7103 } }));
            services.AddSingleton<IGatewayActorInvocationPolicy, PlayerSessionPolicy>();
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("GAME_ADMIN_BASE_PORT"), out var adminBase))
        {
            var index = Array.IndexOf(new[] { "gateway", "game-a", "game-b" }, node);
            services.AddHostedService(provider => new GameOperations(adminBase + index, node,
                provider.GetRequiredService<GameAdmission>(), provider.GetRequiredService<DrainDirectory>(),
                provider.GetRequiredService<PulseServiceManager>(), provider.GetRequiredService<PulseRPC.Server.Processing.Engine.ITieredMessageEngine>(),
                provider.GetRequiredService<GameDispatcher>(), source, redis, provider.GetRequiredService<ILogger<GameOperations>>()));
        }
    }).Build();
host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => Console.WriteLine($"READY {node}"));
await host.RunAsync();

internal sealed class BackendPlacement(IClusterMembership membership, DrainDirectory draining) : IActorPlacementStrategy
{
    public string SelectOwner(string hub, string key)
    {
        var nodes = membership.LiveNodeIds.Where(id => id != "gateway" && !draining.Excludes(id)).ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("No game node is available.");
        return new NodeConsistentHashRing(nodes).GetOwner(HashPlacementStrategy.BuildIdentity(hub, key));
    }
}
