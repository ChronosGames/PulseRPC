#if NETSTANDARD2_1
using System;
using System.Diagnostics.CodeAnalysis;

namespace System;

/// <summary>
/// 兼容性帮助器 - 为 .NET Standard 2.1 提供现代 API 的兼容实现
/// </summary>
internal static class CompatibilityHelpers
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

    public static void ThrowIfNull(this ArgumentException self, object? argument, string? message)
    {
        if (argument == null)
            throw new ArgumentException(message);
    }
}
#endif
