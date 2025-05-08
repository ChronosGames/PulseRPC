using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PulseRPC.Protocol;
using PulseRPC.Protocol.Attributes;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Server.MessageRegistration;

/// <summary>
/// 服务端消息注册器
/// </summary>
public static class ServerMessageRegistration
{
    private static bool _isInitialized;
    private static readonly object _lock = new();

    /// <summary>
    /// 模块初始化器，程序集加载时自动执行
    /// </summary>
    internal static void Initialize()
    {
        // 确保只初始化一次
        if (_isInitialized)
            return;

        lock (_lock)
        {
            if (_isInitialized)
                return;

            // 获取所有已加载的程序集
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                // 排除系统程序集和第三方程序集
                .Where(a => !a.IsDynamic &&
                           !a.FullName!.StartsWith("System.") &&
                           !a.FullName!.StartsWith("Microsoft.") &&
                           !a.FullName!.StartsWith("netstandard"));

            // 注册所有消息类型
            foreach (var assembly in assemblies)
            {
                try
                {
                    RegisterAssemblyMessages(assembly);
                }
                catch
                {
                    // 忽略注册失败的程序集
                }
            }

            // 注册程序集加载事件，处理动态加载的程序集
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            {
                var assembly = args.LoadedAssembly;
                if (!assembly.IsDynamic &&
                    !assembly.FullName!.StartsWith("System.") &&
                    !assembly.FullName!.StartsWith("Microsoft.") &&
                    !assembly.FullName!.StartsWith("netstandard"))
                {
                    try
                    {
                        RegisterAssemblyMessages(assembly);
                    }
                    catch
                    {
                        // 忽略注册失败的程序集
                    }
                }
            };

            _isInitialized = true;
        }
    }

    /// <summary>
    /// 注册单个程序集中的消息类型
    /// </summary>
    /// <param name="assembly">要扫描的程序集</param>
    /// <param name="logger">日志记录器</param>
    private static void RegisterAssemblyMessages(Assembly assembly, ILogger? logger = null)
    {
        logger?.LogInformation("开始扫描程序集 {Assembly} 中的消息类型", assembly.FullName);

        try
        {
            // 获取所有实现了IMessage接口的类型
            var messageTypes = assembly.GetTypes()
                .Where(t => typeof(IMessage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in messageTypes)
            {
                try
                {
                    // 获取Message特性
                    var attr = type.GetCustomAttribute<MessageAttribute>();
                    if (attr != null)
                    {
                        MessageRegistry.RegisterMessageType(attr.Id, type);
                        logger?.LogDebug("已注册消息类型: {Type} (ID: {MessageId})", type.FullName, attr.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "注册消息类型 {Type} 时发生错误", type.FullName);
                }
            }

            logger?.LogInformation("程序集 {Assembly} 的消息类型扫描完成", assembly.FullName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "扫描程序集 {Assembly} 时发生错误", assembly.FullName);
        }
    }
}
