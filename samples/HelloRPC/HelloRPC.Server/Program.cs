using HelloRPC.Contracts;
using HelloRPC.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseRPC.Server.Extensions;

const int port = 5055;

using var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Warning);
    })
    .ConfigureServices(services =>
    {
        services.AddPulseServer(options => options.AddTcp("hello", port, isDefault: true));
        services.AddSingleton<IHelloHub, HelloHub>();
    })
    .Build();

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
    Console.WriteLine($"HelloRPC server ready on 127.0.0.1:{port}"));

await host.RunAsync();
