using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PulseRPC.Client.Channels;
using PulseRPC.Events;
using PulseRPC.Messaging;

namespace PulseRPC.Client.Extensions
{
    /// <summary>
    /// 事件订阅扩展方法 - 简化事件处理器注册
    /// </summary>
    public static class EventSubscriptionExtensions
    {
        /// <summary>
        /// 自动注册事件处理器接口的所有事件
        /// </summary>
        /// <typeparam name="T">事件处理器接口类型</typeparam>
        /// <param name="client">RPC客户端</param>
        /// <param name="handler">事件处理器实例</param>
        /// <param name="channelSelector">通道选择器，null则使用默认通道</param>
        /// <returns>组合订阅令牌</returns>
        public static ISubscriptionToken RegisterEventHandler<T>(this IPulseRpcClient client, T handler,
            Func<string, string>? channelSelector = null)
            where T : class
        {
            var subscriptions = new List<ISubscriptionToken>();
            var channelManager = client.GetChannelManager();

            // 获取接口的所有方法
            var methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods)
            {
                // 检查方法是否是事件处理器（约定：On开头，单个参数）
                if (!method.Name.StartsWith("On") || method.GetParameters().Length != 1)
                    continue;

                var parameter = method.GetParameters()[0];
                var eventType = parameter.ParameterType;
                var eventName = method.Name; // 使用方法名作为事件名

                // 选择通道
                var channelName = channelSelector?.Invoke(eventName) ?? "Default";

                try
                {
                    var channel = channelName == "Default" ?
                        channelManager.GetDefaultChannel() :
                        channelManager.GetChannel(channelName);

                    // 创建事件订阅
                    var subscription = SubscribeToEventGeneric(channel, eventName, eventType, handler, method);
                    subscriptions.Add(subscription);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to register event handler for {eventName}: {ex.Message}", ex);
                }
            }

            return new CompositeSubscriptionToken(subscriptions);
        }

        /// <summary>
        /// 使用配置对象注册事件处理器
        /// </summary>
        public static ISubscriptionToken RegisterEventHandler<T>(this IPulseRpcClient client, T handler,
            EventRegistrationConfig config)
            where T : class
        {
            var subscriptions = new List<ISubscriptionToken>();
            var channelManager = client.GetChannelManager();

            foreach (var mapping in config.EventMappings)
            {
                var method = typeof(T).GetMethod(mapping.HandlerMethod);
                if (method == null)
                    throw new InvalidOperationException($"Method {mapping.HandlerMethod} not found on {typeof(T).Name}");

                var eventType = method.GetParameters()[0].ParameterType;
                var channel = channelManager.GetChannel(mapping.ChannelName);

                var subscription = SubscribeToEventGeneric(channel, mapping.EventName, eventType, handler, method);
                subscriptions.Add(subscription);
            }

            return new CompositeSubscriptionToken(subscriptions);
        }

        /// <summary>
        /// 流式API - 开始事件注册链
        /// </summary>
        public static EventRegistrationBuilder<T> RegisterEvents<T>(this IPulseRpcClient client, T handler)
            where T : class
        {
            return new EventRegistrationBuilder<T>(client, handler);
        }

        private static ISubscriptionToken SubscribeToEventGeneric(IClientChannel channel, string eventName,
            Type eventType, object handler, MethodInfo method)
        {
            // 使用反射调用泛型SubscribeToEvent方法
            var subscribeMethod = typeof(IClientChannel).GetMethod("SubscribeToEvent")
                ?.MakeGenericMethod(eventType);

            if (subscribeMethod == null)
                throw new InvalidOperationException("SubscribeToEvent method not found");

            // 创建EventHandler<T>委托
            var eventHandlerType = typeof(System.EventHandler<>).MakeGenericType(eventType);
            var eventHandler = CreateEventHandlerDelegate(handler, method, eventType, eventHandlerType);

            return (ISubscriptionToken)subscribeMethod.Invoke(channel, new object[] { eventName, eventHandler })!;
        }

        private static Delegate CreateEventHandlerDelegate(object handler, MethodInfo method, Type eventType, Type delegateType)
        {
            // 创建lambda表达式：(sender, eventData) => handler.Method(eventData)
            var senderParam = System.Linq.Expressions.Expression.Parameter(typeof(object), "sender");
            var eventDataParam = System.Linq.Expressions.Expression.Parameter(eventType, "eventData");

            var handlerConstant = System.Linq.Expressions.Expression.Constant(handler);
            var methodCall = System.Linq.Expressions.Expression.Call(handlerConstant, method, eventDataParam);

            var lambda = System.Linq.Expressions.Expression.Lambda(delegateType, methodCall, senderParam, eventDataParam);
            return lambda.Compile();
        }
    }

    /// <summary>
    /// 事件注册配置
    /// </summary>
    public class EventRegistrationConfig
    {
        public List<EventMapping> EventMappings { get; set; } = new();

        public EventRegistrationConfig Map<T>(string eventName, string handlerMethod, string channelName = "Default")
        {
            EventMappings.Add(new EventMapping
            {
                EventName = eventName,
                HandlerMethod = handlerMethod,
                ChannelName = channelName,
                EventType = typeof(T)
            });
            return this;
        }
    }

    /// <summary>
    /// 事件映射配置
    /// </summary>
    public class EventMapping
    {
        public string EventName { get; set; } = string.Empty;
        public string HandlerMethod { get; set; } = string.Empty;
        public string ChannelName { get; set; } = "Default";
        public Type EventType { get; set; } = typeof(object);
    }

    /// <summary>
    /// 流式事件注册构建器
    /// </summary>
    public class EventRegistrationBuilder<T> where T : class
    {
        private readonly IPulseRpcClient _client;
        private readonly T _handler;
        private readonly List<EventMapping> _mappings = new();

        internal EventRegistrationBuilder(IPulseRpcClient client, T handler)
        {
            _client = client;
            _handler = handler;
        }

        public EventRegistrationBuilder<T> On<TEvent>(string eventName, Action<TEvent> handlerAction, string channelName = "Default")
        {
            _mappings.Add(new EventMapping
            {
                EventName = eventName,
                HandlerMethod = handlerAction.Method.Name,
                ChannelName = channelName,
                EventType = typeof(TEvent)
            });
            return this;
        }

        public EventRegistrationBuilder<T> OnTcp<TEvent>(string eventName, Action<TEvent> handlerAction)
        {
            return On(eventName, handlerAction, "TcpChannel");
        }

        public EventRegistrationBuilder<T> OnKcp<TEvent>(string eventName, Action<TEvent> handlerAction)
        {
            return On(eventName, handlerAction, "KcpChannel");
        }

        public ISubscriptionToken Build()
        {
            var config = new EventRegistrationConfig();
            foreach (var mapping in _mappings)
            {
                config.EventMappings.Add(mapping);
            }

            return _client.RegisterEventHandler(_handler, config);
        }
    }
}
