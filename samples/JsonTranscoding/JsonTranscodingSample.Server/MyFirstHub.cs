using JsonTranscodingSample.Shared;

namespace JsonTranscodingSample.Server;

public sealed class MyFirstHub : IMyFirstHub
{
    public Task<string> SayHelloAsync(string name, int age)
        => Task.FromResult($"Hello {name} ({age})!");

    public Task<RegisterUserResponse> RegisterUserAsync(RegisterUserRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new RegisterUserResponse
        {
            Success = true,
            Message = $"Welcome {request.Name}!",
            RegisteredUser = request
        });
    }
}
