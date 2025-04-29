using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChatApp.Server;

// RPC服务接口
public interface IRpcService {
    Task<T> CallRemoteAsync<T>(string serverId, string endpoint, object request);
    void RegisterEndpoint<TRequest, TResponse>(string endpoint, Func<TRequest, Task<TResponse>> handler);
}

// RPC服务实现
public class RpcService : IRpcService {
    private readonly string _currentServerId;
    private readonly HttpClient _httpClient;
    private readonly IPlayerRoutingService _routingService;
    private readonly ILogger _logger;

    // 本地注册的RPC处理函数
    private readonly Dictionary<string, Func<object, Task<object>>> _handlers =
        new Dictionary<string, Func<object, Task<object>>>();

    public RpcService(
        string currentServerId,
        HttpClient httpClient,
        IPlayerRoutingService routingService,
        ILogger logger) {

        _currentServerId = currentServerId;
        _httpClient = httpClient;
        _routingService = routingService;
        _logger = logger;
    }

    // 调用远程服务
    public async Task<T> CallRemoteAsync<T>(string serverId, string endpoint, object request) {
        // 构建请求URL
        string url = $"http://{serverId}/rpc/{endpoint}";

        try {
            // 序列化请求
            string requestJson = JsonSerializer.Serialize(request);
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            // 发送HTTP请求
            var response = await _httpClient.PostAsync(url, content);

            // 检查响应状态
            response.EnsureSuccessStatusCode();

            // 反序列化响应
            string responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseJson);
        }
        catch (Exception ex) {
            _logger.Error($"RPC call failed to {serverId}/{endpoint}: {ex.Message}");
            throw new RpcException($"Failed to call {endpoint} on {serverId}", ex);
        }
    }

    // 注册本地RPC端点
    public void RegisterEndpoint<TRequest, TResponse>(
        string endpoint, Func<TRequest, Task<TResponse>> handler) {

        _handlers[endpoint] = async (object request) => {
            TRequest typedRequest = (TRequest)request;
            TResponse response = await handler(typedRequest);
            return response;
        };

        _logger.Debug($"Registered RPC endpoint: {endpoint}");
    }

    // 处理传入的RPC请求
    public async Task<object> HandleRequest(string endpoint, string requestJson) {
        if (!_handlers.TryGetValue(endpoint, out var handler)) {
            throw new EndpointNotFoundException($"Endpoint {endpoint} not found");
        }

        try {
            // 反序列化请求
            JsonSerializerOptions options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            Type requestType = handler.Method.GetParameters()[0].ParameterType;
            object request = JsonSerializer.Deserialize(requestJson, requestType, options);

            // 调用处理函数
            object response = await handler(request);
            return response;
        }
        catch (Exception ex) {
            _logger.Error($"Error handling RPC request to {endpoint}: {ex.Message}");
            throw;
        }
    }
}
