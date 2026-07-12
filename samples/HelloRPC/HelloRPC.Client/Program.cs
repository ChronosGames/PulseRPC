using HelloRPC.Contracts;
using Microsoft.Extensions.Logging;
using PulseRPC.Client;
using PulseRPC.Client.Configuration;

const int port = 5055;
const string expected = "Hello, PulseRPC!";

using var loggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Warning);
});
using var client = new PulseClientBuilder()
    .WithLogging(loggerFactory)
    .Build();

await client.InitializeAsync();
var channel = await client.ConnectToServerAsync("127.0.0.1", port);

try
{
    var hello = channel.GetHub<IHelloHub>();
    var response = await hello.SayHelloAsync("PulseRPC");
    if (!string.Equals(response, expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{response}'.");
    }

    Console.WriteLine(response);
}
finally
{
    await channel.DisconnectAsync();
    await client.StopAsync();
}
