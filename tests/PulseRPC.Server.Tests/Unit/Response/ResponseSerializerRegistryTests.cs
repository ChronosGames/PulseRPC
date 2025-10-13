using System;
using System.Buffers;
using Xunit;
using PulseRPC.Serialization;
using MemoryPack;

namespace PulseRPC.Server.Tests.Unit.Response;

/// <summary>
/// 测试生成的响应序列化器注册表功能
/// </summary>
public class ResponseSerializerRegistryTests
{
    [Fact]
    public void EmptyRegistry_TryGetSerializer_ReturnsFalse()
    {
        // Arrange
        var registry = EmptyResponseSerializerRegistry.Instance;

        // Act
        var result = registry.TryGetSerializer("TestService", "TestMethod", out var serializer);

        // Assert
        Assert.False(result);
        Assert.Null(serializer);
    }

    [Fact]
    public void EmptyRegistry_EnumerateSerializers_ReturnsEmpty()
    {
        // Arrange
        var registry = EmptyResponseSerializerRegistry.Instance;

        // Act
        var serializers = registry.EnumerateSerializers();

        // Assert
        Assert.True(serializers.IsEmpty);
    }

    [Fact]
    public void ResponseSerializer_Serialize_WithNullResponse_ThrowsArgumentNullException()
    {
        // Arrange
        var serializer = new TestResponseSerializer();
        var writer = new ArrayBufferWriter<byte>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => serializer.Serialize(null!, writer));
    }

    [Fact]
    public void ResponseSerializer_Serialize_WithValidResponse_Succeeds()
    {
        // Arrange
        var serializer = new TestResponseSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var response = new TestResponse { Value = "test" };

        // Act
        serializer.Serialize(response, writer);

        // Assert
        Assert.True(writer.WrittenCount > 0);
    }

    [Fact]
    public void ResponseSerializer_SerializeAsync_WithValidResponse_Succeeds()
    {
        // Arrange
        var serializer = new TestResponseSerializer();
        var writer = new ArrayBufferWriter<byte>();
        var response = new TestResponse { Value = "test" };

        // Act
        var task = serializer.SerializeAsync(response, writer);

        // Assert
        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(writer.WrittenCount > 0);
    }

    [Fact]
    public void ResponseSerializer_TryGetTypedSerializer_WithMatchingType_ReturnsTrue()
    {
        // Arrange
        var serializer = new TestResponseSerializer();

        // Act
        var result = serializer.TryGetTypedSerializer<TestResponse>(out var typedSerializer);

        // Assert
        Assert.True(result);
        Assert.NotNull(typedSerializer);
    }

    [Fact]
    public void ResponseSerializer_TryGetTypedSerializer_WithNonMatchingType_ReturnsFalse()
    {
        // Arrange
        var serializer = new TestResponseSerializer();

        // Act
        var result = serializer.TryGetTypedSerializer<string>(out var typedSerializer);

        // Assert
        Assert.False(result);
        Assert.Null(typedSerializer);
    }

    [Fact]
    public void TypedSerializer_Serialize_ProducesCorrectOutput()
    {
        // Arrange
        var serializer = new TestResponseSerializer();
        serializer.TryGetTypedSerializer<TestResponse>(out var typedSerializer);
        var writer = new ArrayBufferWriter<byte>();
        var response = new TestResponse { Value = "typed test" };

        // Act
        typedSerializer!(response, writer);

        // Assert
        Assert.True(writer.WrittenCount > 0);
        
        // 验证可以反序列化回来
        var deserialized = MemoryPackSerializer.Deserialize<TestResponse>(writer.WrittenSpan);
        Assert.NotNull(deserialized);
        Assert.Equal("typed test", deserialized.Value);
    }
}

/// <summary>
/// 测试用响应序列化器实现
/// </summary>
internal sealed class TestResponseSerializer : IResponseSerializer
{
    public string ServiceName => "TestService";
    public string MethodName => "TestMethod";

    public void Serialize(object response, IBufferWriter<byte> writer)
    {
        if (response is null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if (response is TestResponse typed)
        {
            MemoryPackSerializer.Serialize(writer, typed);
            return;
        }

        throw new InvalidCastException($"期待类型 {typeof(TestResponse)} 但接收到 {response.GetType()}");
    }

    public ValueTask SerializeAsync(object response, IBufferWriter<byte> writer, System.Threading.CancellationToken cancellationToken = default)
    {
        Serialize(response, writer);
        return ValueTask.CompletedTask;
    }

    public bool TryGetTypedSerializer<T>(out Action<T, IBufferWriter<byte>> serializer)
    {
        if (typeof(T) == typeof(TestResponse))
        {
            serializer = static (value, bufferWriter) =>
            {
                MemoryPackSerializer.Serialize(bufferWriter, System.Runtime.CompilerServices.Unsafe.As<T, TestResponse>(ref value));
            };
            return true;
        }

        serializer = null!;
        return false;
    }
}

/// <summary>
/// 测试用响应类型
/// </summary>
[MemoryPackable]
internal partial class TestResponse
{
    public string? Value { get; set; }
}

