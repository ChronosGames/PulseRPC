using System;

namespace PulseRPC.Transport;

/// <summary>
/// 消息处理结果
/// </summary>
public struct ProcessingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    
    /// <summary>
    /// 成功结果
    /// </summary>
    public static ProcessingResult SuccessResult() => new() { Success = true };
    
    /// <summary>
    /// 失败结果
    /// </summary>
    public static ProcessingResult FailResult(string errorMessage) => new() 
    { 
        Success = false, 
        ErrorMessage = errorMessage 
    };
}