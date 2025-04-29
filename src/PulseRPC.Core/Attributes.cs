namespace PulseRPC;

/// <summary>
/// Marks an interface as a PulseRPC service for code generation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class PulseServiceAttribute : Attribute
{
}

/// <summary>
/// Marks an interface as a PulseRPC Hub for code generation.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public sealed class PulseHubAttribute : Attribute
{
}
