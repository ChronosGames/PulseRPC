# Basic RPC Example

This sample defines one Hub, one client Receiver, and the assembly-local marker that asks the
PulseRPC and MemoryPack source generators to create their Unity-compatible implementations.

After importing the sample from Package Manager, use `IBasicExampleHub` with `GetHub<T>()` and
register an `IBasicExampleReceiver` implementation with `RegisterReceiver<T>()`.
