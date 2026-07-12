using HelloRPC.Contracts;

namespace HelloRPC.Server;

internal sealed class HelloHub : IHelloHub
{
    public Task<string> SayHelloAsync(string name)
    {
        return Task.FromResult($"Hello, {name}!");
    }
}
