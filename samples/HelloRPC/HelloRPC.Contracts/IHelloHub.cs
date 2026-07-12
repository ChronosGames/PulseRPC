using PulseRPC;

namespace HelloRPC.Contracts;

public interface IHelloHub : IPulseHub
{
    Task<string> SayHelloAsync(string name);
}
