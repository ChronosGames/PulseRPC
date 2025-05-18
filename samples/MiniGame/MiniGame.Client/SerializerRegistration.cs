using System;
using System.Collections.Generic;
using MemoryPack;
using MemoryPack.Formatters;
using PulseRPC.Samples.Shared.Messages;

namespace MiniGame.Client;

/// <summary>
/// 序列化器注册帮助类
/// </summary>
internal static class SerializerRegistration
{
    /// <summary>
    /// 注册所有序列化器
    /// </summary>
    public static void RegisterAll()
    {
        // 注册自定义的响应类型序列化器
        RegisterResponseSerializers();

        // 注册临时对象序列化器
        RegisterTemporaryObjects();
    }

    /// <summary>
    /// 注册响应类型序列化器
    /// </summary>
    private static void RegisterResponseSerializers()
    {
        // 注册AuthStreamingHub的响应类型序列化器
        MemoryPackFormatterProvider.Register(new DelegateFormatter<IAuthStreamingHub_Register_Response>());
        new FormatterRegister<IAuthStreamingHub_Register_Response>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<IAuthStreamingHub_Login_Response>());
        new FormatterRegister<IAuthStreamingHub_Login_Response>().RegisterFormatter();

        // 注册UserStreamingHub的响应类型序列化器
        MemoryPackFormatterProvider.Register(new DelegateFormatter<IUserStreamingHub_GetUserInfoAsync_Request>());
        new FormatterRegister<IUserStreamingHub_GetUserInfoAsync_Request>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<IUserStreamingHub_GetUserInfoAsync_Response>());
        new FormatterRegister<IUserStreamingHub_GetUserInfoAsync_Response>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<IUserStreamingHub_UpdateUserInfoAsync_Response>());
        new FormatterRegister<IUserStreamingHub_UpdateUserInfoAsync_Response>().RegisterFormatter();

        // 注册GameStreamingHub的响应类型序列化器
        MemoryPackFormatterProvider.Register(new DelegateFormatter<GameStreamingHub_GetGameStatusAsync_Request>());
        new FormatterRegister<GameStreamingHub_GetGameStatusAsync_Request>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<GameStreamingHub_GetGameStatusAsync_Response>());
        new FormatterRegister<GameStreamingHub_GetGameStatusAsync_Response>().RegisterFormatter();

        // 注册INotificationReceiver的请求响应类型序列化器
        MemoryPackFormatterProvider.Register(new DelegateFormatter<INotificationReceiver_SubscribeNotificationsAsync_Request>());
        new FormatterRegister<INotificationReceiver_SubscribeNotificationsAsync_Request>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<INotificationReceiver_SubscribeNotificationsAsync_Response>());
        new FormatterRegister<INotificationReceiver_SubscribeNotificationsAsync_Response>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<INotificationReceiver_UnsubscribeNotificationsAsync_Request>());
        new FormatterRegister<INotificationReceiver_UnsubscribeNotificationsAsync_Request>().RegisterFormatter();

        MemoryPackFormatterProvider.Register(new DelegateFormatter<INotificationReceiver_UnsubscribeNotificationsAsync_Response>());
        new FormatterRegister<INotificationReceiver_UnsubscribeNotificationsAsync_Response>().RegisterFormatter();
    }

    /// <summary>
    /// 注册临时对象序列化器
    /// </summary>
    private static void RegisterTemporaryObjects()
    {
        // 此方法可用于注册额外的临时对象序列化器
        // 如果有更多需要注册的序列化器，可以在这里添加
    }
}

/// <summary>
/// 委托格式化器，用于没有实现序列化接口的类型
/// </summary>
/// <typeparam name="T">格式化类型</typeparam>
public class DelegateFormatter<T> : MemoryPackFormatter<T>
{
    /// <summary>
    /// 序列化对象的方法实现
    /// </summary>
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref T? value)
    {
        // 简单实现序列化逻辑，仅写入类型名
        // 实际项目中应该根据类型实现更复杂的序列化
        writer.WriteString(typeof(T).Name);
    }

    /// <summary>
    /// 反序列化对象的方法实现
    /// </summary>
    public override void Deserialize(ref MemoryPackReader reader, scoped ref T? value)
    {
        // 简单实现反序列化逻辑，仅读取类型名
        // 实际项目中应该根据类型实现更复杂的反序列化
        reader.ReadString();
        value = Activator.CreateInstance<T>();
    }
}

/// <summary>
/// IMemoryPackFormatterRegister接口的实现类
/// </summary>
/// <typeparam name="T">格式化类型</typeparam>
public class FormatterRegister<T> : IMemoryPackFormatterRegister
{
    // 实现接口方法
    public void RegisterFormatter()
    {
        // 注册自定义格式化器
        MemoryPackFormatterProvider.Register(new DelegateFormatter<T>());
    }
}
