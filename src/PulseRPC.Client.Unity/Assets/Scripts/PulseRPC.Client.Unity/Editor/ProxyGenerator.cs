using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PulseRPC.Editor
{
    /// <summary>
    /// 代理类生成工具
    /// </summary>
    public static class ProxyGenerator
    {
        private const string GeneratedDirectory = "Assets/Scripts/PulseRPC.Client.Unity/Generated";
        private const string GeneratedNamespace = "PulseRPC.Generated";

        /// <summary>
        /// 生成代理类
        /// </summary>
        [MenuItem("PulseRPC/Generate Proxies")]
        public static void GenerateProxies()
        {
            try
            {
                // 确保目录存在
                if (!Directory.Exists(GeneratedDirectory))
                {
                    Directory.CreateDirectory(GeneratedDirectory);
                }

                // 获取所有标有 PulseClientGeneration 特性的类型
                var interfaceTypes = FindInterfacesWithAttribute();
                if (interfaceTypes.Length == 0)
                {
                    Debug.LogWarning("找不到任何标有 PulseClientGeneration 特性的接口");
                    return;
                }

                int generatedCount = 0;
                foreach (var interfaceType in interfaceTypes)
                {
                    // 生成代理类
                    var className = GetServiceName(interfaceType);
                    var fileName = $"{className}Proxy.cs";
                    var filePath = Path.Combine(GeneratedDirectory, fileName);

                    var code = GenerateProxyClass(interfaceType, className);
                    File.WriteAllText(filePath, code);
                    generatedCount++;

                    Debug.Log($"已生成代理类: {fileName}");
                }

                // 刷新资源
                AssetDatabase.Refresh();

                Debug.Log($"代理类生成完成，共 {generatedCount} 个");
            }
            catch (Exception ex)
            {
                Debug.LogError($"生成代理类失败: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 查找标有 PulseClientGeneration 特性的接口
        /// </summary>
        private static Type[] FindInterfacesWithAttribute()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => t.IsInterface &&
                       t.GetCustomAttributes(true)
                        .Any(attr => attr.GetType().Name == "PulseClientGenerationAttribute"))
                .ToArray();
        }

        /// <summary>
        /// 获取服务名称
        /// </summary>
        private static string GetServiceName(Type interfaceType)
        {
            var name = interfaceType.Name;
            if (name.StartsWith("I"))
                name = name.Substring(1);
            return name;
        }

        /// <summary>
        /// 生成代理类代码
        /// </summary>
        private static string GenerateProxyClass(Type interfaceType, string className)
        {
            var sb = new StringBuilder();

            // 添加 using 指令
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using PulseRPC.Client.Channels;");
            sb.AppendLine("using PulseRPC.Serialization;");
            sb.AppendLine();

            // 命名空间
            sb.AppendLine($"namespace {GeneratedNamespace}");
            sb.AppendLine("{");

            // 类定义
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// {className} 代理类");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    [Serializable]");
            sb.AppendLine($"    public class {className}Proxy : {interfaceType.FullName}");
            sb.AppendLine("    {");

            // 字段
            sb.AppendLine("        private readonly IMessageChannel _channel;");
            sb.AppendLine();

            // 构造函数
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// 构造函数");
            sb.AppendLine($"        /// </summary>");
            sb.AppendLine("        public " + className + "Proxy(IMessageChannel channel)");
            sb.AppendLine("        {");
            sb.AppendLine("            _channel = channel ?? throw new ArgumentNullException(nameof(channel));");
            sb.AppendLine("        }");
            sb.AppendLine();

            // 方法
            var methods = interfaceType.GetMethods();
            foreach (var method in methods)
            {
                GenerateMethod(sb, method, className);
            }

            // 结束类定义
            sb.AppendLine("    }");

            // 结束命名空间
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 生成方法代码
        /// </summary>
        private static void GenerateMethod(StringBuilder sb, MethodInfo method, string className)
        {
            // 方法注释
            sb.AppendLine($"        /// <summary>");
            sb.AppendLine($"        /// {method.Name} 方法");
            sb.AppendLine($"        /// </summary>");

            // 参数注释
            foreach (var param in method.GetParameters())
            {
                sb.AppendLine($"        /// <param name=\"{param.Name}\">{param.Name} 参数</param>");
            }

            // 返回值注释
            if (method.ReturnType != typeof(void))
            {
                sb.AppendLine($"        /// <returns>返回值</returns>");
            }

            // 方法签名
            var returnTypeName = GetTypeName(method.ReturnType);
            sb.Append($"        public {returnTypeName} {method.Name}(");

            // 方法参数
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramTypeName = GetTypeName(param.ParameterType);
                sb.Append($"{paramTypeName} {param.Name}");

                if (i < parameters.Length - 1)
                    sb.Append(", ");
            }
            sb.AppendLine(")");

            // 方法实现
            sb.AppendLine("        {");

            // 根据返回类型生成不同的实现
            if (IsTaskType(method.ReturnType))
            {
                GenerateAsyncMethodBody(sb, method, className);
            }
            else
            {
                GenerateSyncMethodBody(sb, method, className);
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        /// <summary>
        /// 生成异步方法体
        /// </summary>
        private static void GenerateAsyncMethodBody(StringBuilder sb, MethodInfo method, string className)
        {
            // 创建请求对象
            sb.AppendLine($"            // 创建请求对象");
            sb.AppendLine($"            var request = new {method.Name}Request");
            sb.AppendLine("            {");

            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                sb.Append($"                {CapitalizeFirst(param.Name)} = {param.Name}");

                if (i < parameters.Length - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("            };");
            sb.AppendLine();

            // 序列化请求
            sb.AppendLine($"            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                // 序列化请求");
            sb.AppendLine($"                var serializer = new PulseRPCSerializer();");
            sb.AppendLine($"                var requestData = serializer.Serialize(request);");
            sb.AppendLine();

            // 发送请求
            sb.AppendLine($"                // 发送请求");
            sb.AppendLine($"                var responseData = await _channel.SendRequestAsync(\"{className}\", \"{method.Name}\", requestData);");
            sb.AppendLine();

            // 获取返回类型
            Type actualReturnType = GetTaskInnerType(method.ReturnType);
            string responseTypeName = "void";

            if (actualReturnType != typeof(void))
            {
                responseTypeName = actualReturnType.Name + "Response";

                // 反序列化响应
                sb.AppendLine($"                // 反序列化响应");
                sb.AppendLine($"                var response = serializer.Deserialize<{responseTypeName}>(responseData);");
                sb.AppendLine($"                return response.Result;");
            }
            else
            {
                // 无返回值的情况
                sb.AppendLine($"                return Task.CompletedTask;");
            }

            // 异常处理
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine($"                throw new Exception($\"调用 {method.Name} 失败: {{ex.Message}}\", ex);");
            sb.AppendLine("            }");
        }

        /// <summary>
        /// 生成同步方法体
        /// </summary>
        private static void GenerateSyncMethodBody(StringBuilder sb, MethodInfo method, string className)
        {
            // 创建请求对象
            sb.AppendLine($"            // 创建请求对象");
            sb.AppendLine($"            var request = new {method.Name}Request");
            sb.AppendLine("            {");

            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                sb.Append($"                {CapitalizeFirst(param.Name)} = {param.Name}");

                if (i < parameters.Length - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("            };");
            sb.AppendLine();

            // 序列化请求
            sb.AppendLine($"            try");
            sb.AppendLine("            {");
            sb.AppendLine($"                // 序列化请求");
            sb.AppendLine($"                var serializer = new PulseRPCSerializer();");
            sb.AppendLine($"                var requestData = serializer.Serialize(request);");
            sb.AppendLine();

            // 发送请求
            sb.AppendLine($"                // 同步发送请求 (实际上是异步调用的阻塞版本)");
            sb.AppendLine($"                var responseData = _channel.SendRequestAsync(\"{className}\", \"{method.Name}\", requestData).GetAwaiter().GetResult();");
            sb.AppendLine();

            // 返回值处理
            if (method.ReturnType != typeof(void))
            {
                // 反序列化响应
                sb.AppendLine($"                // 反序列化响应");
                sb.AppendLine($"                var response = serializer.Deserialize<{method.ReturnType.Name}Response>(responseData);");
                sb.AppendLine($"                return response.Result;");
            }

            // 异常处理
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine($"                throw new Exception($\"调用 {method.Name} 失败: {{ex.Message}}\", ex);");
            sb.AppendLine("            }");
        }

        /// <summary>
        /// 获取类型名称
        /// </summary>
        private static string GetTypeName(Type type)
        {
            if (type == typeof(void))
                return "void";

            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                if (genericType == typeof(Task<>))
                {
                    var innerType = type.GetGenericArguments()[0];
                    return $"Task<{GetTypeName(innerType)}>";
                }
                else if (genericType == typeof(Task))
                {
                    return "Task";
                }
                else
                {
                    var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetTypeName));
                    return $"{type.Name.Split('`')[0]}<{genericArgs}>";
                }
            }

            return type.Name;
        }

        /// <summary>
        /// 判断是否为 Task 类型
        /// </summary>
        private static bool IsTaskType(Type type)
        {
            if (type == typeof(Task))
                return true;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
                return true;

            return false;
        }

        /// <summary>
        /// 获取 Task 的内部类型
        /// </summary>
        private static Type GetTaskInnerType(Type taskType)
        {
            if (taskType == typeof(Task))
                return typeof(void);

            if (taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>))
                return taskType.GetGenericArguments()[0];

            return typeof(void);
        }

        /// <summary>
        /// 首字母大写
        /// </summary>
        private static string CapitalizeFirst(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}
