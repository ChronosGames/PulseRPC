using System;
using MemoryPack;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Serialization;
using PulseRPC.Samples.Shared.Messages;

namespace PulseRPC.Samples.Shared;

/// <summary>
/// MemoryPack 序列化帮助器
/// </summary>
public static class MemoryPackHelpers
{
    /// <summary>
    /// 注册所有消息类型的序列化方法
    /// </summary>
    public static void RegisterAllTypes()
    {
        // 确保 MemoryPack 已为消息类型生成序列化器
        EnsureTypeRegistration<LoginRequest>();
        EnsureTypeRegistration<LoginResponse>();
        EnsureTypeRegistration<RegisterRequest>();
        EnsureTypeRegistration<RegisterResponse>();
        EnsureTypeRegistration<GetUserInfoRequest>();
        EnsureTypeRegistration<GetUserInfoResponse>();
        EnsureTypeRegistration<UpdateUserInfoRequest>();
        EnsureTypeRegistration<UpdateUserInfoResponse>();
        EnsureTypeRegistration<SystemNotification>();
        EnsureTypeRegistration<UserStatusNotification>();
        EnsureTypeRegistration<GlobalBroadcast>();
    }

    /// <summary>
    /// 确保类型已正确注册序列化方法
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    private static void EnsureTypeRegistration<T>() where T : IMessage
    {
        try
        {
            // 创建一个测试对象
            var testObj = Activator.CreateInstance<T>();

            // 尝试序列化
            var bytes = MemoryPackSerializer.Serialize(testObj);

            // 尝试反序列化
            var deser = MemoryPackSerializer.Deserialize<T>(bytes);

            if (deser == null)
            {
                throw new Exception($"反序列化 {typeof(T).Name} 返回了 null");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"类型 {typeof(T).Name} 的序列化/反序列化测试失败: {ex.Message}", ex);
        }
    }
}
