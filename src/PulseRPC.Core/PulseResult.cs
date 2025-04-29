namespace PulseRPC;

/// <summary>
/// Represents the result of an RPC call.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
public readonly struct PulseResult<T>
{
    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the result value if the operation was successful.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; }

    private PulseResult(T? value, bool isSuccess, string? errorMessage)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static PulseResult<T> Success(T value) => new(value, true, null);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static PulseResult<T> Error(string message) => new(default, false, message);

    /// <summary>
    /// Implicit conversion from T to a successful PulseResult<T>.
    /// </summary>
    public static implicit operator PulseResult<T>(T value) => Success(value);
}
