using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Microsoft.Extensions.Logging;
using PulseRPC.Network;

namespace PulseRPC.Server;

/// <summary>
/// PulseRPC服务实现 - 统一的消息处理系统
/// </summary>
public class PulseService : IPulseService
{
    // 类型ID映射
    private readonly ConcurrentDictionary<Type, ushort> _typeToIdMap = new();
    private readonly ConcurrentDictionary<ushort, Type> _idToTypeMap = new();

    // 处理器映射
    private readonly ConcurrentDictionary<Type, Delegate> _handlers = new();

    // 请求-响应映射
    private readonly ConcurrentDictionary<Type, Type> _requestResponseMap = new();

    private readonly MemoryPackSerializerOptions _options;
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private bool _isInitialized;

    // 自动分配的类型ID从1开始
    private ushort _nextTypeId = 1;

    /// <summary>
    /// 构造函数
    /// </summary>
    public PulseService(
        ILogger<PulseService> logger,
        IServiceProvider serviceProvider,
        MemoryPackSerializerOptions? options = null)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options ?? MemoryPackSerializerOptions.Default;
    }

    /// <summary>
    /// 初始化服务
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
            return;

        _logger.LogInformation("初始化PulseRPC服务...");

        ScanMemoryPackableTypes();
        ScanHandlers();

        _logger.LogInformation($"PulseRPC服务初始化完成，共注册 {_typeToIdMap.Count} 个类型和 {_handlers.Count} 个处理器");
        _isInitialized = true;
    }

    /// <summary>
    /// 扫描所有MemoryPackable类型
    /// </summary>
    private void ScanMemoryPackableTypes()
    {
        _logger.LogDebug("扫描程序集查找MemoryPackable类型...");

        // 获取所有已加载的程序集
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !ShouldSkipAssembly(a))
            .ToList();

        var processedTypes = new HashSet<string>();

        foreach (var assembly in assemblies)
        {
            try
            {
                foreach (var type in assembly.GetExportedTypes())
                {
                    // 跳过已处理的类型
                    if (processedTypes.Contains(type.FullName ?? string.Empty))
                        continue;

                    // 检查是否有MemoryPackable特性
                    var hasAttribute = type.GetCustomAttribute<MemoryPackableAttribute>() != null;
                    if (!hasAttribute)
                        continue;

                    // 检查是否实现了IMemoryPackable<T>
                    bool isMemoryPackable = false;
                    foreach (var interfaceType in type.GetInterfaces())
                    {
                        if (interfaceType.IsGenericType &&
                            interfaceType.GetGenericTypeDefinition() == typeof(IMemoryPackable<>) &&
                            interfaceType.GetGenericArguments()[0] == type)
                        {
                            isMemoryPackable = true;
                            break;
                        }
                    }

                    if (isMemoryPackable)
                    {
                        // 注册类型
                        RegisterType(type);
                        processedTypes.Add(type.FullName ?? string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"扫描程序集 {assembly.GetName().Name} 时出错");
            }
        }
    }

    /// <summary>
    /// 扫描处理器
    /// </summary>
    private void ScanHandlers()
    {
        // 注意：此处可以实现反射扫描处理器的逻辑
        // 但为了简化，我们使用显式注册模式
    }

    /// <summary>
    /// 检查是否应跳过程序集
    /// </summary>
    private bool ShouldSkipAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;

        return name.StartsWith("System.") ||
               name.StartsWith("Microsoft.") ||
               name.StartsWith("netstandard") ||
               name.StartsWith("mscorlib") ||
               name.Equals("System") ||
               name.Equals("netstandard") ||
               name.Equals("mscorlib");
    }

    /// <summary>
    /// 注册类型
    /// </summary>
    public void RegisterType(Type type)
    {
        var typeId = _nextTypeId++;

        if (!_typeToIdMap.TryAdd(type, typeId) || !_idToTypeMap.TryAdd(typeId, type))
        {
            _logger.LogWarning($"类型 {type.Name} 已注册，忽略");
            return;
        }

        _logger.LogDebug($"已注册类型: {type.Name} (ID={typeId})");
    }

    /// <summary>
    /// 注册双向消息处理器（请求-响应模式）
    /// </summary>
    public void RegisterHandler<TMessage, TResponse>(
        Func<NetworkSession, TMessage, CancellationToken, Task<TResponse>> handler)
        where TMessage : IMemoryPackable<TMessage>
        where TResponse : IMemoryPackable<TResponse>
    {
        var messageType = typeof(TMessage);
        var responseType = typeof(TResponse);

        // 确保类型已注册
        if (!_typeToIdMap.ContainsKey(messageType))
        {
            RegisterType(messageType);
        }

        if (!_typeToIdMap.ContainsKey(responseType))
        {
            RegisterType(responseType);
        }

        // 注册请求-响应映射关系
        _requestResponseMap[messageType] = responseType;

        // 注册处理器
        _handlers[messageType] = handler;

        _logger.LogDebug($"已注册双向消息处理器: {messageType.Name} -> {responseType.Name}");
    }

    /// <summary>
    /// 注册单向消息处理器（命令模式）
    /// </summary>
    public void RegisterHandler<TMessage>(
        Func<NetworkSession, TMessage, CancellationToken, Task> handler)
        where TMessage : IMemoryPackable<TMessage>
    {
        var messageType = typeof(TMessage);

        // 确保类型已注册
        if (!_typeToIdMap.ContainsKey(messageType))
        {
            RegisterType(messageType);
        }

        // 注册处理器
        _handlers[messageType] = handler;

        _logger.LogDebug($"已注册单向消息处理器: {messageType.Name}");
    }

    /// <summary>
    /// 序列化对象
    /// </summary>
    public void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IMemoryPackable<T>
    {
        // 确保已初始化
        if (!_isInitialized)
            Initialize();

        var type = typeof(T);

        // 确保类型已注册
        if (!_typeToIdMap.TryGetValue(type, out var typeId))
        {
            RegisterType(type);
            typeId = _typeToIdMap[type];
        }

        // 写入类型ID
        var span = writer.GetSpan(2);
        BinaryPrimitives.WriteUInt16LittleEndian(span, typeId);
        writer.Advance(2);

        // 序列化对象
        MemoryPackSerializer.Serialize(writer, value, _options);
    }

    /// <summary>
    /// 反序列化对象
    /// </summary>
    public T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : IMemoryPackable<T>
    {
        // 确保已初始化
        if (!_isInitialized)
            Initialize();

        // 跳过类型ID
        return MemoryPackSerializer.Deserialize<T>(bytes[2..], _options)!;
    }

    /// <summary>
    /// 处理消息
    /// </summary>
    public async Task ProcessMessageAsync(
        NetworkSession session,
        ushort sequenceId,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        // 确保已初始化
        if (!_isInitialized)
            Initialize();

        try
        {
            // 读取类型ID
            Span<byte> headerSpan = stackalloc byte[2];
            buffer.Slice(0, 2).CopyTo(headerSpan);
            var typeId = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan);

            // 获取对应的类型
            if (!_idToTypeMap.TryGetValue(typeId, out var messageType))
            {
                _logger.LogWarning($"未知类型ID: {typeId}");
                return;
            }

            // 获取处理器
            if (!_handlers.TryGetValue(messageType, out var handlerObj))
            {
                _logger.LogWarning($"未找到 {messageType.Name} 的处理器");
                return;
            }

            // 反序列化消息
            object message;
            try
            {
                var method = typeof(MemoryPackSerializer).GetMethod(nameof(MemoryPackSerializer.Deserialize), [typeof(ReadOnlySpan<byte>), typeof(MemoryPackSerializerOptions)])!;

                var genericMethod = method.MakeGenericMethod(messageType);
                message = genericMethod.Invoke(null, new object[] { buffer.Slice(2).FirstSpan.ToArray(), _options })!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"反序列化类型 {messageType.Name} 时出错");
                return;
            }

            // 判断处理器类型并调用
            if (_requestResponseMap.TryGetValue(messageType, out var responseType))
            {
                // 双向消息处理器
                var handlerType = handlerObj.GetType();
                var invokeMethod = handlerType.GetMethod("Invoke");
                if (invokeMethod == null)
                {
                    _logger.LogError($"无法获取处理器 {handlerType.Name} 的调用方法");
                    return;
                }

                try
                {
                    // 调用处理器
                    var task = (Task)invokeMethod.Invoke(handlerObj, new[] { session, message, cancellationToken })!;
                    await task;

                    // 获取响应
                    var resultProperty = task.GetType().GetProperty("Result");
                    if (resultProperty == null)
                    {
                        _logger.LogError("无法获取处理结果");
                        return;
                    }

                    var response = resultProperty.GetValue(task);
                    if (response == null)
                    {
                        _logger.LogWarning("处理器返回了空响应");
                        return;
                    }

                    // 发送响应
                    var sendMethod = typeof(NetworkSession).GetMethod("SendPacketAsync")
                        ?.MakeGenericMethod(responseType);

                    if (sendMethod == null)
                    {
                        _logger.LogError("无法获取发送方法");
                        return;
                    }

                    await (Task)sendMethod.Invoke(session, new[] { response, sequenceId })!;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"调用双向消息处理器时出错: {ex.Message}");
                }
            }
            else
            {
                // 单向消息处理器
                var handlerType = handlerObj.GetType();
                var invokeMethod = handlerType.GetMethod("Invoke");
                if (invokeMethod == null)
                {
                    _logger.LogError($"无法获取处理器 {handlerType.Name} 的调用方法");
                    return;
                }

                try
                {
                    // 调用处理器
                    var task = (Task)invokeMethod.Invoke(handlerObj, new[] { session, message, cancellationToken })!;
                    await task;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"调用单向消息处理器时出错: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息时出错");
        }
    }
}
