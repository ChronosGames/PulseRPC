using System;

namespace PulseRPC.Abstractions;

/// <summary>
/// Requests a typed Router proxy for a Hub in the current assembly without changing the shared Hub contract.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute at assembly level in a consumer project. When at least one marker is present, the
/// server generator runs in consumer-only mode for that assembly: it generates Router proxies only for the
/// listed Hub types and does not emit provider routing tables, registries, or module initializers.
/// </para>
/// <code>
/// [assembly: PulseRouterGeneration(typeof(IGameHub))]
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PulseRouterGenerationAttribute : Attribute
{
    /// <summary>Gets the Hub contract for which the current assembly consumes a Router proxy.</summary>
    public Type HubType { get; }

    /// <summary>Creates an assembly-local Router proxy generation marker.</summary>
    /// <param name="hubType">A non-client <see cref="IPulseHub"/> interface.</param>
    public PulseRouterGenerationAttribute(Type hubType)
    {
        HubType = hubType ?? throw new ArgumentNullException(nameof(hubType));
    }
}
