using UnityEngine;
using PulseRPC.AOT;
using PulseRPC.Transport;
using PulseRPC.Serialization;
using PulseRPC.Client.Channels;

namespace PulseRPC.Client.Unity.Examples
{
    /// <summary>
    /// AOT 支持测试脚本
    /// </summary>
    public class AOTTestScript : MonoBehaviour
    {
        void Start()
        {
            Debug.Log("[AOTTest] 开始测试 AOT 支持...");

            // 测试基本类型
            TestBasicTypes();

            // 测试传输选项
            TestTransportOptions();

            // 测试序列化器
            TestSerializer();

            // 测试传输通道
            TestTransportChannel();

            Debug.Log("[AOTTest] AOT 支持测试完成");
        }

        void TestBasicTypes()
        {
            Debug.Log("[AOTTest] 测试基本类型创建...");

            // 测试传输类型枚举
            var transportType = TransportType.Tcp;
            Debug.Log($"[AOTTest] 传输类型: {transportType}");
        }

        void TestTransportOptions()
        {
            Debug.Log("[AOTTest] 测试传输选项...");

            var options = new TransportOptions
            {
                AutoReconnect = true,
                ReconnectInterval = 5000,
                MaxReconnectAttempts = 3
            };

            Debug.Log($"[AOTTest] 传输选项创建成功 - 自动重连: {options.AutoReconnect}");
        }

        void TestSerializer()
        {
            Debug.Log("[AOTTest] 测试序列化器...");

            var serializer = new PulseRPCSerializer();
            var testString = "Hello PulseRPC";

            try
            {
                var serialized = serializer.Serialize(testString);
                var deserialized = serializer.Deserialize<string>(serialized);
                Debug.Log($"[AOTTest] 序列化测试成功 - 原始: {testString}, 反序列化: {deserialized}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AOTTest] 序列化测试失败: {ex.Message}");
            }
        }

        void TestTransportChannel()
        {
            Debug.Log("[AOTTest] 测试传输通道...");

            try
            {
                // 创建一个模拟的传输和序列化器用于测试
                var serializer = new PulseRPCSerializer();
                var transportFactory = new TransportFactory();

                // 注意：这里只是创建对象进行AOT测试，不进行实际连接
                Debug.Log("[AOTTest] 传输通道相关类型创建测试完成");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AOTTest] 传输通道测试失败: {ex.Message}");
            }
        }
    }
}
