using System;
using System.Collections.Concurrent;

namespace PulseRPC.Client;

/// <summary>
/// ProtocolId 计算辅助类
/// 提供与服务端一致的 ProtocolId 计算逻辑
/// </summary>
public static class ProtocolIdHelper
{
    // Channel名称到接口全限定名称的映射
    // 这个映射应该由源生成器自动生成，这里提供手动注册的机制
    private static readonly ConcurrentDictionary<string, string> ChannelToInterfaceMap = new();

    /// <summary>
    /// 注册 Channel 名称到接口全限定名称的映射
    /// </summary>
    /// <param name="channelName">Channel 名称（如 "BackendServer"）</param>
    /// <param name="interfaceFullName">接口全限定名称（如 "DistributedGameApp.Shared.Hubs.IBackendHub"）</param>
    public static void RegisterChannelMapping(string channelName, string interfaceFullName)
    {
        ChannelToInterfaceMap[channelName] = interfaceFullName;
    }

    /// <summary>
    /// 计算协议号 - 使用与服务端相同的 FNV-1a 哈希算法
    /// </summary>
    /// <param name="serviceNameOrChannel">服务名称或 Channel 名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="parameterTypes">参数类型列表（可选）</param>
    /// <returns>协议号</returns>
    public static ushort ComputeProtocolId(string serviceNameOrChannel, string methodName, Type[]? parameterTypes = null)
    {
        // 尝试将 Channel 名称转换为接口全限定名称
        var interfaceName = ChannelToInterfaceMap.TryGetValue(serviceNameOrChannel, out var mapped)
            ? mapped
            : serviceNameOrChannel;

        // 构造方法签名：InterfaceFullName.MethodName(ParamType1,ParamType2,...)
        var signature = BuildMethodSignature(interfaceName, methodName, parameterTypes);

        // 使用 FNV-1a 哈希算法（与服务端一致）
        const uint FnvPrime = 0x01000193;
        const uint FnvOffsetBasis = 0x811C9DC5;

        var hash = FnvOffsetBasis;
        foreach (var c in signature)
        {
            hash ^= c;
            hash *= FnvPrime;
        }

        return (ushort)(hash & 0xFFFF);
    }

    private static string BuildMethodSignature(string interfaceName, string methodName, Type[]? parameterTypes)
    {
        var signature = $"{interfaceName}.{methodName}(";

        if (parameterTypes != null && parameterTypes.Length > 0)
        {
            // 过滤掉 CancellationToken 参数
            var filteredParams = parameterTypes
                .Where(t => t != typeof(System.Threading.CancellationToken))
                .Select(t => t.FullName ?? t.Name)
                .ToArray();

            if (filteredParams.Length > 0)
            {
                signature += string.Join(",", filteredParams);
            }
        }

        signature += ")";
        return signature;
    }
}
