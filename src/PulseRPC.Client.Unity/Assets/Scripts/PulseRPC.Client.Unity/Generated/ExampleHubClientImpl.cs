using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PulseRPC.Protocol;
using PulseRPC.Client.Unity;
using UnityEngine;

namespace PulseRPC.Client.Unity.Generated
{
    // 注意：这是一个示例生成的Hub代理类，实际代码会由代码生成器生成
    public class ExampleHubClientImpl : IExampleHub
    {
        private readonly IPulseConnection _connection;
        private readonly IExampleHubReceiver _receiver;
        private readonly string _hubId;

        public ExampleHubClientImpl(IPulseConnection connection, IExampleHubReceiver receiver)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            _hubId = Guid.NewGuid().ToString();

            // 订阅事件
            _connection.OnEventReceived += HandleEventAsync;

            // 注册Hub
            _ = RegisterHubAsync();
        }

        private async Task RegisterHubAsync()
        {
            try
            {
                // 创建Hub注册请求
                var request = new PulseRequest
                {
                    RequestId = Guid.NewGuid(),
                    ServiceName = "ExampleHub",
                    MethodName = "__connect",
                    Parameters = MemoryPackSerializer.Serialize(new object[] { _hubId })
                };

                // 发送请求并等待响应
                await _connection.SendRequestAsync(request);

                Debug.Log($"已成功注册Hub: {_hubId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"注册Hub时出错: {ex.Message}");
                throw;
            }
        }

        private async Task HandleEventAsync(PulseEvent eventData)
        {
            // 仅处理针对此Hub的事件
            if (eventData.HubId != _hubId) return;

            try
            {
                switch (eventData.MethodName)
                {
                    case "OnMessage":
                        var message = MemoryPackSerializer.Deserialize<string>(eventData.Parameters);
                        await _receiver.OnMessageAsync(message);
                        break;

                    case "OnUserJoined":
                        var user = MemoryPackSerializer.Deserialize<string>(eventData.Parameters);
                        await _receiver.OnUserJoinedAsync(user);
                        break;

                    case "OnUserLeft":
                        var userId = MemoryPackSerializer.Deserialize<string>(eventData.Parameters);
                        await _receiver.OnUserLeftAsync(userId);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理Hub事件时出错: {ex.Message}");
            }
        }

        // Hub方法实现
        public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            try
            {
                // 创建请求
                var request = new PulseRequest
                {
                    RequestId = Guid.NewGuid(),
                    ServiceName = "ExampleHub",
                    MethodName = "SendMessage",
                    HubId = _hubId,
                    Parameters = MemoryPackSerializer.Serialize(new object[] { message })
                };

                // 发送请求并等待响应
                await _connection.SendRequestAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"调用SendMessageAsync时出错: {ex.Message}");
                throw;
            }
        }

        public async Task JoinRoomAsync(string roomName, CancellationToken cancellationToken = default)
        {
            try
            {
                // 创建请求
                var request = new PulseRequest
                {
                    RequestId = Guid.NewGuid(),
                    ServiceName = "ExampleHub",
                    MethodName = "JoinRoom",
                    HubId = _hubId,
                    Parameters = MemoryPackSerializer.Serialize(new object[] { roomName })
                };

                // 发送请求并等待响应
                await _connection.SendRequestAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"调用JoinRoomAsync时出错: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> GetUsersInRoomAsync(string roomName, CancellationToken cancellationToken = default)
        {
            try
            {
                // 创建请求
                var request = new PulseRequest
                {
                    RequestId = Guid.NewGuid(),
                    ServiceName = "ExampleHub",
                    MethodName = "GetUsersInRoom",
                    HubId = _hubId,
                    Parameters = MemoryPackSerializer.Serialize(new object[] { roomName })
                };

                // 发送请求并等待响应
                var response = await _connection.SendRequestAsync(request, cancellationToken);

                // 反序列化结果
                if (response.ReturnValue != null && response.ReturnValue.Length > 0)
                {
                    return MemoryPackSerializer.Deserialize<List<string>>(response.ReturnValue);
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"调用GetUsersInRoomAsync时出错: {ex.Message}");
                throw;
            }
        }
    }

    // Hub接口定义 - 实际上会是一个共享的接口
    public interface IExampleHub : IPulseHub<IExampleHub, IExampleHubReceiver>
    {
        Task SendMessageAsync(string message, CancellationToken cancellationToken = default);
        Task JoinRoomAsync(string roomName, CancellationToken cancellationToken = default);
        Task<List<string>> GetUsersInRoomAsync(string roomName, CancellationToken cancellationToken = default);
    }

    // 接收器接口定义 - 实际上会是一个共享的接口
    public interface IExampleHubReceiver
    {
        Task OnMessageAsync(string message);
        Task OnUserJoinedAsync(string user);
        Task OnUserLeftAsync(string userId);
    }
}
