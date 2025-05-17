namespace PulseRPC;

// 线程处理策略枚举
public enum HandlerThreadingPolicy
{
    // 在工作线程池中处理
    WorkerThread,

    // 在专用高优先级线程处理
    HighPriorityThread,

    // 在专用低延迟线程处理（适用于战斗逻辑）
    LowLatencyThread,

    // 在主线程处理（适用于维护全局状态的操作）
    MainThread
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class HandlerAttribute(bool isInternal = false) : Attribute
{
    // 可选：处理优先级
    public int Priority { get; set; } = 0;

    // 可选：处理线程策略
    public HandlerThreadingPolicy ThreadingPolicy { get; set; } = HandlerThreadingPolicy.WorkerThread;
}

