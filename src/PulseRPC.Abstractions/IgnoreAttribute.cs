namespace PulseRPC;

/// <summary>
/// Don't register on PulseRPC Engine.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false,Inherited = false)]
public class IgnoreAttribute : Attribute
{
}
