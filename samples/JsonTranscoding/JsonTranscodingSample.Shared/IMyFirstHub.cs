using MemoryPack;
using PulseRPC;

namespace JsonTranscodingSample.Shared;

/// <summary>Contract shared by the PulseRPC transport and the explicit JSON gateway.</summary>
public interface IMyFirstHub : IPulseHub
{
    Task<string> SayHelloAsync(string name, int age);

    Task<RegisterUserResponse> RegisterUserAsync(RegisterUserRequest request);
}

[MemoryPackable]
public partial class RegisterUserRequest
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

[MemoryPackable]
public partial class RegisterUserResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public RegisterUserRequest RegisteredUser { get; set; } = new();
}
