using MemoryPack;
using System;

namespace PulseRPC.Messaging
{
    /// <summary>
    /// 错误响应类型
    /// </summary>
    [MemoryPackable]
    public partial class ErrorResponse
    {
        /// <summary>
        /// 错误代码
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 错误详情
        /// </summary>
        public string? ErrorDetails { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>
        /// 创建错误响应
        /// </summary>
        public static ErrorResponse Create(string errorCode, string errorMessage, string? errorDetails = null)
        {
            return new ErrorResponse
            {
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                ErrorDetails = errorDetails
            };
        }
    }

    /// <summary>
    /// 成功响应类型（用于只需要表示成功的场景）
    /// </summary>
    [MemoryPackable]
    public readonly partial struct SuccessResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public long Timestamp { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public SuccessResponse(bool success, long timestamp)
        {
            Success = success;
            Timestamp = timestamp;
        }

        /// <summary>
        /// 静态成功实例
        /// </summary>
        public static readonly SuccessResponse Instance = new(true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static SuccessResponse Create() => new(true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Pong 响应类型
    /// </summary>
    [MemoryPackable]
    public readonly partial struct PongResponse
    {
        /// <summary>
        /// 时间戳
        /// </summary>
        public long Timestamp { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public PongResponse(long timestamp)
        {
            Timestamp = timestamp;
        }

        /// <summary>
        /// 创建 Pong 响应
        /// </summary>
        public static PongResponse Create() => new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        /// <summary>
        /// 静态实例
        /// </summary>
        public static readonly PongResponse Instance = Create();
    }
}
