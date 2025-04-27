using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace UnityTCP.MemoryPackModels
{
    /// <summary>
    /// MemoryPack序列化示例模型
    /// </summary>
    [MemoryPack.MemoryPackable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public partial struct PlayerState
    {
        // 玩家标识
        public uint PlayerId { get; set; }

        // 位置
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        // 旋转
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }

        // 状态标志
        public byte PlayerStateFlags { get; set; }

        // 运动参数
        public float Speed { get; set; }
        public float Timestamp { get; set; }

        // 创建自Unity的Transform
        public static PlayerState FromTransform(Transform transform, uint playerId)
        {
            return new PlayerState
            {
                PlayerId = playerId,
                PositionX = transform.position.x,
                PositionY = transform.position.y,
                PositionZ = transform.position.z,
                RotationX = transform.rotation.x,
                RotationY = transform.rotation.y,
                RotationZ = transform.rotation.z,
                RotationW = transform.rotation.w,
                PlayerStateFlags = 1, // 活动状态
                Speed = 0, // 默认速度
                Timestamp = Time.realtimeSinceStartup
            };
        }

        // 应用到Unity的Transform
        public void ApplyToTransform(Transform transform)
        {
            transform.position = new Vector3(PositionX, PositionY, PositionZ);
            transform.rotation = new Quaternion(RotationX, RotationY, RotationZ, RotationW);
        }
    }

    /// <summary>
    /// 游戏世界状态，包含多个实体的状态
    /// </summary>
    [MemoryPack.MemoryPackable]
    public partial class WorldState
    {
        // 时间戳
        public float ServerTime { get; set; }

        // 世界状态ID
        public ulong StateId { get; set; }

        // 玩家状态列表
        public PlayerState[] Players { get; set; }

        // 构造函数
        public WorldState()
        {
            Players = Array.Empty<PlayerState>();
        }

        // 创建新的世界状态
        public static WorldState Create(ulong stateId, PlayerState[] players)
        {
            return new WorldState
            {
                ServerTime = Time.realtimeSinceStartup,
                StateId = stateId,
                Players = players
            };
        }
    }

    /// <summary>
    /// 网络命令
    /// </summary>
    [MemoryPack.MemoryPackable]
    public partial struct NetworkCommand
    {
        // 命令类型
        public byte CommandType { get; set; }

        // 目标ID
        public uint TargetId { get; set; }

        // 参数
        public float Param1 { get; set; }
        public float Param2 { get; set; }
        public float Param3 { get; set; }
        public float Param4 { get; set; }

        // 命令数据
        public byte[] Data { get; set; }

        // 时间戳
        public double Timestamp { get; set; }

        // 创建移动命令
        public static NetworkCommand CreateMoveCommand(uint playerId, Vector3 direction, float speed)
        {
            return new NetworkCommand
            {
                CommandType = 1, // 移动命令
                TargetId = playerId,
                Param1 = direction.x,
                Param2 = direction.y,
                Param3 = direction.z,
                Param4 = speed,
                Data = null,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };
        }

        // 创建动作命令
        public static NetworkCommand CreateActionCommand(uint playerId, byte actionId, byte[] actionParams = null)
        {
            return new NetworkCommand
            {
                CommandType = 2, // 动作命令
                TargetId = playerId,
                Param1 = actionId,
                Data = actionParams ?? Array.Empty<byte>(),
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };
        }
    }

    /// <summary>
    /// 网络事件
    /// </summary>
    [MemoryPack.MemoryPackable]
    public partial struct NetworkEvent
    {
        // 事件类型
        public byte EventType { get; set; }

        // 事件源
        public uint SourceId { get; set; }

        // 目标
        public uint TargetId { get; set; }

        // 事件数据
        public byte[] EventData { get; set; }

        // 时间戳
        public double Timestamp { get; set; }
    }

    /// <summary>
    /// 聊天消息
    /// </summary>
    [MemoryPack.MemoryPackable]
    public partial class ChatMessage
    {
        // 发送者ID
        public uint SenderId { get; set; }

        // 发送者名称
        public string SenderName { get; set; }

        // 接收者ID (0表示广播)
        public uint ReceiverId { get; set; }

        // 消息内容
        public string Content { get; set; }

        // 发送时间
        public double Timestamp { get; set; }

        // 频道
        public byte Channel { get; set; }
    }

    /// <summary>
    /// 自定义二进制数据类型
    /// </summary>
    [MemoryPack.MemoryPackable]
    public partial class BinaryPacket
    {
        // 数据类型
        public ushort PacketType { get; set; }

        // 源ID
        public uint SourceId { get; set; }

        // 二进制数据
        public byte[] Data { get; set; }

        // 压缩标志
        public bool Compressed { get; set; }

        // 附加属性
        public string[] Attributes { get; set; }

        // 默认构造函数
        public BinaryPacket()
        {
            Data = Array.Empty<byte>();
            Attributes = Array.Empty<string>();
        }
    }
}