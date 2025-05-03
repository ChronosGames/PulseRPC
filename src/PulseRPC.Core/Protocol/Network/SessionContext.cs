using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PulseRPC.Protocol.Network
{
    /// <summary>
    /// 表示客户端会话上下文
    /// </summary>
    public class SessionContext
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly Dictionary<string, object> _items = new Dictionary<string, object>();

        /// <summary>
        /// 会话唯一标识
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// 会话创建时间
        /// </summary>
        public DateTime CreatedTime { get; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivityTime { get; private set; }

        /// <summary>
        /// 初始化会话上下文
        /// </summary>
        /// <param name="client">TCP客户端</param>
        public SessionContext(TcpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _stream = client.GetStream();
            Id = Guid.NewGuid();
            CreatedTime = DateTime.UtcNow;
            LastActivityTime = CreatedTime;
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="data">序列化后的消息数据</param>
        public async Task SendAsync(int messageId, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            // 构建消息
            var packet = TcpProtocol.BuildPacket(messageId, data);

            // 发送数据
            await _stream.WriteAsync(packet, 0, packet.Length);

            // 更新活动时间
            LastActivityTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 设置会话数据项
        /// </summary>
        /// <param name="key">键</param>
        /// <param name="value">值</param>
        public void SetItem(string key, object value)
        {
            _items[key] = value;
        }

        /// <summary>
        /// 获取会话数据项
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="key">键</param>
        /// <returns>数据值，如果不存在则返回默认值</returns>
        public T? GetItem<T>(string key)
        {
            if (_items.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        /// <summary>
        /// 移除会话数据项
        /// </summary>
        /// <param name="key">键</param>
        /// <returns>是否成功移除</returns>
        public bool RemoveItem(string key)
        {
            return _items.Remove(key);
        }

        /// <summary>
        /// 关闭会话
        /// </summary>
        public void Close()
        {
            _stream?.Close();
            _client?.Close();
        }
    }
}
