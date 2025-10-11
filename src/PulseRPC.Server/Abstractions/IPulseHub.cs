using System.Threading.Tasks;

namespace PulseRPC.Server.Abstractions;

/// <summary>
/// Base interface for user-defined RPC services.
/// Service implementations should inherit from this interface to be registered with PulseRPC.Server.
/// Methods will be automatically discovered and made available for remote invocation.
/// </summary>
/// <remarks>
/// This is a marker interface that allows the server to identify and register services.
/// Service methods can be synchronous or asynchronous (Task/Task&lt;T&gt;).
///
/// Example:
/// <code>
/// public interface ICalculatorService : IPulseHub
/// {
///     int Add(int a, int b);
///     Task&lt;double&gt; DivideAsync(double numerator, double denominator);
/// }
///
/// public class CalculatorService : ICalculatorService
/// {
///     public int Add(int a, int b) => a + b;
///     public async Task&lt;double&gt; DivideAsync(double numerator, double denominator)
///     {
///         if (denominator == 0) throw new DivideByZeroException();
///         await Task.Delay(1); // Simulate async work
///         return numerator / denominator;
///     }
/// }
/// </code>
/// </remarks>
public interface IPulseHub
{
    // Marker interface - no methods required
    // Service methods are discovered via reflection or source generation
}
