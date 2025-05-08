using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Network;
using PulseRPC.Server.Examples;
using Xunit;

namespace PulseRPC.Server.Tests;

/// <summary>
/// 动态绑定功能测试
/// </summary>
public class DynamicBindingTests
{
    /// <summary>
    /// 测试消息处理器动态发现和注册
    /// </summary>
    [Fact]
    public void ShouldDiscoverAndRegisterHandlers()
    {
        // 准备
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPulseRpcMessageHandling(scanForHandlers: false);
        services.AddSingleton<TestMessageHandler>();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<MessageDispatcher>();

        // 执行
        dispatcher.RegisterHandler<TestMessage, TestMessageHandler>();

        // 验证
        // 如果注册过程没有抛出异常，则认为测试通过
    }

    /// <summary>
    /// 测试处理器基类提供的类型信息
    /// </summary>
    [Fact]
    public void HandlerBaseShouldProvideCorrectTypeInfo()
    {
        // 准备
        var handler = new TestMessageHandler();

        // 执行
        var typeInfo = handler.GetMessageTypeInfo();

        // 验证
        Assert.Equal(9999, typeInfo.MessageId);
        Assert.Equal(typeof(TestMessage), typeInfo.MessageType);
    }

    /// <summary>
    /// 测试从程序集自动注册处理器
    /// </summary>
    [Fact]
    public void ShouldRegisterHandlersFromAssembly()
    {
        // 准备
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPulseRpcMessageHandling(scanForHandlers: false);

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<MessageDispatcher>();

        // 执行
        dispatcher.RegisterHandlersFromAssembly(typeof(TestMessageHandler).Assembly);

        // 验证
        // 如果注册过程没有抛出异常，则认为测试通过
    }

    /// <summary>
    /// 测试消息分发调用了正确的处理器
    /// </summary>
    [Fact]
    public async Task ShouldDispatchMessageToCorrectHandler()
    {
        // 准备
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddPulseRpcMessageHandling(scanForHandlers: false);

        // 注册测试处理器
        var testHandler = new TestMessageHandler();
        services.AddSingleton<TestMessageHandler>(testHandler);

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<MessageDispatcher>();
        var handlerFactory = provider.GetRequiredService<MessageHandlerFactory>();

        // 注册处理器
        dispatcher.RegisterHandler<TestMessage, TestMessageHandler>();

        // 创建测试消息和上下文
        var message = new TestMessage { Content = "测试内容" };
        var serializedData = Protocol.Serialization.MessageSerializer.Serialize(message);
        var context = new MockSessionContext();

        // 执行
        await dispatcher.DispatchAsync(9999, serializedData, context);

        // 验证
        Assert.True(testHandler.WasHandleCalled);
        Assert.Equal("测试内容", testHandler.LastMessageContent);
    }
}

/// <summary>
/// 测试消息
/// </summary>
[Message(9999)]
public class TestMessage : IMessage
{
    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 测试处理器
/// </summary>
public class TestMessageHandler : MessageHandlerBase<TestMessage>
{
    /// <summary>
    /// 记录处理方法是否被调用
    /// </summary>
    public bool WasHandleCalled { get; private set; }

    /// <summary>
    /// 最后处理的消息内容
    /// </summary>
    public string? LastMessageContent { get; private set; }

    /// <summary>
    /// 处理消息
    /// </summary>
    public override Task HandleAsync(SessionContext context, TestMessage message)
    {
        WasHandleCalled = true;
        LastMessageContent = message.Content;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 测试用的会话上下文，不实际发送消息
/// </summary>
public class MockSessionContext : SessionContext
{
    public MockSessionContext() : base(new System.Net.Sockets.TcpClient())
    {
    }

    public override Task SendAsync<T>(T message)
    {
        // 不实际发送，只返回完成的任务
        return Task.CompletedTask;
    }

    public override Task SendAsync(int messageId, byte[] data)
    {
        // 不实际发送，只返回完成的任务
        return Task.CompletedTask;
    }
}
