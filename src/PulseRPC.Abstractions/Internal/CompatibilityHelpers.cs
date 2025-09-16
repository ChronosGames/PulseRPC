#if NETSTANDARD2_1
using System;
using System.Diagnostics.CodeAnalysis;

namespace System;

/// <summary>
/// 兼容性帮助器 - 为 .NET Standard 2.1 提供现代 API 的兼容实现
/// </summary>
public static class CompatibilityHelpers
{
    /// <summary>
    /// ObjectDisposedException.ThrowIf 的兼容实现
    /// </summary>
    public static void ThrowIfDisposed(bool disposed, string objectName)
    {
        if (disposed)
            throw new ObjectDisposedException(objectName);
    }

    /// <summary>
    /// ArgumentException.ThrowIfNullOrWhiteSpace 的兼容实现
    /// </summary>
    public static void ThrowIfNullOrWhiteSpace(string? argument, string paramName)
    {
        if (string.IsNullOrWhiteSpace(argument))
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
    }

    /// <summary>
    /// ArgumentNullException.ThrowIfNull 的兼容实现
    /// </summary>
    public static void ThrowIfNull([NotNull] object? argument, string paramName)
    {
        if (argument == null)
            throw new ArgumentNullException(paramName);
    }

    public static void ThrowIfNullOrEmpty(this ArgumentException self, [NotNull] string? argument)
    {
        if (string.IsNullOrEmpty(argument))
            throw new ArgumentException();
    }

    public static void ThrowIfNullOrEmpty(this ArgumentException self, [NotNull] string? argument, string? message)
    {
        if (string.IsNullOrEmpty(argument))
            throw new ArgumentNullException(message);
    }

    /// <summary>Initializes a new instance of the <see cref="T:System.ArgumentException" /> class with a specified error message and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception. If the <paramref name="innerException" /> parameter is not a null reference, the current exception is raised in a <see langword="catch" /> block that handles the inner exception.</param>
    public static void ThrowIfNullOrEmpty(this ArgumentException self, [NotNull] string? argument, string? message, Exception? innerException)
    {
        if (string.IsNullOrEmpty(argument))
            throw new ArgumentException(message, innerException);
    }

    /// <summary>Initializes a new instance of the <see cref="T:System.ArgumentException" /> class with a specified error message and the name of the parameter that causes this exception.</summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="paramName">The name of the parameter that caused the current exception.</param>
    public static void ThrowIfNullOrEmpty(this ArgumentException self, [NotNull] string? argument, string? message, string? paramName)
    {
        if (string.IsNullOrEmpty(argument))
            throw new ArgumentException(message, paramName);
    }

    /// <summary>Initializes a new instance of the <see cref="T:System.ArgumentException" /> class with a specified error message, the parameter name, and a reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="paramName">The name of the parameter that caused the current exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception. If the <paramref name="innerException" /> parameter is not a null reference, the current exception is raised in a <see langword="catch" /> block that handles the inner exception.</param>
    public static void ThrowIfNullOrEmpty(this ArgumentException self, [NotNull] string? argument, string? message,
        string? paramName, Exception? innerException)
    {
        if (string.IsNullOrEmpty(argument))
            throw new ArgumentException(message, paramName, innerException);
    }

    public static void ThrowIfNull(this ArgumentException self, object? argument, string? message)
    {
        if (argument == null)
            throw new ArgumentException(message);
    }
}
#endif
