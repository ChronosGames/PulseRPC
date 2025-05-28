using System;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Events;
using PulseRPC.Server.Transport;
using System.Reflection;

namespace PulseRPC.Server.Events;

/// <summary>
/// 事件发布器接口
/// </summary>
public interface IEventPublisher
{
    Task PublishEventAsync<T>(string eventName, T eventData) where T : IEventData;
}
