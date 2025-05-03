using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using PulseRPC.Protocol.Serialization;

namespace PulseRPC.Protocol.Network
{
    /// <summary>
    /// TCP协议处理类，包含消息打包和解包逻辑
    /// </summary>
    public static class TcpProtocol
    {
        // 消息头长度（4字节消息长度 + 4字节消息ID）
        private const int HeaderLength = 8;

        /// <summary>
        /// 构建消息包
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="messageData">消息数据</param>
        /// <returns>完整的消息包数据</returns>
        public static byte[] BuildPacket(int messageId, byte[] messageData)
        {
            // 消息总长度 = 消息头长度 + 消息体长度
            int totalLength = HeaderLength + messageData.Length;

            // 创建结果数组
            byte[] result = new byte[totalLength];

            // 写入数据长度（不包括长度字段本身的4字节）
            BitConverter.GetBytes(totalLength - 4).CopyTo(result, 0);

            // 写入消息ID
            BitConverter.GetBytes(messageId).CopyTo(result, 4);

            // 写入消息体
            messageData.CopyTo(result, HeaderLength);

            return result;
        }

        /// <summary>
        /// 从网络流中读取一个完整的消息
        /// </summary>
        /// <param name="stream">网络流</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>消息ID和消息体元组</returns>
        public static async Task<(int MessageId, byte[] Data)> ReadMessageAsync(NetworkStream stream, CancellationToken cancellationToken = default)
        {
            // 读取消息头
            byte[] headerBuffer = new byte[HeaderLength];
            await ReadExactBytesAsync(stream, headerBuffer, 0, HeaderLength, cancellationToken);

            // 解析消息长度（总长度-4字节长度字段）
            int messageLength = BitConverter.ToInt32(headerBuffer, 0);

            // 解析消息ID
            int messageId = BitConverter.ToInt32(headerBuffer, 4);

            // 计算消息体长度
            int bodyLength = messageLength - (HeaderLength - 4);

            // 读取消息体
            byte[] bodyBuffer = new byte[bodyLength];
            await ReadExactBytesAsync(stream, bodyBuffer, 0, bodyLength, cancellationToken);

            return (messageId, bodyBuffer);
        }

        /// <summary>
        /// 从流中准确读取指定字节数的数据
        /// </summary>
        /// <param name="stream">网络流</param>
        /// <param name="buffer">目标缓冲区</param>
        /// <param name="offset">偏移量</param>
        /// <param name="count">要读取的字节数</param>
        /// <param name="cancellationToken">取消令牌</param>
        private static async Task ReadExactBytesAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = 0;
            while (bytesRead < count)
            {
                int readResult = await stream.ReadAsync(buffer, offset + bytesRead, count - bytesRead, cancellationToken);
                if (readResult == 0)
                {
                    // 连接已关闭
                    throw new EndOfStreamException("远程连接已关闭");
                }

                bytesRead += readResult;
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="stream">网络流</param>
        /// <param name="message">消息对象</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task SendMessageAsync<T>(NetworkStream stream, T message, CancellationToken cancellationToken = default)
            where T : class, IMessage
        {
            // 获取消息ID
            int messageId = MessageRegistry.GetMessageId<T>();

            // 序列化消息
            byte[] data = MessageSerializer.Serialize(message);

            // 构建完整消息包
            byte[] packet = BuildPacket(messageId, data);

            // 发送消息
            await stream.WriteAsync(packet, 0, packet.Length, cancellationToken);
        }
    }
}
