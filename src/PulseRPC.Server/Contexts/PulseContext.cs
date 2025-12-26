using System.Runtime.CompilerServices;
using PulseRPC.Transport;

namespace PulseRPC.Server.Contexts;

/// <summary>
/// Static accessor for the current request context.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Design</strong>:
/// </para>
/// <list type="bullet">
/// <item><description>Uses <see cref="AsyncLocal{T}"/> to ensure context flows correctly through async operations</description></item>
/// <item><description>Supports nested contexts via <see cref="ContextScope"/> which auto-restores previous context</description></item>
/// <item><description>Provides convenient static accessors for common properties</description></item>
/// </list>
/// <para>
/// <strong>Usage Example</strong>:
/// </para>
/// <code>
/// // Setting context
/// using (PulseContext.SetContext(new PulseContextData { UserId = "user-123" }))
/// {
///     // Access context anywhere in the async call chain
///     var userId = PulseContext.CurrentUserId;
///     var transport = PulseContext.CurrentTransport;
///
///     // Nested context (auto-restores when disposed)
///     using (PulseContext.SetContext(new PulseContextData { UserId = "inner-user" }))
///     {
///         // Here UserId = "inner-user"
///     }
///     // Restored to "user-123"
/// }
/// </code>
/// </remarks>
public static class PulseContext
{
    private static readonly AsyncLocal<IPulseContext?> _current = new();

    /// <summary>
    /// Gets the current request context.
    /// </summary>
    /// <remarks>
    /// Returns null if no context is set. Use <see cref="RequireCurrent"/> if you need
    /// to throw an exception when no context is available.
    /// </remarks>
    public static IPulseContext? Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value;
    }

    /// <summary>
    /// Gets the current user ID (convenience accessor).
    /// </summary>
    public static string? CurrentUserId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value?.UserId;
    }

    /// <summary>
    /// Gets the current connection ID (convenience accessor).
    /// </summary>
    public static string? CurrentConnectionId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value?.ConnectionId;
    }

    /// <summary>
    /// Gets the current caller ID (convenience accessor).
    /// </summary>
    public static string? CurrentCallerId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value?.CallerId;
    }

    /// <summary>
    /// Gets the current transport connection (convenience accessor).
    /// </summary>
    public static IServerTransport? CurrentTransport
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value?.Transport;
    }

    /// <summary>
    /// Gets whether a valid context exists.
    /// </summary>
    public static bool HasContext
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current.Value != null;
    }

    /// <summary>
    /// Requires that a context exists, throwing if not.
    /// </summary>
    /// <returns>The current context</returns>
    /// <exception cref="InvalidOperationException">When no context is available</exception>
    public static IPulseContext RequireCurrent()
    {
        return _current.Value ?? throw new InvalidOperationException(
            "No request context available. Ensure the request is being processed within a valid context scope.");
    }

    /// <summary>
    /// Sets the current context and returns a scope for automatic cleanup.
    /// </summary>
    /// <param name="context">The context to set</param>
    /// <returns>A disposable scope that restores the previous context when disposed</returns>
    /// <remarks>
    /// <para>
    /// Always use with a using statement to ensure proper cleanup:
    /// </para>
    /// <code>
    /// using (PulseContext.SetContext(context))
    /// {
    ///     // Process request
    /// }
    /// </code>
    /// </remarks>
    public static ContextScope SetContext(IPulseContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new ContextScope(previous);
    }

    /// <summary>
    /// Clears the current context.
    /// </summary>
    /// <remarks>
    /// Generally you should use <see cref="SetContext"/> with a using statement
    /// instead of manually clearing the context.
    /// </remarks>
    public static void Clear()
    {
        _current.Value = null;
    }

    /// <summary>
    /// Context scope for automatic context restoration.
    /// </summary>
    /// <remarks>
    /// This is a value type (struct) to avoid heap allocation.
    /// </remarks>
    public readonly struct ContextScope : IDisposable
    {
        private readonly IPulseContext? _previous;

        internal ContextScope(IPulseContext? previous)
        {
            _previous = previous;
        }

        /// <summary>
        /// Restores the previous context.
        /// </summary>
        public void Dispose()
        {
            _current.Value = _previous;
        }
    }
}
