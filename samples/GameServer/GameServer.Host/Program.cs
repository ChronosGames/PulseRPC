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
if (args[0] == "init") { await store.InitializeAsync(); return; }
if (args[0] != "node" || args.Length != 5) throw new ArgumentException("Invalid node arguments.");
var node = args[1];
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
        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddPulseServer(options =>
        {
            if (node == "gateway") options.UseGameGatewayProfile();
            else options.UseGameNodeProfile();
            options.MessageWorkerShardCount = 2;
            options.AddTcp(node, port, configure: tcp => tcp.MaxPacketSize = 256 * 1024);
            options.Transports[0].ListenAddress = IPAddress.Loopback;
        });
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
        });
        services.Configure<TcpNodeTransportOptions>(transport =>
        {
            transport.SecurityMode = NodeTransportSecurityMode.ExternalMutualTls;
            transport.ConnectTimeout = TimeSpan.FromSeconds(2);
            transport.RequestTimeout = TimeSpan.FromSeconds(5);
            transport.MaxFrameSize = 256 * 1024;
            transport.SendQueueCapacity = 128;
        });
        services.Configure<ActorLeaseHeartbeatOptions>(heartbeat =>
        {
            heartbeat.Interval = TimeSpan.FromSeconds(2);
            heartbeat.RenewalTimeout = TimeSpan.FromSeconds(1);
        });
        services.Configure<StaticClusterMembershipOptions>(membership =>
        {
            membership.FailureThreshold = 1;
            membership.QuarantineDuration = TimeSpan.FromSeconds(5);
        });
        services.AddRedisActorLeases(options => options.KeyPrefix = "game-acceptance");
        services.AddSingleton<IActorPlacementStrategy, BackendPlacement>();
        services.AddPulseService<PlayerService>((provider, key) => new PlayerService(key, store, node,
            provider.GetRequiredService<PulseServiceManager>(), provider.GetRequiredService<ILogger<PlayerService>>()));
        if (node == "gateway")
        {
            services.AddPulseGateway();
            services.AddSingleton<ISessionHub, SessionHub>();
            services.AddSingleton<IGatewayActorInvocationPolicy>(new UserOwnedActorInvocationPolicy(
                new Dictionary<string, IReadOnlyCollection<ushort>> { ["PlayerHub"] = new ushort[] { 0x7101, 0x7102, 0x7103 } }));
        }
    }).Build();
host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.Register(() => Console.WriteLine($"READY {node}"));
await host.RunAsync();

internal sealed class BackendPlacement(IClusterMembership membership) : IActorPlacementStrategy
{
    public string SelectOwner(string hub, string key)
    {
        var nodes = membership.LiveNodeIds.Where(id => id != "gateway").ToArray();
        if (nodes.Length == 0) throw new InvalidOperationException("No game node is available.");
        return new NodeConsistentHashRing(nodes).GetOwner(HashPlacementStrategy.BuildIdentity(hub, key));
    }
}
