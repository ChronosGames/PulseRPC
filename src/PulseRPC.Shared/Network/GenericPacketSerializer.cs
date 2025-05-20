// using System;
// using System.Buffers;
// using System.Buffers.Binary;
// using System.Collections.Concurrent;
// using MemoryPack;
//
// namespace PulseRPC.Serialization;
//
// /// <summary>
// /// 泛型包序列化器 - 支持类型安全的序列化和反序列化
// /// </summary>
// public class GenericPacketSerializer : IPulseRPCSerializer
// {
//     private readonly ConcurrentDictionary<Type, ushort> _typeToIdMap = new();
//     private readonly ConcurrentDictionary<ushort, Type> _idToTypeMap = new();
//     private readonly MemoryPackSerializerOptions _options;
//
//     public GenericPacketSerializer(MemoryPackSerializerOptions? options = null)
//     {
//         _options = options ?? MemoryPackSerializerOptions.Default;
//     }
//
//     /// <summary>
//     /// 注册数据包类型
//     /// </summary>
//     public void RegisterPacketType<T>(ushort typeId) where T : IMemoryPackable<T>
//     {
//         var type = typeof(T);
//         _typeToIdMap.TryAdd(type, typeId);
//         _idToTypeMap.TryAdd(typeId, type);
//     }
//
//     /// <summary>
//     /// 注册数据包类型
//     /// </summary>
//     public void RegisterPacketType(Type type, ushort typeId)
//     {
//         _typeToIdMap.TryAdd(type, typeId);
//         _idToTypeMap.TryAdd(typeId, type);
//     }
//
//     /// <summary>
//     /// 序列化对象到缓冲区
//     /// </summary>
//     public void Serialize<T>(IBufferWriter<byte> writer, in T value) where T : IMemoryPackable<T>
//     {
//         // 获取类型ID
//         if (!_typeToIdMap.TryGetValue(typeof(T), out var typeId))
//         {
//             throw new InvalidOperationException($"类型 {typeof(T).FullName} 未注册");
//         }
//
//         // 写入类型ID
//         var span = writer.GetSpan(2);
//         BinaryPrimitives.WriteUInt16LittleEndian(span, typeId);
//         writer.Advance(2);
//
//         // 序列化对象
//         MemoryPackSerializer.Serialize(writer, value, _options);
//     }
//
//     /// <summary>
//     /// 通用反序列化方法（实现接口）
//     /// </summary>
//     // public object Deserialize(in ReadOnlySpan<byte> bytes)
//     // {
//     //     // 读取类型ID
//     //     var typeId = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
//     //
//     //     // 获取对应的类型
//     //     if (!_idToTypeMap.TryGetValue(typeId, out var type))
//     //     {
//     //         throw new InvalidOperationException($"无法找到类型ID {typeId} 对应的类型");
//     //     }
//     //
//     //     // 使用反射进行反序列化
//     //     var deserializeMethod = typeof(MemoryPackSerializer).GetMethod(nameof(MemoryPackSerializer.Deserialize),
//     //         new[] { typeof(ReadOnlySpan<byte>), typeof(MemoryPackSerializerOptions) })!.MakeGenericMethod(type);
//     //
//     //     return deserializeMethod.Invoke(null, new object[] { bytes[2..], _options })!;
//     // }
//
//     public int ProcessMessage(ref ReadOnlySequence<byte> buffer)
//     {
//         throw new NotImplementedException();
//     }
//
//     /// <summary>
//     /// 强类型反序列化方法（扩展功能）
//     /// </summary>
//     public T Deserialize<T>(in ReadOnlySpan<byte> bytes) where T : IMemoryPackable<T>
//     {
//         // 读取类型ID
//         var typeId = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
//
//         // 验证类型ID是否匹配
//         if (_typeToIdMap.TryGetValue(typeof(T), out var expectedTypeId) && typeId != expectedTypeId)
//         {
//             throw new InvalidOperationException($"类型不匹配: 期望 {typeof(T).Name}(ID={expectedTypeId}), 实际ID={typeId}");
//         }
//
//         // 直接反序列化为指定类型
//         return MemoryPackSerializer.Deserialize<T>(bytes[2..], _options)!;
//     }
// }
