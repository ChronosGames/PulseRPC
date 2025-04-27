using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using UnityEngine;
// ReSharper disable InconsistentNaming
// ReSharper disable ConvertToAutoPropertyWhenPossible

namespace UnityTCP.Serialization
{
    /// <summary>
    /// 网络写入器 - 提供高性能写入基本类型的方法
    /// </summary>
    public ref struct NetworkWriter
    {
        private Span<byte> _buffer;
        private int _position;

        public int Position => _position;

        public NetworkWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _position = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buffer[_position..], value);
            _position += sizeof(int);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_position..], value);
            _position += sizeof(uint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteFloat(float value)
        {
            // WriteUInt32(BitConverter.SingleToUInt32Bits(value));
            WriteUInt32(SingleToUInt32Bits(value));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe uint SingleToUInt32Bits(float value)
        {
            return *(uint*)&value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteDouble(double value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(_buffer[_position..], BitConverter.DoubleToInt64Bits(value));
            _position += sizeof(double);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBool(bool value)
        {
            _buffer[_position++] = (byte)(value ? 1 : 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value)
        {
            _buffer[_position++] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteBytes(ReadOnlySpan<byte> data)
        {
            data.CopyTo(_buffer[_position..]);
            _position += data.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteInt32(0);
                return;
            }

            var byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
            WriteInt32(byteCount);

            System.Text.Encoding.UTF8.GetBytes(value, _buffer[_position..]);
            _position += byteCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteNetworkObject<T>(T value) where T : struct, INetworkSerializable
        {
            value.Serialize(ref this);
        }

        // 支持Unity Vector3等常用类型的直接序列化
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteVector3(Vector3 value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteQuaternion(Quaternion value)
        {
            WriteFloat(value.x);
            WriteFloat(value.y);
            WriteFloat(value.z);
            WriteFloat(value.w);
        }
    }

}
