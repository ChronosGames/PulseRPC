using System;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using StackExchange.Redis;

namespace ChatApp.Server;

// 服务扩展类 - 用于依赖注入注册
public static class PlayerRepositoryServiceExtensions
{
    public static IServiceCollection AddPlayerRepository(
        this IServiceCollection services, IConfiguration configuration)
    {
        // 注册MongoDB连接
        services.AddSingleton<IMongoClient>(provider =>
        {
            var connectionString = configuration.GetConnectionString("MongoDb");
            return new MongoClient(connectionString);
        });

        services.AddSingleton<IMongoDatabase>(provider =>
        {
            var client = provider.GetRequiredService<IMongoClient>();
            var databaseName = configuration["MongoDB:DatabaseName"];
            return client.GetDatabase(databaseName);
        });

        // 注册Redis连接
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var redisConnectionString = configuration.GetConnectionString("Redis");
            return ConnectionMultiplexer.Connect(redisConnectionString);
        });

        // 注册JSON序列化器
        services.AddSingleton<IJsonSerializer, DefaultJsonSerializer>();

        // 注册仓储实现
        services.AddSingleton<IMongoPlayerRepository, MongoPlayerRepository>();

        // 根据配置选择使用哪种实现
        if (configuration.GetValue<bool>("UseRedisCache"))
        {
            services.AddSingleton<IPlayerRepository, RedisPlayerRepository>();
        }
        else
        {
            services.AddSingleton<IPlayerRepository>(provider =>
                provider.GetRequiredService<IMongoPlayerRepository>());
        }

        return services;
    }
}

// JSON序列化接口和实现
public interface IJsonSerializer
{
    string Serialize<T>(T value);
    T Deserialize<T>(string json);
}

public class DefaultJsonSerializer : IJsonSerializer
{
    private readonly JsonSerializerOptions _options;

    public DefaultJsonSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _options);
    }

    public T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, _options);
    }
}

// 自定义异常类
public class RepositoryException : Exception
{
    public RepositoryException(string message) : base(message) { }

    public RepositoryException(string message, Exception innerException) : base(message, innerException) { }
}

public class PlayerNotFoundException(string message) : Exception(message);

public class EntityNotFoundException(string message) : Exception(message);

public class DuplicateEntityException(string message) : Exception(message);

public class ConcurrencyException(string message) : Exception(message);

public class ResourceLockException(string message) : Exception(message);

public class EndpointNotFoundException(string message) : Exception(message);

public class TransactionNotFoundException(string transactionId) : Exception($"Transaction with ID {transactionId} not found.");

public class InvalidTransactionStateException(string message) : Exception(message);

public class RpcException(string message, Exception innerException) : Exception(message, innerException);
