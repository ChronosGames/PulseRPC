using System;

namespace PulseRPC.Sessions;

/// <summary>
/// 会话上下文访问器接口 - 提供当前会话上下文的访问
/// 符合三层抽象架构的应用层设计
/// </summary>
public interface ISessionContextAccessor
{
    /// <summary>
    /// 获取当前会话上下文
    /// </summary>
    IClientSession? Current { get; }

    /// <summary>
    /// 设置当前会话上下文
    /// </summary>
    /// <param name="session">会话上下文</param>
    void SetCurrent(IClientSession? session);
}

/// <summary>
/// 会话上下文访问器默认实现
/// 使用 AsyncLocal 确保线程安全的上下文传递
/// </summary>
public class SessionContextAccessor : ISessionContextAccessor
{
    private static readonly AsyncLocal<IClientSession?> _current = new();

    /// <summary>
    /// 获取当前会话上下文
    /// </summary>
    public IClientSession? Current => _current.Value;

    /// <summary>
    /// 设置当前会话上下文
    /// </summary>
    /// <param name="session">会话上下文</param>
    public void SetCurrent(IClientSession? session)
    {
        _current.Value = session;
    }
}