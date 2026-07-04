using System;

namespace PulseRPC.Shared;

/// <summary>
/// 消息处理结果
/// </summary>
public struct ProcessingResult
{
    public bool Success { get; set; }
    public object? Payload { get; set; }

    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }

    /// <summary>
    /// 成功结果
    /// </summary>
    public static ProcessingResult SuccessResult(object? payload) => new() { Success = true, Payload = payload };

    /// <summary>
    /// 失败结果
    /// </summary>
    public static ProcessingResult FailResult(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
