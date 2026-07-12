using JsonTranscodingSample.Server;
using JsonTranscodingSample.Shared;
using PulseRPC.Server;
using PulseRPC.Server.Extensions;
using PulseRPC.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPulseServer(options =>
{
    options.Transports = new()
    {
        new TransportChannelConfiguration
        {
            Name = "TCP",
            Type = TransportType.TCP,
            Port = 5010,
            IsDefault = true
        }
    };
});
builder.Services.AddSingleton<IMyFirstHub, MyFirstHub>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    description = "Explicit JSON gateway backed by the same PulseRPC Hub implementation.",
    pulseRpc = "tcp://localhost:5010",
    hello = "/api/hello?name=Alice&age=20",
    register = "/api/users"
}));

app.MapGet("/api/hello", async (string name, int age, IMyFirstHub hub) =>
    Results.Ok(new { message = await hub.SayHelloAsync(name, age) }));

app.MapPost("/api/users", async (RegisterUserRequest request, IMyFirstHub hub) =>
    Results.Ok(await hub.RegisterUserAsync(request)));

app.Run();
