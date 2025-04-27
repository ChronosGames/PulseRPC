using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityTCP.Serialization;
using UnityTCP.ZeroCopy;

namespace UnityTCP.Examples
{
    /// <summary>
    /// 演示如何将C#对象直接序列化至网卡的示例
    /// </summary>
    public class DirectSerializationExample : MonoBehaviour
    {
        [SerializeField] private string serverIp = "127.0.0.1";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private int messageRate = 60; // 每秒发送消息数

        private ZeroCopyTCPClient _client;
        private float _messageInterval;
        private float _nextMessageTime;

        // 自定义的游戏状态消息结构体
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct GameStateMessage : INetworkSerializable
        {
            public uint PlayerId;
            public Vector3 Position;
            public Quaternion Rotation;
            public byte PlayerState;
            public float Speed;
            public float Timestamp;

            public int GetSerializedSize()
            {
                return sizeof(uint) + // PlayerId
                       3 * sizeof(float) + // Position (Vector3)
                       4 * sizeof(float) + // Rotation (Quaternion)
                       sizeof(byte) + // PlayerState
                       sizeof(float) + // Speed
                       sizeof(float);  // Timestamp
            }

            public void Serialize(ref NetworkWriter writer)
            {
                writer.WriteUInt32(PlayerId);
                writer.WriteVector3(Position);
                writer.WriteQuaternion(Rotation);
                writer.WriteByte(PlayerState);
                writer.WriteFloat(Speed);
                writer.WriteFloat(Timestamp);
            }

            public void Deserialize(ref NetworkReader reader)
            {
                PlayerId = reader.ReadUInt32();
                Position = reader.ReadVector3();
                Rotation = reader.ReadQuaternion();
                PlayerState = reader.ReadByte();
                Speed = reader.ReadFloat();
                Timestamp = reader.ReadFloat();
            }
        }

        // 使用StructLayout特性的Blittable结构体，可以直接进行内存操作
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TransformState
        {
            public uint EntityId;
            public float PosX;
            public float PosY;
            public float PosZ;
            public float RotX;
            public float RotY;
            public float RotZ;
            public float RotW;
            public float ScaleX;
            public float ScaleY;
            public float ScaleZ;
        }

        private void Start()
        {
            _messageInterval = 1.0f / messageRate;
            _nextMessageTime = 0;

            ConnectToServer();
        }

        private async void ConnectToServer()
        {
            try
            {
                _client = new ZeroCopyTCPClient();

                // 订阅事件
                _client.DataReceived += OnDataReceived;
                _client.Disconnected += () => Debug.Log("Disconnected from server");
                _client.ErrorOccurred += ex => Debug.LogError($"Error: {ex.Message}");

                // 连接到服务器
                await _client.ConnectAsync(serverIp, serverPort);
                Debug.Log($"Connected to server: {serverIp}:{serverPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to connect: {ex.Message}");
            }
        }

        private void Update()
        {
            if (_client == null) return;

            // 按指定速率发送消息
            if (Time.time >= _nextMessageTime)
            {
                _nextMessageTime = Time.time + _messageInterval;
                SendGameState();
            }
        }

        private async void SendGameState()
        {
            if (_client == null) return;

            try
            {
                // 方法1: 使用INetworkSerializable接口
                var gameState = new GameStateMessage
                {
                    PlayerId = 123,
                    Position = transform.position,
                    Rotation = transform.rotation,
                    PlayerState = 1, // 假设1=活动状态
                    Speed = 5.0f,
                    Timestamp = Time.realtimeSinceStartup
                };

                // 将对象直接序列化到网络流
                await _client.SendObjectAsync(gameState);

                // 方法2: 使用Blittable结构体和直接内存拷贝
                var transformState = new TransformState
                {
                    EntityId = 456,
                    PosX = transform.position.x,
                    PosY = transform.position.y,
                    PosZ = transform.position.z,
                    RotX = transform.rotation.x,
                    RotY = transform.rotation.y,
                    RotZ = transform.rotation.z,
                    RotW = transform.rotation.w,
                    ScaleX = transform.localScale.x,
                    ScaleY = transform.localScale.y,
                    ScaleZ = transform.localScale.z
                };

                // 使用零拷贝方式发送
                await _client.SendBlittableAsync(transformState);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to send state: {ex.Message}");
            }
        }

        private void OnDataReceived(System.Buffers.ReadOnlySequence<byte> data)
        {
            // 处理接收到的数据
            Debug.Log($"Received {data.Length} bytes");

            // 尝试读取GameStateMessage
            var buffer = data;

            // 从缓冲区中读取所有完整的消息
            while (ZeroCopyNetworkExtensions.TryReadObject(ref buffer, out GameStateMessage message))
            {
                // 处理游戏状态消息
                Debug.Log($"Received state for player {message.PlayerId}");
                // 更新游戏对象...
            }

            // 重置缓冲区位置以读取TransformState消息
            buffer = data;

            // 从缓冲区中读取所有完整的TransformState消息
            while (ZeroCopyNetworkExtensions.TryReadBlittable(ref buffer, out TransformState transformState))
            {
                // 处理变换状态
                Debug.Log($"Received transform for entity {transformState.EntityId}");
                // 更新游戏对象...
            }
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }

        // 演示使用Socket发送结构体的直接方法（不通过流或管道）
        private unsafe void SendStructDirectToSocket<T>(Socket socket, T data) where T : unmanaged
        {
            int size = sizeof(T);
            byte[] buffer = new byte[size + sizeof(int)];

            // 写入长度前缀
            BitConverter.GetBytes(size).CopyTo(buffer, 0);

            // 直接将结构体复制到缓冲区
            fixed (byte* ptr = &buffer[sizeof(int)])
            {
                *(T*)ptr = data;
            }

            // 直接发送到Socket
            socket.Send(buffer);
        }

        // 演示使用固定的发送缓冲区减少内存分配
        private unsafe void SendWithFixedBuffer<T>(Socket socket, T data, byte[] reuseBuffer) where T : unmanaged
        {
            int size = sizeof(T);
            if (reuseBuffer.Length < size + sizeof(int))
            {
                Debug.LogError("Buffer too small");
                return;
            }

            // 写入长度前缀
            BitConverter.GetBytes(size).CopyTo(reuseBuffer, 0);

            // 直接将结构体复制到缓冲区
            fixed (byte* ptr = &reuseBuffer[sizeof(int)])
            {
                *(T*)ptr = data;
            }

            // 发送
            socket.Send(reuseBuffer, 0, size + sizeof(int), SocketFlags.None);
        }
    }
}
