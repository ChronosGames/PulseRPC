using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityTCP.Serialization
{
    /// <summary>
    /// 网络读取器 - 提供高性能读取基本类型的方法
    /// </summary>
    public ref struct NetworkReader
    {
        private ReadOnlySpan<byte> _buffer;
        private int _position;

        public int Position => _position;

        public NetworkReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.Slice(_position));
            _position += sizeof(int);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.Slice(_position));
            _position += sizeof(uint);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat()
        {
            // return BitConverter.UInt32BitsToSingle(ReadUInt32());
            return UInt32BitsToSingle(ReadUInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe float UInt32BitsToSingle(uint value)
        {
            return *(float*)&value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            long value = BinaryPrimitives.ReadInt64LittleEndian(_buffer[_position..]);
            _position += sizeof(double);
            return BitConverter.Int64BitsToDouble(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool()
        {
            return _buffer[_position++] != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            return _buffer[_position++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            var result = _buffer.Slice(_position, length);
            _position += length;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString()
        {
            int length = ReadInt32();
            if (length == 0)
            {
                return string.Empty;
            }

            string result = System.Text.Encoding.UTF8.GetString(_buffer.Slice(_position, length));
            _position += length;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ReadNetworkObject<T>() where T : struct, INetworkSerializable
        {
            T value = new T();
            value.Deserialize(ref this);
            return value;
        }

        // 支持Unity Vector3等常用类型的直接序列化
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ReadVector3()
        {
            return new Vector3(ReadFloat(), ReadFloat(), ReadFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Quaternion ReadQuaternion()
        {
            return new Quaternion(ReadFloat(), ReadFloat(), ReadFloat(), ReadFloat());
        }
    }
}
