using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PulseRPC.Exceptions
{
    /// <summary>
    /// 握手异常
    /// </summary>
    [Serializable]
    public class HandshakeException : Exception
    {
        /// <summary>
        /// 握手阶段
        /// </summary>
        public HandshakeStage Stage { get; }

        /// <summary>
        /// 会话ID
        /// </summary>
        public uint ConversationId { get; }

        /// <summary>
        /// 远程端点
        /// </summary>
        public string? RemoteEndpoint { get; }

        /// <summary>
        /// 尝试次数
        /// </summary>
        public int AttemptCount { get; }

        /// <summary>
        /// 诊断信息
        /// </summary>
        public Dictionary<string, object> DiagnosticInfo { get; }

        /// <summary>
        /// 故障排除建议
        /// </summary>
        public List<string> TroubleshootingSuggestions { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public HandshakeException(string message, HandshakeStage stage, uint conversationId,
            string? remoteEndpoint = null, int attemptCount = 0) : base(message)
        {
            Stage = stage;
            ConversationId = conversationId;
            RemoteEndpoint = remoteEndpoint;
            AttemptCount = attemptCount;
            DiagnosticInfo = new Dictionary<string, object>();
            TroubleshootingSuggestions = new List<string>();

            GenerateTroubleshootingSuggestions();
        }

        /// <summary>
        /// 构造函数（带内部异常）
        /// </summary>
        public HandshakeException(string message, Exception innerException, HandshakeStage stage,
            uint conversationId, string? remoteEndpoint = null, int attemptCount = 0) : base(message, innerException)
        {
            Stage = stage;
            ConversationId = conversationId;
            RemoteEndpoint = remoteEndpoint;
            AttemptCount = attemptCount;
            DiagnosticInfo = new Dictionary<string, object>();
            TroubleshootingSuggestions = new List<string>();

            GenerateTroubleshootingSuggestions();
        }

        /// <summary>
        /// 序列化构造函数
        /// </summary>
        protected HandshakeException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Stage = (HandshakeStage)info.GetInt32(nameof(Stage));
            ConversationId = info.GetUInt32(nameof(ConversationId));
            RemoteEndpoint = info.GetString(nameof(RemoteEndpoint));
            AttemptCount = info.GetInt32(nameof(AttemptCount));
            DiagnosticInfo = (Dictionary<string, object>)(info.GetValue(nameof(DiagnosticInfo), typeof(Dictionary<string, object>)) ?? new Dictionary<string, object>());
            TroubleshootingSuggestions = (List<string>)(info.GetValue(nameof(TroubleshootingSuggestions), typeof(List<string>)) ?? new List<string>());
        }

        /// <summary>
        /// 序列化
        /// </summary>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(Stage), (int)Stage);
            info.AddValue(nameof(ConversationId), ConversationId);
            info.AddValue(nameof(RemoteEndpoint), RemoteEndpoint);
            info.AddValue(nameof(AttemptCount), AttemptCount);
            info.AddValue(nameof(DiagnosticInfo), DiagnosticInfo);
            info.AddValue(nameof(TroubleshootingSuggestions), TroubleshootingSuggestions);
        }

        /// <summary>
        /// 添加诊断信息
        /// </summary>
        public void AddDiagnosticInfo(string key, object value)
        {
            DiagnosticInfo[key] = value;
        }

        /// <summary>
        /// 添加故障排除建议
        /// </summary>
        public void AddTroubleshootingSuggestion(string suggestion)
        {
            if (!TroubleshootingSuggestions.Contains(suggestion))
            {
                TroubleshootingSuggestions.Add(suggestion);
            }
        }

        /// <summary>
        /// 生成故障排除建议
        /// </summary>
        private void GenerateTroubleshootingSuggestions()
        {
            switch (Stage)
            {
                case HandshakeStage.SendingHandshake:
                    TroubleshootingSuggestions.Add("检查网络连接是否正常");
                    TroubleshootingSuggestions.Add("确认服务器地址和端口配置正确");
                    TroubleshootingSuggestions.Add("检查防火墙是否阻止UDP通信");
                    break;

                case HandshakeStage.WaitingConfirmation:
                    TroubleshootingSuggestions.Add("检查服务器是否正在运行");
                    TroubleshootingSuggestions.Add("确认服务器监听正确的端口");
                    TroubleshootingSuggestions.Add("检查ConversationId配置是否匹配");
                    TroubleshootingSuggestions.Add("尝试增加握手超时时间");
                    break;

                case HandshakeStage.ProcessingConfirmation:
                    TroubleshootingSuggestions.Add("检查网络包是否被篡改");
                    TroubleshootingSuggestions.Add("确认客户端和服务端使用相同的协议版本");
                    break;

                default:
                    TroubleshootingSuggestions.Add("检查网络连接");
                    TroubleshootingSuggestions.Add("重试连接");
                    break;
            }

            // 基于尝试次数的建议
            if (AttemptCount > 1)
            {
                TroubleshootingSuggestions.Add("多次重试失败，可能存在持续性网络问题");
                TroubleshootingSuggestions.Add("检查网络质量和延迟");
            }
        }

        /// <summary>
        /// 获取详细的错误描述
        /// </summary>
        public string GetDetailedDescription()
        {
            var description = $"KCP握手失败: {Message}\n";
            description += $"阶段: {Stage}\n";
            description += $"会话ID: {ConversationId}\n";

            if (!string.IsNullOrEmpty(RemoteEndpoint))
                description += $"远程端点: {RemoteEndpoint}\n";

            if (AttemptCount > 0)
                description += $"尝试次数: {AttemptCount}\n";

            if (DiagnosticInfo.Count > 0)
            {
                description += "\n诊断信息:\n";
                foreach (var kvp in DiagnosticInfo)
                {
                    description += $"  {kvp.Key}: {kvp.Value}\n";
                }
            }

            if (TroubleshootingSuggestions.Count > 0)
            {
                description += "\n故障排除建议:\n";
                foreach (var suggestion in TroubleshootingSuggestions)
                {
                    description += $"  • {suggestion}\n";
                }
            }

            return description;
        }
    }

    /// <summary>
    /// 握手阶段
    /// </summary>
    public enum HandshakeStage
    {
        /// <summary>
        /// 未知阶段
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 发送握手包
        /// </summary>
        SendingHandshake = 1,

        /// <summary>
        /// 等待确认
        /// </summary>
        WaitingConfirmation = 2,

        /// <summary>
        /// 处理确认
        /// </summary>
        ProcessingConfirmation = 3,

        /// <summary>
        /// 握手完成
        /// </summary>
        Completed = 4
    }
}
